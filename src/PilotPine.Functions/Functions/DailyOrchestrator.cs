using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Functions;

/// <summary>
/// Orquestador principal con Durable Functions.
///
/// Durable Functions guarda checkpoint después de cada await.
/// Si una tool falla, al reintentar NO repite los pasos anteriores.
///
/// Ejemplo de recovery:
///   1. GetKeywords     ✓ (checkpoint)
///   2. GenerateArticle ✓ (checkpoint - Claude ya cobró, no se repite)
///   3. PublishToWP     ✗ (falla aquí)
///   → Al reiniciar, salta directo al paso 3 con los datos guardados.
/// </summary>
public class DailyOrchestrator
{
    private readonly Kernel _kernel;
    private readonly FoundryModelProvider _foundryProvider;
    private readonly StateManager _stateManager;
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(
        Kernel kernel,
        FoundryModelProvider foundryProvider,
        StateManager stateManager,
        ILogger<DailyOrchestrator> logger)
    {
        _kernel = kernel;
        _foundryProvider = foundryProvider;
        _stateManager = stateManager;
        _logger = logger;
    }

    // ─── Timer Trigger: 6 AM UTC diario ────────────────────────────
    [Function("DailyTrigger")]
    public async Task DailyTrigger(
        [TimerTrigger("0 0 6 * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client)
    {
        var instanceId = $"daily-{DateTime.UtcNow:yyyy-MM-dd}";

        var existing = await client.GetInstanceAsync(instanceId);
        if (existing?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        {
            _logger.LogWarning("Orchestration already running: {Id}", instanceId);
            return;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(MainOrchestration),
            new PipelineInput { ArticleCount = 3 },
            new StartOrchestrationOptions { InstanceId = instanceId }
        );

        _logger.LogInformation("Started orchestration: {Id}", instanceId);
    }

    // ─── HTTP Trigger: Para testing manual ─────────────────────────
    [Function("ManualTrigger")]
    public async Task<HttpResponseData> ManualTrigger(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var instanceId = $"manual-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}";

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(MainOrchestration),
            new PipelineInput { ArticleCount = 1 },
            new StartOrchestrationOptions { InstanceId = instanceId }
        );

        var response = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new { instanceId, status = "started" });
        return response;
    }

    // ─── Orquestación Principal ────────────────────────────────────
    [Function(nameof(MainOrchestration))]
    public async Task<PipelineResult> MainOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<PipelineInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();
        var results = new List<ArticleResult>();

        // Paso 1: Research keywords
        // >>> Checkpoint: si falla después, no repite el research <<<
        var keywords = await context.CallActivityAsync<List<KeywordResult>>(
            nameof(GetKeywordsActivity),
            input!.ArticleCount
        );

        logger.LogInformation("Keywords found: {Count}", keywords.Count);

        // Paso 2: Procesar cada keyword como sub-orquestación
        foreach (var kw in keywords)
        {
            // Cada artículo tiene sus propios checkpoints internos.
            // Si el pin falla, el artículo ya publicado no se repite.
            var result = await context.CallSubOrchestrationAsync<ArticleResult>(
                nameof(ProcessArticleOrchestration),
                new ArticleInput { Keyword = kw.Keyword, ArticleType = kw.ArticleType }
            );
            results.Add(result);
        }

        // Paso 3: Guardar resultados del día
        await context.CallActivityAsync(
            nameof(SaveResultsActivity),
            results
        );

        return new PipelineResult
        {
            Date = context.CurrentUtcDateTime,
            ArticlesPublished = results.Count(r => r.Success),
            TotalPinsCreated = results.Sum(r => r.PinsCreated),
            Errors = results.Where(r => !r.Success).Select(r => r.Error!).ToList()
        };
    }

    // ─── Sub-Orquestación por Artículo ─────────────────────────────
    [Function(nameof(ProcessArticleOrchestration))]
    public async Task<ArticleResult> ProcessArticleOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ArticleInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        // Retry policies diferenciadas por tipo de operación
        var llmRetry = new TaskRetryOptions(
            firstRetryInterval: TimeSpan.FromSeconds(30),
            maxNumberOfAttempts: 3)
        {
            BackoffCoefficient = 1.5
        };

        var apiRetry = new TaskRetryOptions(
            firstRetryInterval: TimeSpan.FromSeconds(10),
            maxNumberOfAttempts: 5)
        {
            BackoffCoefficient = 2.0,
            MaxRetryInterval = TimeSpan.FromMinutes(3)
        };

        try
        {
            // Paso 1: Generar artículo (LLM - costoso, se cachea en checkpoint)
            var article = await context.CallActivityAsync<Article>(
                nameof(GenerateArticleActivity),
                input,
                new TaskActivityOptions { Retry = llmRetry }
            );
            logger.LogInformation("Article generated: {Title}", article.Title);

            // Paso 2: Publicar en WordPress (API externa)
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article,
                new TaskActivityOptions { Retry = apiRetry }
            );

            if (!wpResult.Success)
            {
                return new ArticleResult
                {
                    Success = false,
                    Keyword = input!.Keyword,
                    Error = wpResult.Error
                };
            }
            logger.LogInformation("Published: {Url}", wpResult.PostUrl);

            // Paso 3: Crear pins (API externa)
            var pinResult = await context.CallActivityAsync<PinResult>(
                nameof(CreatePinActivity),
                new { Title = article.Title, PostUrl = wpResult.PostUrl, Keyword = input!.Keyword },
                new TaskActivityOptions { Retry = apiRetry }
            );

            return new ArticleResult
            {
                Success = true,
                Keyword = input.Keyword,
                PostUrl = wpResult.PostUrl,
                PinsCreated = pinResult.Success ? 1 : 0
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed: {Keyword}", input!.Keyword);
            return new ArticleResult
            {
                Success = false,
                Keyword = input.Keyword,
                Error = ex.Message
            };
        }
    }

