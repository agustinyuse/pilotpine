using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Generación de imágenes para Pinterest pins.
///
/// Soporta dos modos:
///   1. Bannerbear (pago): Genera imágenes con texto overlay profesional
///   2. Unsplash (gratis): Fotos stock como fallback
///
/// Las imágenes de Pinterest deben ser 1000x1500 (portrait 2:3).
/// </summary>
public class ImageTools
{
    private readonly HttpClient _http;
    private readonly string _bannerbearApiKey;
    private readonly Dictionary<string, string> _templateIds;
    private readonly ILogger<ImageTools> _logger;

    public ImageTools(HttpClient http, IConfiguration config, ILogger<ImageTools> logger)
    {
        _http = http;
        _logger = logger;
        _bannerbearApiKey = config["Bannerbear:ApiKey"] ?? "";

        // Templates por estilo de artículo
        _templateIds = new Dictionary<string, string>
        {
            ["travel"] = config["Bannerbear:Templates:travel"] ?? "",
            ["listicle"] = config["Bannerbear:Templates:listicle"] ?? "",
            ["guide"] = config["Bannerbear:Templates:guide"] ?? ""
        };
    }

    public bool IsBannerbearConfigured => !string.IsNullOrEmpty(_bannerbearApiKey);

    /// <summary>
    /// Genera una imagen de pin. Usa Bannerbear si está configurado, Unsplash como fallback.
    /// </summary>
    public async Task<string> GeneratePinImageAsync(
        string headline,
        string keyword,
        string templateStyle = "travel")
    {
        if (IsBannerbearConfigured)
        {
            var url = await GenerateWithBannerbearAsync(headline, keyword, templateStyle);
            if (url != null) return url;
            _logger.LogWarning("Bannerbear failed, falling back to Unsplash");
        }

        return GetUnsplashUrl(keyword);
    }

    /// <summary>
    /// Genera múltiples variaciones de imagen para A/B testing.
    /// </summary>
    public async Task<List<PinVariation>> GeneratePinVariationsAsync(
        string articleTitle,
        string keyword,
        List<string> headlines)
    {
        var variations = new List<PinVariation>();

        foreach (var headline in headlines)
        {
            var imageUrl = await GeneratePinImageAsync(headline, keyword);
            variations.Add(new PinVariation
            {
                Title = headline,
                ImageUrl = imageUrl
            });
        }

        return variations;
    }

    private async Task<string?> GenerateWithBannerbearAsync(
        string headline,
        string keyword,
        string templateStyle)
    {
        try
        {
            var templateId = _templateIds.GetValueOrDefault(templateStyle, "");
            if (string.IsNullOrEmpty(templateId))
            {
                _logger.LogWarning("No Bannerbear template for style: {Style}", templateStyle);
                return null;
            }

            var request = new BannerbearRequest
            {
                Template = templateId,
                Modifications =
                [
                    new() { Name = "headline", Text = headline },
                    new() { Name = "background", ImageUrl = GetUnsplashUrl(keyword) }
                ]
            };

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.bannerbear.com/v2/images")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bannerbearApiKey);

            var response = await _http.SendAsync(httpRequest);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Bannerbear failed: {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<BannerbearResponse>();
            return result?.ImageUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bannerbear exception");
            return null;
        }
    }

    /// <summary>
    /// URL de Unsplash para imágenes stock gratuitas.
    /// Pinterest pins: 1000x1500 portrait.
    /// </summary>
    private static string GetUnsplashUrl(string keyword)
    {
        var encoded = Uri.EscapeDataString(keyword);
        return $"https://source.unsplash.com/1000x1500/?{encoded}";
    }

    // ─── Bannerbear DTOs ────────────────────────────────────────

    private record BannerbearRequest
    {
        public string Template { get; init; } = "";
        public List<BannerbearModification> Modifications { get; init; } = [];
    }

    private record BannerbearModification
    {
        public string Name { get; init; } = "";
        public string? Text { get; init; }
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; init; }
    }

    private record BannerbearResponse
    {
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; init; }
    }
}
