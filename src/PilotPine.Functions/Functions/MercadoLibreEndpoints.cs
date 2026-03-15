using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.DurableTask;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Models;
using PilotPine.Functions.Tools;

namespace PilotPine.Functions.Functions;

/// <summary>
/// HTTP endpoints para el flujo de Mercado Libre.
///
/// Endpoints:
///   GET  /api/SearchMLProducts?q={query}&limit=10&strategy=best_sellers
///   POST /api/GenerateProductPost?q={query}&strategy=best_sellers
///   POST /api/ReplaceAffiliateLinks (body: { permalinks: [...], affiliateLinks: [...], postContent: "..." })
/// </summary>
public class MercadoLibreEndpoints
{
    private readonly MercadoLibreTools _mlTools;
    private readonly FoundryModelProvider _foundryProvider;
    private readonly MercadoLibreContentTools _mlContentTools;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MercadoLibreEndpoints> _logger;

    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
    };

    public MercadoLibreEndpoints(
        MercadoLibreTools mlTools,
        FoundryModelProvider foundryProvider,
        MercadoLibreContentTools mlContentTools,
        IMemoryCache cache,
        ILogger<MercadoLibreEndpoints> logger)
    {
        _mlTools = mlTools;
        _foundryProvider = foundryProvider;
        _mlContentTools = mlContentTools;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Busca productos en ML y devuelve JSON.
    /// GET /api/SearchMLProducts?q=auriculares&limit=5&strategy=best_sellers
    /// </summary>
    [Function("SearchMLProducts")]
    public async Task<HttpResponseData> SearchMLProducts(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
    {
        var query = req.Query("q");
        if (string.IsNullOrEmpty(query))
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Query parameter 'q' is required" });
            return badRequest;
        }

        var limit = int.TryParse(req.Query("limit"), out var l) ? l : 10;
        var strategy = req.Query("strategy") ?? "best_sellers";

        var products = await _mlTools.SearchProductsAsync(query, limit, strategy);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            query,
            strategy,
            count = products.Count,
            products,
            permalinks = products.Select(p => p.Permalink).ToList()
        });
        return response;
    }

    /// <summary>
    /// Busca productos + genera post con Claude.
    /// POST /api/GenerateProductPost?q=auriculares&strategy=best_sellers&limit=5
    /// </summary>
    [Function("GenerateProductPost")]
    public async Task<HttpResponseData> GenerateProductPost(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var query = req.Query("q");
        if (string.IsNullOrEmpty(query))
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Query parameter 'q' is required" });
            return badRequest;
        }

        var limit = int.TryParse(req.Query("limit"), out var l) ? l : 5;
        var strategy = req.Query("strategy") ?? "best_sellers";

        _logger.LogInformation("GenerateProductPost: q={Query}, strategy={Strategy}", query, strategy);

        // Paso 1: Buscar productos en ML
        var products = await _mlTools.SearchProductsAsync(query, limit, strategy);

        if (products.Count == 0)
        {
            var notFound = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = $"No products found for: {query}" });
            return notFound;
        }

        // Paso 2: Armar contexto para el agente
        var productContext = string.Join("\n", products.Select((p, i) =>
        {
            var discount = p.OriginalPrice.HasValue && p.OriginalPrice > p.Price
                ? $" (antes ${p.OriginalPrice:N0}, {(1 - p.Price / p.OriginalPrice.Value) * 100:N0}% OFF)"
                : "";
            var shipping = p.FreeShipping ? " | Envío gratis" : "";
            return $"- [{p.Id}] {p.Title} — ${p.Price:N0} ARS{discount}{shipping} | Vendidos: {p.SoldQuantity} | Vendedor: {p.SellerName}";
        }));

        var prompt = $"""
            Creá un post corto para redes sociales recomendando estos productos de Mercado Libre.
            Búsqueda: "{query}"

            Productos encontrados:
            {productContext}

            Recordá usar [PRODUCT_LINK:ITEM_ID] para cada producto que menciones (reemplazá ITEM_ID con el ID real, ej: [PRODUCT_LINK:{products[0].Id}]).
            """;

        // Paso 3: Generar contenido con el agente ProductWriter
        try
        {
            var agent = DurableAgentContext.Current.GetAgent("ProductWriter");
            var session = await agent.CreateSessionAsync();
            var agentResponse = await agent.RunAsync<ProductPost>(message: prompt, session: session);

            ProductPost? post = agentResponse.Result;

            if (post == null)
            {
                // Fallback: crear post básico con el response del agente
                post = await _mlContentTools.CreateProductPost(
                    query,
                    $"Mejores {query} en Mercado Libre",
                    agentResponse.ToString() ?? "",
                    string.Join(",", products.Select(p => p.Id)),
                    $"Encontrá las mejores ofertas de {query} en Mercado Libre Argentina"
                );
            }

            // Paso 4: Reemplazar placeholders con permalinks reales
            var finalContent = post.Content;
            foreach (var product in products)
            {
                finalContent = finalContent.Replace($"[PRODUCT_LINK:{product.Id}]", product.Permalink);
            }

            var finalPost = post with
            {
                Content = finalContent,
                Products = products,
                PermalinksToConvert = products
                    .Where(p => post.Content.Contains(p.Id))
                    .Select(p => p.Permalink)
                    .ToList()
            };

            // Cachear el post para que FinalizeProductPost lo pueda recuperar
            var cacheKey = $"product_post:{query.ToLowerInvariant().Trim()}";
            _cache.Set(cacheKey, finalPost, CacheOptions);
            _logger.LogInformation("Cached post for query '{Query}' (key: {CacheKey})", query, cacheKey);

            // String listo para pegar directo en el Generador de links de ML
            var permalinksForGenerator = string.Join("\n", finalPost.PermalinksToConvert);

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                post = finalPost,
                permalinks_to_convert = finalPost.PermalinksToConvert,
                permalinks_for_generator = permalinksForGenerator,
                instructions = "Copiá el campo 'permalinks_for_generator' y pegalo directo en el Generador de links de ML (https://www.mercadolibre.com.ar/afiliados/generador-de-links). Luego usá POST /api/FinalizeProductPost con los meli.la para obtener el post final."
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate product post for: {Query}", query);
            var error = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            await error.WriteAsJsonAsync(new { error = ex.Message });
            return error;
        }
    }

    /// <summary>
    /// Reemplaza permalinks con affiliate links en el contenido.
    /// POST /api/ReplaceAffiliateLinks
    /// Body: { "postContent": "...", "permalinks": ["https://..."], "affiliateLinks": ["https://meli.la/..."] }
    /// </summary>
    [Function("ReplaceAffiliateLinks")]
    public async Task<HttpResponseData> ReplaceAffiliateLinks(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<ReplaceLinksRequest>();

        if (body == null || string.IsNullOrEmpty(body.PostContent))
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Body must include postContent, permalinks, and affiliateLinks arrays" });
            return badRequest;
        }

        if (body.Permalinks.Count != body.AffiliateLinks.Count)
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "permalinks and affiliateLinks must have the same length" });
            return badRequest;
        }

        var finalContent = body.PostContent;
        var replacements = 0;

        for (var i = 0; i < body.Permalinks.Count; i++)
        {
            if (finalContent.Contains(body.Permalinks[i]))
            {
                finalContent = finalContent.Replace(body.Permalinks[i], body.AffiliateLinks[i]);
                replacements++;
            }
        }

        _logger.LogInformation("Replaced {Count} affiliate links", replacements);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            finalContent,
            replacements,
            ready = true
        });
        return response;
    }

    /// <summary>
    /// Recupera el post cacheado y reemplaza permalinks con affiliate links.
    /// POST /api/FinalizeProductPost
    /// Body: { "query": "auriculares", "affiliateLinks": ["https://meli.la/abc", ...] }
    ///
    /// Requiere haber llamado GenerateProductPost previamente con la misma query (cache de 30 min).
    /// </summary>
    [Function("FinalizeProductPost")]
    public async Task<HttpResponseData> FinalizeProductPost(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var body = await req.ReadFromJsonAsync<FinalizePostRequest>();

        if (body == null || string.IsNullOrEmpty(body.Query))
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { error = "Body must include 'query' and 'affiliateLinks'" });
            return badRequest;
        }

        var cacheKey = $"product_post:{body.Query.ToLowerInvariant().Trim()}";

        if (!_cache.TryGetValue<ProductPost>(cacheKey, out var cachedPost) || cachedPost == null)
        {
            var notFound = req.CreateResponse(System.Net.HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new
            {
                error = $"No hay post cacheado para la query '{body.Query}'. Llamá primero a POST /api/GenerateProductPost?q={body.Query}",
                hint = "El cache dura 30 minutos desde la generación."
            });
            return notFound;
        }

        if (body.AffiliateLinks.Count != cachedPost.PermalinksToConvert.Count)
        {
            var badRequest = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new
            {
                error = $"Se esperaban {cachedPost.PermalinksToConvert.Count} affiliate links pero se recibieron {body.AffiliateLinks.Count}.",
                expected_count = cachedPost.PermalinksToConvert.Count,
                permalinks = cachedPost.PermalinksToConvert
            });
            return badRequest;
        }

        var finalContent = cachedPost.Content;
        var replacements = 0;

        for (var i = 0; i < cachedPost.PermalinksToConvert.Count; i++)
        {
            if (finalContent.Contains(cachedPost.PermalinksToConvert[i]))
            {
                finalContent = finalContent.Replace(cachedPost.PermalinksToConvert[i], body.AffiliateLinks[i]);
                replacements++;
            }
        }

        _logger.LogInformation("FinalizeProductPost: replaced {Count} links for query '{Query}'", replacements, body.Query);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            finalContent,
            title = cachedPost.Title,
            replacements,
            ready = true
        });
        return response;
    }

    private record ReplaceLinksRequest
    {
        public string PostContent { get; init; } = "";
        public List<string> Permalinks { get; init; } = [];
        public List<string> AffiliateLinks { get; init; } = [];
    }

    private record FinalizePostRequest
    {
        public string Query { get; init; } = "";
        public List<string> AffiliateLinks { get; init; } = [];
    }
}

// Extension helper para query params
internal static class HttpRequestDataExtensions
{
    public static string? Query(this HttpRequestData req, string key)
    {
        var queryString = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        return queryString[key];
    }
}
