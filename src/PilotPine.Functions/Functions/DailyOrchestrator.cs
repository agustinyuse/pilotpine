using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Models;
using PilotPine.Functions.Tools;

namespace PilotPine.Functions.Functions;

/// <summary>
/// Orquestador principal con Durable Functions.
///
/// Approach híbrido (ver docs/TOOLS-VS-DIRECT-CALLS.md):
///   - GetKeywords: directo (mecánico)
///   - GenerateArticle: AGENT + TOOLS (el LLM decide estructura/contenido)
///   - PublishToWordPress: directo (mecánico)
///   - GenerateImages + CreatePins: directo (mecánico)
///
/// Durable Functions guarda checkpoint después de cada await.
/// Si falla en paso 3, NO repite el paso 2 (que costó tokens).
/// </summary>
public class DailyOrchestrator
{
    private readonly Kernel _kernel;
    private readonly FoundryModelProvider _foundryProvider;
    private readonly StateManager _stateManager;
    private readonly ResearchTools _researchTools;
    private readonly ContentTools _contentTools;
    private readonly WordPressTools _wordPressTools;
    private readonly PinterestTools _pinterestTools;
    private readonly ImageTools _imageTools;
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(
        Kernel kernel,
        FoundryModelProvider foundryProvider,
        StateManager stateManager,
        ResearchTools researchTools,
        ContentTools contentTools,
        WordPressTools wordPressTools,
        PinterestTools pinterestTools,
        ImageTools imageTools,
        ILogger<DailyOrchestrator> logger)
    {
        _kernel = kernel;
        _foundryProvider = foundryProvider;
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

    [Function(nameof(ProcessArticleOrchestration))]
    public async Task<ArticleResult> ProcessArticleOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ArticleInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        var llmRetry = new TaskRetryOptions(
            firstRetryInterval: TimeSpan.FromSeconds(30),
            maxNumberOfAttempts: 3)
        { BackoffCoefficient = 1.5 };

        var apiRetry = new TaskRetryOptions(
            firstRetryInterval: TimeSpan.FromSeconds(10),
            maxNumberOfAttempts: 5)
        { BackoffCoefficient = 2.0, MaxRetryInterval = TimeSpan.FromMinutes(3) };

        try
        {
            // Paso 1: Generar artículo con AGENT + TOOLS (LLM decide contenido)
            // >>> Checkpoint: $0.06 de Claude, no se repite si falla después <<<
            var article = await context.CallActivityAsync<Article>(
                nameof(GenerateArticleActivity),
                input,
                new TaskActivityOptions { Retry = llmRetry }
            );
            logger.LogInformation("Article generated: {Title}", article.Title);

            // Paso 2: Generar imágenes para pins (directo, mecánico)
            var pinVariations = await context.CallActivityAsync<List<PinVariation>>(
                nameof(GeneratePinImagesActivity),
                new PinImageInput { ArticleTitle = article.Title, Keyword = input!.Keyword }
            );
            logger.LogInformation("Pin images generated: {Count}", pinVariations.Count);

            // Paso 3: Publicar en WordPress (directo, mecánico)
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article,
                new TaskActivityOptions { Retry = apiRetry }
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
                new TaskActivityOptions { Retry = apiRetry }
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
        // Llamada directa al ResearchTools (no necesita el LLM para esto)
        return await _researchTools.GetKeywords(count);
    }

    [Function(nameof(GenerateArticleActivity))]
    public async Task<Article> GenerateArticleActivity(
        [ActivityTrigger] ArticleInput input)
    {
        // ─── Approach: Agent + Tools ────────────────────────────────
        // Creamos un kernel específico con los ContentTools registrados.
        // El agente (Claude) genera el contenido y llama a
        // CreateArticleStructure para empaquetarlo.

        var kernel = _foundryProvider.CreateKernelForModel();
        kernel.Plugins.AddFromObject(_contentTools, "Content");

        var agent = new ChatCompletionAgent
        {
            Name = "ContentWriter",
            Instructions = $"""
                You are an expert travel content writer.

                Your task: Create a {input.ArticleType} article about "{input.Keyword}"

                Requirements:
                - Catchy title including the current year
                - 1500-2000 words in HTML format
                - Use [HOTEL_LINK] where you recommend hotels
                - Use [TOUR_LINK] where you recommend tours/activities
                - Meta description of max 155 characters
                - Informative but friendly tone
                - Include practical tips and insider knowledge

                IMPORTANT: After generating the content, call the CreateArticleStructure
                tool to save the article with all fields filled in.
                """,
            Kernel = kernel,
            Arguments = new KernelArguments(
                new PromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                }
            )
        };

        var chat = new ChatHistory();
        chat.AddUserMessage($"Write a {input.ArticleType} about: {input.Keyword}");

        // El agente genera contenido y llama a CreateArticleStructure
        Article? result = null;
        await foreach (var response in agent.InvokeAsync(chat))
        {
            // Intentar extraer el Article del último tool call
            if (response.Content != null)
            {
                _logger.LogDebug("Agent response: {Content}", response.Content[..Math.Min(200, response.Content.Length)]);
            }
        }

        // Si el agente no llamó al tool, generar directamente
        if (result == null)
        {
            _logger.LogWarning("Agent did not call CreateArticleStructure, using direct generation");

            // Fallback: llamada directa al LLM para generar contenido
            var chatService = kernel.GetRequiredService<IChatCompletionService>();
            var directChat = new ChatHistory();
            directChat.AddSystemMessage(
                "You are a travel content writer. Write in HTML format. " +
                "Use [HOTEL_LINK] for hotel recommendations and [TOUR_LINK] for tour recommendations.");
            directChat.AddUserMessage(
                $"Write a {input.ArticleType} article about: {input.Keyword}. " +
                "Include a title, 1500 words of content, and a meta description.");

            var directResponse = await chatService.GetChatMessageContentAsync(directChat);
            var content = directResponse.Content ?? "";

            result = await _contentTools.CreateArticleStructure(
                input.Keyword,
                input.ArticleType,
                $"Best {input.Keyword} Guide {DateTime.UtcNow.Year}",
                content,
                $"Discover the best {input.Keyword}. Local tips and insider secrets."
            );
        }

        return result;
    }

    [Function(nameof(GeneratePinImagesActivity))]
    public async Task<List<PinVariation>> GeneratePinImagesActivity(
        [ActivityTrigger] PinImageInput input)
    {
        // Directo: generar headlines y luego imágenes
        var headlines = await _contentTools.GeneratePinHeadlines(input.ArticleTitle, 3);
        return await _imageTools.GeneratePinVariationsAsync(input.ArticleTitle, input.Keyword, headlines);
    }

    [Function(nameof(PublishToWordPressActivity))]
    public async Task<PublishResult> PublishToWordPressActivity(
        [ActivityTrigger] Article article)
    {
        // Directo: verificar duplicado, luego publicar
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
        // Directo: crear pins con rate limiting
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

        // Actualizar keywords publicadas
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
