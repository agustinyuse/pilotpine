using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Cliente para la API de Mercado Libre Argentina.
///
/// La API de búsqueda (/search) está bloqueada por PolicyAgent,
/// así que usamos una estrategia alternativa:
///   1. domain_discovery/search → keyword a categoría
///   2. highlights → best sellers por categoría (product IDs)
///   3. products/{id} → detalles del producto (nombre, fotos)
///   4. products/{id}/items → listings reales (precio, envío, vendedor)
/// </summary>
public class MercadoLibreTools
{
    private readonly HttpClient _http;
    private readonly string _siteId;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly ILogger<MercadoLibreTools> _logger;

    private string _accessToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private const string BaseUrl = "https://api.mercadolibre.com";

    public MercadoLibreTools(HttpClient http, IConfiguration config, ILogger<MercadoLibreTools> logger)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PilotPine/1.0");
        _siteId = config["MercadoLibre:SiteId"] ?? "MLA";
        _clientId = config["MercadoLibre:ClientId"] ?? "";
        _clientSecret = config["MercadoLibre:ClientSecret"] ?? "";
        _accessToken = config["MercadoLibre:AccessToken"] ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Busca productos por keyword usando highlights + products API.
    /// </summary>
    public async Task<List<MercadoLibreProduct>> SearchProductsAsync(
        string query,
        int limit = 10,
        string strategy = "best_sellers")
    {
        await EnsureTokenAsync();

        try
        {
            // Paso 1: Mapear keyword a categoría
            var categoryId = await FindCategoryAsync(query);
            if (categoryId == null)
            {
                _logger.LogWarning("No category found for: {Query}", query);
                return [];
            }
            _logger.LogInformation("Category for '{Query}': {Category}", query, categoryId);

            // Paso 2: Obtener best sellers de la categoría
            var productIds = await GetHighlightsAsync(categoryId, limit);
            if (productIds.Count == 0)
            {
                _logger.LogWarning("No highlights for category: {Category}", categoryId);
                return [];
            }

            // Paso 3: Obtener detalles de cada producto
            var products = new List<MercadoLibreProduct>();
            foreach (var productId in productIds)
            {
                var product = await GetProductWithBestItemAsync(productId);
                if (product != null)
                    products.Add(product);
            }

            _logger.LogInformation("Found {Count} products for: {Query}", products.Count, query);
            return products;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for: {Query}", query);
            return [];
        }
    }

