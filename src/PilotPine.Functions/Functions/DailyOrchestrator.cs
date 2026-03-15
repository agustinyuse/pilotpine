using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DurableTask;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Models;
using PilotPine.Functions.Tools;

namespace PilotPine.Functions.Functions;

/// <summary>
/// Orquestador principal con Durable Functions + Agent Framework.
///
/// Approach híbrido:
///   - GetKeywords: directo (mecánico)
///   - GenerateArticle: DurableAIAgent en la orquestación (checkpointed)
///   - PublishToWordPress: directo (mecánico)
///   - GenerateImages + CreatePins: directo (mecánico)
/// </summary>
public class DailyOrchestrator
{
    private readonly StateManager _stateManager;
    private readonly ResearchTools _researchTools;
    private readonly ContentTools _contentTools;
    private readonly WordPressTools _wordPressTools;
    private readonly PinterestTools _pinterestTools;
    private readonly ImageTools _imageTools;
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(
        StateManager stateManager,
        ResearchTools researchTools,
        ContentTools contentTools,
        WordPressTools wordPressTools,
        PinterestTools pinterestTools,
        ImageTools imageTools,
        ILogger<DailyOrchestrator> logger)
    {
        _stateManager = stateManager;
        _researchTools = researchTools;
        _contentTools = contentTools;
        _wordPressTools = wordPressTools;
        _pinterestTools = pinterestTools;
        _imageTools = imageTools;
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

    // ─── HTTP Trigger: Testing manual ──────────────────────────────
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

    // ═══════════════════════════════════════════════════════════════
    // ORCHESTRATIONS
    // ═══════════════════════════════════════════════════════════════

    [Function(nameof(MainOrchestration))]
    public async Task<PipelineResult> MainOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<PipelineInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();
        var results = new List<ArticleResult>();

        // Paso 1: Research keywords (directo, mecánico)
        var keywords = await context.CallActivityAsync<List<KeywordResult>>(
            nameof(GetKeywordsActivity),
            input!.ArticleCount
        );
        logger.LogInformation("Keywords found: {Count}", keywords.Count);

        // Paso 2: Procesar cada keyword (sub-orquestación con checkpoints)
        foreach (var kw in keywords)
        {
            var result = await context.CallSubOrchestratorAsync<ArticleResult>(
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

    [Function(nameof(ProcessArticleOrchestration))]
    public async Task<ArticleResult> ProcessArticleOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ArticleInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        var apiRetry = new RetryPolicy(
            maxNumberOfAttempts: 5,
            firstRetryInterval: TimeSpan.FromSeconds(10),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromMinutes(3));

        try
        {
            // Paso 1: Generar artículo con DurableAIAgent (checkpointed por el framework)
            DurableAIAgent agent = context.GetAgent("ContentWriter");
            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse<Article> response = await agent.RunAsync<Article>(
                message: $"Write a {input!.ArticleType} about: {input.Keyword}",
                session: session);

            Article article = response.Result
                ?? throw new InvalidOperationException("Agent did not return structured Article output");
            logger.LogInformation("Article generated: {Title}", article.Title);

            // Paso 2: Generar imágenes para pins (directo, mecánico)
            var pinVariations = await context.CallActivityAsync<List<PinVariation>>(
                nameof(GeneratePinImagesActivity),
                new PinImageInput { ArticleTitle = article.Title, Keyword = input.Keyword }
            );
            logger.LogInformation("Pin images generated: {Count}", pinVariations.Count);

            // Paso 3: Publicar en WordPress (directo, mecánico)
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article,
                TaskOptions.FromRetryPolicy(apiRetry)
            );

            if (!wpResult.Success)
            {
                logger.LogWarning("WordPress failed: {Error}", wpResult.Error);
                return new ArticleResult
                {
                    Success = false,
                    Keyword = input.Keyword,
                    Error = wpResult.Error
                };
            }
            logger.LogInformation("Published: {Url}", wpResult.PostUrl);

            // Paso 4: Crear pins en Pinterest (directo, mecánico)
            var pinResults = await context.CallActivityAsync<List<PinResult>>(
                nameof(CreatePinsActivity),
                new CreatePinsInput
                {
                    PostUrl = wpResult.PostUrl!,
                    Description = article.MetaDescription,
                    Variations = pinVariations
                },
                TaskOptions.FromRetryPolicy(apiRetry)
            );

            var successPins = pinResults.Count(p => p.Success);
            logger.LogInformation("Pins created: {Success}/{Total}", successPins, pinResults.Count);

            return new ArticleResult
            {
                Success = true,
                Keyword = input.Keyword,
                PostUrl = wpResult.PostUrl,
                PinsCreated = successPins
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

    // ═══════════════════════════════════════════════════════════════
    // ACTIVITIES - Cada una es una unidad atómica con checkpoint
    // ═══════════════════════════════════════════════════════════════

    [Function(nameof(GetKeywordsActivity))]
    public async Task<List<KeywordResult>> GetKeywordsActivity(
        [ActivityTrigger] int count)
    {
        return await _researchTools.GetKeywords(count);
    }

    [Function(nameof(GeneratePinImagesActivity))]
    public async Task<List<PinVariation>> GeneratePinImagesActivity(
        [ActivityTrigger] PinImageInput input)
    {
        var headlines = await _contentTools.GeneratePinHeadlines(input.ArticleTitle, 3);
        return await _imageTools.GeneratePinVariationsAsync(input.ArticleTitle, input.Keyword, headlines);
    }

    [Function(nameof(PublishToWordPressActivity))]
    public async Task<PublishResult> PublishToWordPressActivity(
        [ActivityTrigger] Article article)
    {
        if (await _wordPressTools.PostExistsAsync(article.Title))
        {
            _logger.LogWarning("Article already exists: {Title}", article.Title);
            return new PublishResult { Success = false, Error = "Article with similar title already exists" };
        }

        return await _wordPressTools.PublishPostAsync(article);
    }

    [Function(nameof(CreatePinsActivity))]
    public async Task<List<PinResult>> CreatePinsActivity(
        [ActivityTrigger] CreatePinsInput input)
    {
        return await _pinterestTools.CreatePinsWithRateLimitAsync(
            input.PostUrl,
            input.Description,
            input.Variations
        );
    }

    [Function(nameof(SaveResultsActivity))]
    public async Task SaveResultsActivity(
        [ActivityTrigger] List<ArticleResult> results)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await _stateManager.SaveAsync($"daily-results/{date}", results);

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

        _logger.LogInformation("Results saved: {Success}/{Total}",
            results.Count(r => r.Success), results.Count);
    }
}

// ─── Input records para Activities ──────────────────────────────

public record PinImageInput
{
    public string ArticleTitle { get; init; } = "";
    public string Keyword { get; init; } = "";
}

public record CreatePinsInput
{
    public string PostUrl { get; init; } = "";
    public string Description { get; init; } = "";
    public List<PinVariation> Variations { get; init; } = [];
}
