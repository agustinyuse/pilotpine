using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Publicación directa en Pinterest via API v5.
///
/// Como WordPressTools, NO se expone al agente.
/// Crear pins es mecánico: siempre se hace después de publicar el artículo.
///
/// Rate limits de Pinterest: ~10 pins/hora por board.
/// El orchestrator maneja los delays entre pins.
/// </summary>
public class PinterestTools
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    private readonly string _defaultBoardId;
    private readonly ILogger<PinterestTools> _logger;

    private const string PinterestApiBase = "https://api.pinterest.com/v5";

    public PinterestTools(HttpClient http, IConfiguration config, ILogger<PinterestTools> logger)
    {
        _http = http;
        _logger = logger;
        _accessToken = config["Pinterest:AccessToken"] ?? "";
        _defaultBoardId = config["Pinterest:DefaultBoardId"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_defaultBoardId);

    /// <summary>
    /// Crea un pin en Pinterest.
    /// </summary>
    public async Task<PinResult> CreatePinAsync(
        string title,
        string description,
        string linkUrl,
        string imageUrl,
        string? boardId = null)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Pinterest not configured, skipping pin creation");
            return new PinResult
            {
                Success = false,
                Error = "Pinterest not configured. Set Pinterest:AccessToken and DefaultBoardId."
            };
        }

        try
        {
            var pin = new PinRequest
            {
                BoardId = boardId ?? _defaultBoardId,
                Title = Truncate(title, 100),
                Description = Truncate(description, 500),
                Link = linkUrl,
                MediaSource = new PinMediaSource
                {
                    SourceType = "image_url",
                    Url = imageUrl
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{PinterestApiBase}/pins")
            {
                Content = JsonContent.Create(pin, options: PinJsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Pinterest create pin failed ({Status}): {Error}",
                    response.StatusCode, errorBody);
                return new PinResult
                {
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}: {errorBody}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<PinResponse>(PinJsonOptions);

            _logger.LogInformation("Pinterest pin created: {PinId}", result?.Id);
            return new PinResult
            {
                Success = true,
                PinId = result?.Id ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pinterest create pin exception");
            return new PinResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Crea múltiples pins con delay entre cada uno para respetar rate limits.
    /// </summary>
    public async Task<List<PinResult>> CreatePinsWithRateLimitAsync(
        string postUrl,
        string description,
        List<PinVariation> variations,
        TimeSpan delayBetweenPins = default)
    {
        if (delayBetweenPins == default)
            delayBetweenPins = TimeSpan.FromSeconds(30);

        var results = new List<PinResult>();

        for (var i = 0; i < variations.Count; i++)
        {
            if (i > 0)
                await Task.Delay(delayBetweenPins);

            var v = variations[i];
            var result = await CreatePinAsync(
                v.Title,
                description,
                postUrl,
                v.ImageUrl
            );
            results.Add(result);

            if (!result.Success)
                _logger.LogWarning("Pin {Index}/{Total} failed: {Error}", i + 1, variations.Count, result.Error);
        }

        _logger.LogInformation("Pins created: {Success}/{Total}",
            results.Count(r => r.Success), results.Count);

        return results;
    }

    /// <summary>
    /// Obtiene los boards del usuario autenticado.
    /// </summary>
    public async Task<List<PinterestBoard>> GetBoardsAsync()
    {
        if (!IsConfigured) return [];

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{PinterestApiBase}/boards");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return [];

            var result = await response.Content.ReadFromJsonAsync<PinterestBoardsResponse>(PinJsonOptions);
            return result?.Items ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Pinterest boards");
            return [];
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;

    private static readonly JsonSerializerOptions PinJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ─── Request/Response DTOs ──────────────────────────────────

    private record PinRequest
    {
        public string BoardId { get; init; } = "";
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string Link { get; init; } = "";
        public PinMediaSource MediaSource { get; init; } = new();
    }

    private record PinMediaSource
    {
        public string SourceType { get; init; } = "image_url";
        public string Url { get; init; } = "";
    }

    private record PinResponse
    {
        public string Id { get; init; } = "";
    }

    private record PinterestBoardsResponse
    {
        public List<PinterestBoard> Items { get; init; } = [];
    }
}

/// <summary>
/// Variación de un pin (diferente imagen/título para A/B testing).
/// </summary>
public record PinVariation
{
    public string Title { get; init; } = "";
    public string ImageUrl { get; init; } = "";
}

/// <summary>
/// Board de Pinterest.
/// </summary>
public record PinterestBoard
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}