    // ─── Activities ────────────────────────────────────────────────
    // Cada Activity es una unidad atómica de trabajo.
    // Durable Functions serializa su output como checkpoint.

    [Function(nameof(GetKeywordsActivity))]
    public async Task<List<KeywordResult>> GetKeywordsActivity(
        [ActivityTrigger] int count)
    {
        // Cargar keywords ya publicadas del state
        var published = await _stateManager.LoadAsync<HashSet<string>>("published-keywords")
            ?? [];

        // Obtener keywords del tool de research
        var researchTools = _kernel.Plugins["Research"];
        var result = await _kernel.InvokeAsync(
            researchTools["GetKeywords"],
            new() { ["count"] = count * 2 }  // Pedir el doble para filtrar
        );

        var all = result.GetValue<List<KeywordResult>>() ?? [];

        // Filtrar las ya publicadas
        return all
            .Where(k => !published.Contains(k.Keyword.ToLowerInvariant()))
            .Take(count)
            .ToList();
    }

    [Function(nameof(GenerateArticleActivity))]
    public async Task<Article> GenerateArticleActivity(
        [ActivityTrigger] ArticleInput input)
    {
        // TODO: Implementar con ChatCompletionAgent cuando ContentTools esté listo.
        // Por ahora retorna un placeholder para que la estructura compile.
        _logger.LogInformation("Generating article for: {Keyword}", input.Keyword);

        return new Article
        {
            Title = $"Guide: {input.Keyword}",
            Content = $"<p>Article about {input.Keyword} - pending real implementation</p>",
            MetaDescription = $"Discover {input.Keyword}",
            Category = "travel",
            Tags = input.Keyword.Split(' ')
        };
    }

    [Function(nameof(PublishToWordPressActivity))]
    public async Task<PublishResult> PublishToWordPressActivity(
        [ActivityTrigger] Article article)
    {
        // TODO: Implementar con WordPressTools
        _logger.LogInformation("Publishing: {Title}", article.Title);
        return new PublishResult { Success = false, Error = "WordPressTools not implemented yet" };
    }

    [Function(nameof(CreatePinActivity))]
    public Task<PinResult> CreatePinActivity(
        [ActivityTrigger] object input)
    {
        // TODO: Implementar con PinterestTools
        _logger.LogInformation("Creating pin (not implemented yet)");
        return Task.FromResult(new PinResult { Success = false, Error = "PinterestTools not implemented yet" });
    }

    [Function(nameof(SaveResultsActivity))]
    public async Task SaveResultsActivity(
        [ActivityTrigger] List<ArticleResult> results)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await _stateManager.SaveAsync($"daily-results/{date}", results);

        // Actualizar lista de keywords publicadas
        var successKeywords = results
            .Where(r => r.Success)
            .Select(r => r.Keyword.ToLowerInvariant());

        await _stateManager.UpdateAsync(
            "published-keywords",
            new HashSet<string>(),
            existing =>
            {
                foreach (var kw in successKeywords)
                    existing.Add(kw);
                return existing;
            }
        );

        _logger.LogInformation(
            "Daily results saved: {Success}/{Total}",
            results.Count(r => r.Success),
            results.Count
        );
    }
}
