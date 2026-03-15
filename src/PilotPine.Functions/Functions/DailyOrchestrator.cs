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
///   - Product posts: ML trends → search → ProductWriter agent → WP → Pinterest
/// </summary>
public class DailyOrchestrator
{
    private readonly StateManager _stateManager;
    private readonly ResearchTools _researchTools;
    private readonly ContentTools _contentTools;
    private readonly WordPressTools _wordPressTools;
    private readonly PinterestTools _pinterestTools;
    private readonly ImageTools _imageTools;
    private readonly MercadoLibreTools _mlTools;
    private readonly MercadoLibreContentTools _mlContentTools;
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(
        StateManager stateManager,
        ResearchTools researchTools,
        ContentTools contentTools,
        WordPressTools wordPressTools,
        PinterestTools pinterestTools,
        ImageTools imageTools,
        MercadoLibreTools mlTools,
        MercadoLibreContentTools mlContentTools,
        ILogger<DailyOrchestrator> logger)
    {
        _stateManager = stateManager;
        _researchTools = researchTools;
        _contentTools = contentTools;
        _wordPressTools = wordPressTools;
        _pinterestTools = pinterestTools;
        _imageTools = imageTools;
        _mlTools = mlTools;
        _mlContentTools = mlContentTools;
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
        var articleResults = new List<ArticleResult>();
        var productResults = new List<ProductPostResult>();

        // ═══ Travel Articles Pipeline ═══

        // Paso 1: Research keywords de viaje (directo, mecánico)
        var keywords = await context.CallActivityAsync<List<KeywordResult>>(
            nameof(GetKeywordsActivity),
            input!.ArticleCount
        );
        logger.LogInformation("Travel keywords found: {Count}", keywords.Count);

        // Paso 2: Procesar cada keyword de viaje (sub-orquestación con checkpoints)
        foreach (var kw in keywords)
        {
            var result = await context.CallSubOrchestratorAsync<ArticleResult>(
                nameof(ProcessArticleOrchestration),
                new ArticleInput { Keyword = kw.Keyword, ArticleType = kw.ArticleType }
            );
            articleResults.Add(result);
        }

        // ═══ Product Posts Pipeline ═══

        // Paso 3: Research keywords de productos (ML trends dinámico)
        var productKeywords = await context.CallActivityAsync<List<string>>(
            nameof(GetProductKeywordsActivity),
            input.ProductPostCount
        );
        logger.LogInformation("Product keywords found: {Count}", productKeywords.Count);

        // Paso 4: Procesar cada keyword de producto (sub-orquestación)
        foreach (var keyword in productKeywords)
        {
            var result = await context.CallSubOrchestratorAsync<ProductPostResult>(
                nameof(ProcessProductPostOrchestration),
                new ProductPostInput { Keyword = keyword }
            );
            productResults.Add(result);
        }

        // ═══ Guardar resultados ═══

        await context.CallActivityAsync(
            nameof(SaveResultsActivity),
            articleResults
        );
        await context.CallActivityAsync(
            nameof(SaveProductResultsActivity),
            productResults
        );

        var allErrors = articleResults.Where(r => !r.Success).Select(r => r.Error!)
            .Concat(productResults.Where(r => !r.Success).Select(r => r.Error!))
            .ToList();

        return new PipelineResult
        {
            Date = context.CurrentUtcDateTime,
            ArticlesPublished = articleResults.Count(r => r.Success),
            ProductPostsPublished = productResults.Count(r => r.Success),
            TotalPinsCreated = articleResults.Sum(r => r.PinsCreated) + productResults.Sum(r => r.PinsCreated),
            Errors = allErrors
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

    /// <summary>
    /// Sub-orquestación para procesar un product post de ML.
    /// Flujo: search productos → generar post con ProductWriter → publicar WP → crear pin.
    /// </summary>
    [Function(nameof(ProcessProductPostOrchestration))]
    public async Task<ProductPostResult> ProcessProductPostOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ProductPostInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        var apiRetry = new RetryPolicy(
            maxNumberOfAttempts: 5,
            firstRetryInterval: TimeSpan.FromSeconds(10),
            backoffCoefficient: 2.0,
            maxRetryInterval: TimeSpan.FromMinutes(3));

        try
        {
            // Paso 1: Buscar productos en ML (activity)
            var products = await context.CallActivityAsync<List<MercadoLibreProduct>>(
                nameof(SearchMLProductsActivity),
                input!
            );

            if (products.Count == 0)
            {
                logger.LogWarning("No products found for: {Keyword}", input.Keyword);
                return new ProductPostResult
                {
                    Success = false,
                    Keyword = input.Keyword,
                    Error = $"No products found for: {input.Keyword}"
                };
            }
            logger.LogInformation("Found {Count} products for: {Keyword}", products.Count, input.Keyword);

            // Paso 2: Generar product post con ProductWriter DurableAgent
            var productContext = string.Join("\n", products.Select((p, i) =>
            {
                var discount = p.OriginalPrice.HasValue && p.OriginalPrice > p.Price
                    ? $" (antes ${p.OriginalPrice:N0}, {(1 - p.Price / p.OriginalPrice.Value) * 100:N0}% OFF)"
                    : "";
                var shipping = p.FreeShipping ? " | Envío gratis" : "";
                return $"- [{p.Id}] {p.Title} — ${p.Price:N0} ARS{discount}{shipping}";
            }));

            DurableAIAgent agent = context.GetAgent("ProductWriter");
            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse<ProductPost> response = await agent.RunAsync<ProductPost>(
                message: $"""
                    Creá un post corto para redes sociales recomendando estos productos de Mercado Libre.
                    Búsqueda: "{input.Keyword}"

                    Productos encontrados:
                    {productContext}

                    Recordá usar [PRODUCT_LINK:ITEM_ID] para cada producto (reemplazá ITEM_ID con el ID real).
                    """,
                session: session);

            ProductPost post = response.Result
                ?? throw new InvalidOperationException("ProductWriter agent did not return structured output");

            logger.LogInformation("Product post generated: {Title}", post.Title);

            // Paso 3: Reemplazar placeholders con affiliate links (automático)
            var finalContent = post.Content;
            foreach (var product in products)
            {
                var affiliateLink = _mlTools.BuildAffiliateLink(product.Permalink);
                finalContent = finalContent.Replace($"[PRODUCT_LINK:{product.Id}]", affiliateLink);
            }

            // Convertir ProductPost a Article para publicar en WordPress
            var article = new Article
            {
                Title = post.Title,
                Content = finalContent,
                MetaDescription = post.MetaDescription,
                Category = "productos",
                Tags = post.Tags
            };

            // Paso 4: Publicar en WordPress (directo, mecánico)
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article,
                TaskOptions.FromRetryPolicy(apiRetry)
            );

            if (!wpResult.Success)
            {
                logger.LogWarning("WordPress publish failed for product post: {Error}", wpResult.Error);
                return new ProductPostResult
                {
                    Success = false,
                    Keyword = input.Keyword,
                    ProductCount = products.Count,
                    Error = wpResult.Error
                };
            }
            logger.LogInformation("Product post published: {Url}", wpResult.PostUrl);

            // Paso 5: Crear pin en Pinterest con thumbnail del primer producto
            var pinResults = await context.CallActivityAsync<List<PinResult>>(
                nameof(CreateProductPinsActivity),
                new CreateProductPinsInput
                {
                    PostUrl = wpResult.PostUrl!,
                    PostTitle = post.Title,
                    Description = post.MetaDescription,
                    Products = products
                },
                TaskOptions.FromRetryPolicy(apiRetry)
            );

            var successPins = pinResults.Count(p => p.Success);
            logger.LogInformation("Product pins created: {Success}/{Total}", successPins, pinResults.Count);

            return new ProductPostResult
            {
                Success = true,
                Keyword = input.Keyword,
                PostUrl = wpResult.PostUrl,
                ProductCount = products.Count,
                PinsCreated = successPins
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Product post failed: {Keyword}", input!.Keyword);
            return new ProductPostResult
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

        _logger.LogInformation("Article results saved: {Success}/{Total}",
            results.Count(r => r.Success), results.Count);
    }

    // ─── Product Post Activities ────────────────────────────────

    [Function(nameof(GetProductKeywordsActivity))]
    public async Task<List<string>> GetProductKeywordsActivity(
        [ActivityTrigger] int count)
    {
        return await _researchTools.GetProductKeywords(count);
    }

    [Function(nameof(SearchMLProductsActivity))]
    public async Task<List<MercadoLibreProduct>> SearchMLProductsActivity(
        [ActivityTrigger] ProductPostInput input)
    {
        return await _mlTools.SearchProductsAsync(input.Keyword, input.ProductLimit);
    }

    [Function(nameof(CreateProductPinsActivity))]
    public async Task<List<PinResult>> CreateProductPinsActivity(
        [ActivityTrigger] CreateProductPinsInput input)
    {
        // Usar el thumbnail del primer producto como imagen del pin
        var variations = new List<PinVariation>();

        if (input.Products.Count > 0)
        {
            var mainProduct = input.Products[0];
            var imageUrl = !string.IsNullOrEmpty(mainProduct.Thumbnail)
                ? mainProduct.Thumbnail
                : $"https://source.unsplash.com/1000x1500/?{Uri.EscapeDataString(input.PostTitle)}";

            variations.Add(new PinVariation
            {
                Title = input.PostTitle,
                ImageUrl = imageUrl
            });
        }

        return await _pinterestTools.CreatePinsWithRateLimitAsync(
            input.PostUrl,
            input.Description,
            variations
        );
    }

    [Function(nameof(SaveProductResultsActivity))]
    public async Task SaveProductResultsActivity(
        [ActivityTrigger] List<ProductPostResult> results)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await _stateManager.SaveAsync($"daily-product-results/{date}", results);

        foreach (var result in results.Where(r => r.Success))
        {
            await _researchTools.MarkProductKeywordAsPublished(result.Keyword);
        }

        _logger.LogInformation("Product results saved: {Success}/{Total}",
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

public record CreateProductPinsInput
{
    public string PostUrl { get; init; } = "";
    public string PostTitle { get; init; } = "";
    public string Description { get; init; } = "";
    public List<MercadoLibreProduct> Products { get; init; } = [];
}