    /// <summary>
    /// Obtiene productos trending de ML.
    /// </summary>
    public async Task<List<MercadoLibreProduct>> GetTrendingProductsAsync(string? categoryId = null, int limit = 10)
    {
        await EnsureTokenAsync();

        try
        {
            if (categoryId != null)
            {
                var productIds = await GetHighlightsAsync(categoryId, limit);
                var products = new List<MercadoLibreProduct>();
                foreach (var id in productIds)
                {
                    var p = await GetProductWithBestItemAsync(id);
                    if (p != null) products.Add(p);
                }
                return products;
            }

            // Sin categoría: usar trends para obtener keywords
            var trends = await GetAsync<List<MlTrendItem>>($"{BaseUrl}/trends/{_siteId}");
            if (trends == null || trends.Count == 0)
                return [];

            // Buscar por el primer trend
            return await SearchProductsAsync(trends[0].Keyword, limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Trending failed");
            return [];
        }
    }

    /// <summary>
    /// Obtiene keywords trending de ML (/trends/MLA).
    /// Usado por ResearchTools para research dinámico.
    /// </summary>
    public async Task<List<string>> GetTrendingKeywordsAsync(int limit = 10)
    {
        await EnsureTokenAsync();

        try
        {
            var trends = await GetAsync<List<MlTrendItem>>($"{BaseUrl}/trends/{_siteId}");
            if (trends == null || trends.Count == 0)
            {
                _logger.LogWarning("No trending keywords found");
                return [];
            }

            var keywords = trends.Take(limit).Select(t => t.Keyword).ToList();
            _logger.LogInformation("Trending keywords: {Count} found", keywords.Count);
            return keywords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTrendingKeywords failed");
            return [];
        }
    }

    /// <summary>
    /// Construye un affiliate link agregando tracking params al permalink.
    /// Usa el app_id (client_id) como parámetro de tracking.
    /// </summary>
    public string BuildAffiliateLink(string permalink)
    {
        if (string.IsNullOrEmpty(permalink)) return permalink;

        var separator = permalink.Contains('?') ? "&" : "?";
        return $"{permalink}{separator}matt_tool=site&matt_word=pilotpine";
    }

    // ─── Internal API calls ───────────────────────────────────────

    private async Task<string?> FindCategoryAsync(string query)
    {
        var results = await GetAsync<List<MlDomainResult>>(
            $"{BaseUrl}/sites/{_siteId}/domain_discovery/search?q={Uri.EscapeDataString(query)}");
        return results?.FirstOrDefault()?.CategoryId;
    }

    private async Task<List<string>> GetHighlightsAsync(string categoryId, int limit)
    {
        var highlights = await GetAsync<MlHighlightsResponse>(
            $"{BaseUrl}/highlights/{_siteId}/category/{categoryId}");

        return highlights?.Content?
            .Where(c => c.Type == "PRODUCT")
            .Take(limit)
            .Select(c => c.Id)
            .ToList() ?? [];
    }

    private async Task<MercadoLibreProduct?> GetProductWithBestItemAsync(string productId)
    {
        // Obtener info del producto (nombre, fotos)
        var product = await GetAsync<MlProduct>($"{BaseUrl}/products/{productId}");
        if (product == null) return null;

        // Obtener el mejor listing (precio más bajo con envío gratis)
        var items = await GetAsync<MlProductItemsResponse>($"{BaseUrl}/products/{productId}/items?status=active&limit=3");
        var bestItem = items?.Results?
            .OrderByDescending(i => i.Shipping?.FreeShipping == true)
            .ThenBy(i => i.Price)
            .FirstOrDefault();

        if (bestItem == null) return null;

        var permalink = $"https://www.mercadolibre.com.ar/p/{productId}";

        return new MercadoLibreProduct
        {
            Id = bestItem.ItemId,
            Title = product.Name,
            Price = bestItem.Price,
            OriginalPrice = bestItem.OriginalPrice,
            CurrencyId = bestItem.CurrencyId,
            Permalink = permalink,
            Thumbnail = product.Pictures?.FirstOrDefault()?.Url ?? "",
            Condition = bestItem.Condition,
            SoldQuantity = 0,
            FreeShipping = bestItem.Shipping?.FreeShipping ?? false,
            SellerName = bestItem.OfficialStoreId?.ToString() ?? ""
        };
    }

    // ─── Auth helpers ─────────────────────────────────────────────

    private async Task EnsureTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken)) return;

        await _tokenLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(_accessToken)) return;
            await RefreshTokenWithClientCredentialsAsync();
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task RefreshTokenWithClientCredentialsAsync()
    {
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
        {
            _logger.LogError("ML ClientId/ClientSecret not configured");
            return;
        }

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
        });

        var response = await _http.PostAsync($"{BaseUrl}/oauth/token", content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("ML client_credentials failed: {Body}", body);
            return;
        }

        var token = await response.Content.ReadFromJsonAsync<MlTokenResponse>(MlJsonOptions);
        if (token != null)
        {
            _accessToken = token.AccessToken;
            _logger.LogInformation("ML token auto-obtained via client_credentials, expires in {Seconds}s", token.ExpiresIn);
        }
    }

    private async Task<T?> GetAsync<T>(string url) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _http.SendAsync(request);

        // Token expirado → refrescar y reintentar
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("ML token expired, refreshing...");
            await RefreshTokenWithClientCredentialsAsync();

            using var retry = new HttpRequestMessage(HttpMethod.Get, url);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            response = await _http.SendAsync(retry);
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("ML GET {Url} failed: {Status} {Body}", url, response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<T>(MlJsonOptions);
    }

    // ─── DTOs ─────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions MlJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record MlDomainResult
    {
        public string DomainId { get; init; } = "";
        public string CategoryId { get; init; } = "";
        public string CategoryName { get; init; } = "";
    }

    private record MlHighlightsResponse
    {
        public List<MlHighlightItem> Content { get; init; } = [];
    }

    private record MlHighlightItem
    {
        public string Id { get; init; } = "";
        public int Position { get; init; }
        public string Type { get; init; } = "";
    }

    private record MlProduct
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public List<MlPicture>? Pictures { get; init; }
    }

    private record MlPicture
    {
        public string Url { get; init; } = "";
    }

    private record MlProductItemsResponse
    {
        public List<MlProductItem> Results { get; init; } = [];
    }

    private record MlProductItem
    {
        public string ItemId { get; init; } = "";
        public decimal Price { get; init; }
        public decimal? OriginalPrice { get; init; }
        public string CurrencyId { get; init; } = "ARS";
        public string Condition { get; init; } = "new";
        public int? OfficialStoreId { get; init; }
        public MlShipping? Shipping { get; init; }
    }

    private record MlShipping
    {
        public bool FreeShipping { get; init; }
    }

    private record MlTrendItem
    {
        public string Keyword { get; init; } = "";
    }
}

public record MlTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = "";
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "";
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "";
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; init; } = "";
}
