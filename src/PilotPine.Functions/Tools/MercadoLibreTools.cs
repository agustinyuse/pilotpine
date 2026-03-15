using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Cliente para la API pública de Mercado Libre Argentina.
///
/// NO se expone al agente (no tiene [Description]).
/// Se llama directamente desde el orchestrator porque buscar
/// productos es una operación mecánica.
///
/// API pública, sin autenticación requerida.
/// Rate limit: 1500 requests/min.
/// </summary>
public class MercadoLibreTools
{
    private readonly HttpClient _http;
    private readonly string _siteId;
    private readonly ILogger<MercadoLibreTools> _logger;

    private const string BaseUrl = "https://api.mercadolibre.com";

    public MercadoLibreTools(HttpClient http, IConfiguration config, ILogger<MercadoLibreTools> logger)
    {
        _http = http;
        _siteId = config["MercadoLibre:SiteId"] ?? "MLA";
        _logger = logger;
    }

    /// <summary>
    /// Busca productos en ML por keyword con la estrategia indicada.
    /// </summary>
    public async Task<List<MercadoLibreProduct>> SearchProductsAsync(
        string query,
        int limit = 10,
        string strategy = "best_sellers")
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var sort = strategy switch
        {
            "best_sellers" => "&sort=sold_quantity_desc",
            "deals" => "&has_deals=yes&sort=sold_quantity_desc",
            "price_asc" => "&sort=price_asc",
            "relevance" => "",
            _ => "&sort=sold_quantity_desc"
        };

        var url = $"{BaseUrl}/sites/{_siteId}/search?q={encodedQuery}&limit={limit}&condition=new{sort}";
        _logger.LogInformation("ML search: {Url}", url);

        try
        {
            var response = await _http.GetFromJsonAsync<MlSearchResponse>(url, MlJsonOptions);

            if (response?.Results == null || response.Results.Count == 0)
            {
                _logger.LogWarning("ML search returned no results for: {Query}", query);
                return [];
            }

            var products = response.Results.Select(r => new MercadoLibreProduct
            {
                Id = r.Id,
                Title = r.Title,
                Price = r.Price,
                OriginalPrice = r.OriginalPrice,
                CurrencyId = r.CurrencyId,
                Permalink = r.Permalink,
                Thumbnail = r.Thumbnail,
                Condition = r.Condition,
                SoldQuantity = r.SoldQuantity,
                FreeShipping = r.Shipping?.FreeShipping ?? false,
                SellerName = r.Seller?.Nickname ?? ""
            }).ToList();

            _logger.LogInformation("ML search found {Count} products for: {Query}", products.Count, query);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML search failed for: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// Obtiene productos trending de ML.
    /// </summary>
    public async Task<List<MercadoLibreProduct>> GetTrendingProductsAsync(string? categoryId = null, int limit = 10)
    {
        var url = categoryId != null
            ? $"{BaseUrl}/highlights/{_siteId}/category/{categoryId}"
            : $"{BaseUrl}/trends/{_siteId}";

        try
        {
            var trendingItems = await _http.GetFromJsonAsync<List<MlTrendItem>>(url, MlJsonOptions);

            if (trendingItems == null || trendingItems.Count == 0)
            {
                _logger.LogWarning("ML trending returned no results");
                return [];
            }

            // Los trending solo traen keywords, hay que buscarlos
            var query = string.Join(" ", trendingItems.Take(3).Select(t => t.Keyword));
            return await SearchProductsAsync(query, limit, "relevance");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ML trending failed");
            // Fallback: buscar productos populares genéricos
            return await SearchProductsAsync("ofertas del dia", limit, "best_sellers");
        }
    }

    // ─── ML API Response DTOs ────────────────────────────────────

    private static readonly JsonSerializerOptions MlJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record MlSearchResponse
    {
        public List<MlSearchResult> Results { get; init; } = [];
    }

    private record MlSearchResult
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public decimal Price { get; init; }
        public decimal? OriginalPrice { get; init; }
        public string CurrencyId { get; init; } = "ARS";
        public string Permalink { get; init; } = "";
        public string Thumbnail { get; init; } = "";
        public string Condition { get; init; } = "new";
        public int SoldQuantity { get; init; }
        public MlShipping? Shipping { get; init; }
        public MlSeller? Seller { get; init; }
    }

    private record MlShipping
    {
        public bool FreeShipping { get; init; }
    }

    private record MlSeller
    {
        public string Nickname { get; init; } = "";
    }

    private record MlTrendItem
    {
        public string Keyword { get; init; } = "";
    }
}
