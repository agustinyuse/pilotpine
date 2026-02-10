using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Publicación directa en WordPress via REST API.
///
/// Este tool NO se expone al agente (no tiene [KernelFunction]).
/// Se llama directamente desde el orchestrator porque publicar
/// es una operación mecánica que no necesita decisión del LLM.
///
/// Ver docs/TOOLS-VS-DIRECT-CALLS.md para la justificación.
/// </summary>
public class WordPressTools
{
    private readonly HttpClient _http;
    private readonly string _wpUrl;
    private readonly string _authHeader;
    private readonly ILogger<WordPressTools> _logger;

    public WordPressTools(HttpClient http, IConfiguration config, ILogger<WordPressTools> logger)
    {
        _http = http;
        _logger = logger;

        _wpUrl = config["WordPress:Url"] ?? "";
        var user = config["WordPress:Username"] ?? "";
        var pass = config["WordPress:AppPassword"] ?? "";

        if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
        {
            _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
        }
        else
        {
            _authHeader = "";
        }
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_wpUrl) && !string.IsNullOrEmpty(_authHeader);

    /// <summary>
    /// Publica un artículo en WordPress.
    /// </summary>
    public async Task<PublishResult> PublishPostAsync(Article article)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("WordPress not configured, skipping publish");
            return new PublishResult
            {
                Success = false,
                Error = "WordPress not configured. Set WordPress:Url, Username, and AppPassword."
            };
        }

        try
        {
            var post = new WpPostRequest
            {
                Title = article.Title,
                Content = article.Content,
                Excerpt = article.MetaDescription,
                Status = "publish",
                Categories = [1],
                Tags = article.Tags
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_wpUrl}/posts")
            {
                Content = JsonContent.Create(post, options: WpJsonOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("WordPress publish failed ({Status}): {Error}",
                    response.StatusCode, errorBody);
                return new PublishResult
                {
                    Success = false,
                    Error = $"HTTP {(int)response.StatusCode}: {errorBody}"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<WpPostResponse>(WpJsonOptions);

            _logger.LogInformation("Published to WordPress: {Url}", result?.Link);
            return new PublishResult
            {
                Success = true,
                PostUrl = result?.Link ?? ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WordPress publish exception");
            return new PublishResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Verifica si ya existe un post con título similar.
    /// </summary>
    public async Task<bool> PostExistsAsync(string title)
    {
        if (!IsConfigured) return false;

        try
        {
            var encodedTitle = Uri.EscapeDataString(title);
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_wpUrl}/posts?search={encodedTitle}&per_page=1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            var posts = await response.Content.ReadFromJsonAsync<List<WpPostResponse>>(WpJsonOptions);
            return posts?.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions WpJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private record WpPostRequest
    {
        public string Title { get; init; } = "";
        public string Content { get; init; } = "";
        public string Excerpt { get; init; } = "";
        public string Status { get; init; } = "publish";
        public int[] Categories { get; init; } = [];
        public string[] Tags { get; init; } = [];
    }

    private record WpPostResponse
    {
        public int Id { get; init; }
        public string Link { get; init; } = "";
    }
}
