# 02-TOOLS: Implementar Tools del Agente

## Cómo Funcionan los Tools

Un Tool es una función C# que el agente puede llamar. El agente (Claude) decide cuándo usarla basándose en:
1. El nombre de la función
2. La descripción `[Description]`
3. Los parámetros y sus descripciones

```csharp
// Claude ve esto y entiende: "puedo llamar esto para publicar en WordPress"
[KernelFunction]
[Description("Publica un artículo en WordPress")]
public async Task<string> PublishPost(
    [Description("Título del post")] string title,
    [Description("Contenido HTML")] string content)
{
    // Tu código
}
```

## Models.cs

Primero, crear los DTOs en `/Models/Models.cs`:

```csharp
namespace PinterestAffiliate.Models;

public record KeywordResult
{
    public string Keyword { get; init; }
    public string ArticleType { get; init; }  // listicle, guide, etc.
}

public record Article
{
    public string Title { get; init; }
    public string Content { get; init; }
    public string MetaDescription { get; init; }
    public string Category { get; init; }
    public string[] Tags { get; init; }
}

public record PublishResult
{
    public bool Success { get; init; }
    public string PostUrl { get; init; }
    public string Error { get; init; }
}

public record PinResult
{
    public bool Success { get; init; }
    public string PinId { get; init; }
    public string Error { get; init; }
}
```

---

## Tool 1: ResearchTools.cs

Busca keywords trending. Por ahora, versión simple que podés mejorar después.

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;
using PinterestAffiliate.Models;

namespace PinterestAffiliate.Tools;

public class ResearchTools
{
    private readonly ILogger<ResearchTools> _logger;
    
    // Keywords ya publicadas (en producción, leer de storage)
    private static readonly HashSet<string> _published = new();

    public ResearchTools(ILogger<ResearchTools> logger)
    {
        _logger = logger;
    }

    [KernelFunction]
    [Description("Obtiene keywords de viajes para crear contenido. Retorna keywords que aún no se han publicado.")]
    public Task<List<KeywordResult>> GetKeywords(
        [Description("Cantidad de keywords a retornar")] int count = 3)
    {
        // Lista base de keywords (en producción: Pinterest Trends API, Google Trends, etc.)
        var allKeywords = new List<KeywordResult>
        {
            new() { Keyword = "hidden beaches portugal", ArticleType = "listicle" },
            new() { Keyword = "budget travel europe 2025", ArticleType = "guide" },
            new() { Keyword = "best hiking trails switzerland", ArticleType = "listicle" },
            new() { Keyword = "romantic hotels paris", ArticleType = "listicle" },
            new() { Keyword = "greek islands guide", ArticleType = "guide" },
            new() { Keyword = "amsterdam travel tips", ArticleType = "guide" },
            new() { Keyword = "best hostels barcelona", ArticleType = "listicle" },
            new() { Keyword = "iceland road trip", ArticleType = "guide" },
            new() { Keyword = "italian coastal towns", ArticleType = "listicle" },
            new() { Keyword = "scotland castles visit", ArticleType = "listicle" }
        };

        // Filtrar las ya publicadas
        var available = allKeywords
            .Where(k => !_published.Contains(k.Keyword.ToLower()))
            .Take(count)
            .ToList();

        _logger.LogInformation("Found {Count} keywords", available.Count);
        return Task.FromResult(available);
    }

    [KernelFunction]
    [Description("Marca una keyword como publicada para no repetirla")]
    public Task MarkAsPublished([Description("Keyword publicada")] string keyword)
    {
        _published.Add(keyword.ToLower());
        _logger.LogInformation("Marked as published: {Keyword}", keyword);
        return Task.CompletedTask;
    }
}
```

---

## Tool 2: ContentTools.cs

Este es el tool más importante. Claude lo usa para estructurar el contenido.

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;
using PinterestAffiliate.Models;

namespace PinterestAffiliate.Tools;

public class ContentTools
{
    private readonly IConfiguration _config;

    public ContentTools(IConfiguration config)
    {
        _config = config;
    }

    [KernelFunction]
    [Description("Estructura un artículo con placeholders de affiliate. El contenido debe ser generado por el LLM.")]
    public Task<Article> CreateArticleStructure(
        [Description("Keyword principal")] string keyword,
        [Description("Tipo: listicle o guide")] string articleType,
        [Description("Título generado")] string title,
        [Description("Contenido HTML completo del artículo")] string content,
        [Description("Meta description (max 155 chars)")] string metaDescription)
    {
        // Insertar affiliate links
        var processedContent = content
            .Replace("[HOTEL_LINK]", GetAffiliateLink("booking", keyword))
            .Replace("[TOUR_LINK]", GetAffiliateLink("getyourguide", keyword));

        var article = new Article
        {
            Title = title,
            Content = processedContent,
            MetaDescription = metaDescription.Length > 155 
                ? metaDescription[..155] 
                : metaDescription,
            Category = "travel",
            Tags = keyword.Split(' ').Concat(new[] { "travel", "europe" }).ToArray()
        };

        return Task.FromResult(article);
    }

    [KernelFunction]
    [Description("Genera variaciones de copy para Pinterest pins")]
    public Task<List<string>> GeneratePinHeadlines(
        [Description("Título del artículo")] string articleTitle,
        [Description("Cantidad de variaciones")] int count = 3)
    {
        // El LLM debería generar esto, pero damos estructura
        var headlines = new List<string>
        {
            articleTitle,
            $"🌍 {articleTitle}",
            $"Must See: {articleTitle}"
        };

        return Task.FromResult(headlines.Take(count).ToList());
    }

    private string GetAffiliateLink(string program, string keyword)
    {
        var searchTerm = Uri.EscapeDataString(keyword);
        return program switch
        {
            "booking" => $"https://www.booking.com/searchresults.html?ss={searchTerm}&aid={_config["Affiliates:BookingId"]}",
            "getyourguide" => $"https://www.getyourguide.com/s/?q={searchTerm}&partner_id={_config["Affiliates:GetYourGuideId"]}",
            _ => "#"
        };
    }
}
```

---

## Tool 3: ImageTools.cs

Genera imágenes para Pinterest. Usa Bannerbear o similar.

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PinterestAffiliate.Tools;

public class ImageTools
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public ImageTools(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["Bannerbear:ApiKey"];
    }

    [KernelFunction]
    [Description("Genera una imagen de Pinterest pin con texto overlay")]
    public async Task<string> GeneratePinImage(
        [Description("Texto principal del pin")] string headline,
        [Description("Keyword para buscar imagen de fondo")] string keyword)
    {
        // Si no hay Bannerbear configurado, usar placeholder
        if (string.IsNullOrEmpty(_apiKey))
        {
            // Usar Unsplash como fallback gratuito
            return $"https://source.unsplash.com/1000x1500/?{Uri.EscapeDataString(keyword)}";
        }

        // Llamar a Bannerbear API
        var request = new
        {
            template = "YOUR_TEMPLATE_ID",
            modifications = new[]
            {
                new { name = "headline", text = headline },
                new { name = "background", image_url = $"https://source.unsplash.com/1000x1500/?{Uri.EscapeDataString(keyword)}" }
            }
        };

        _http.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

        var response = await _http.PostAsJsonAsync("https://api.bannerbear.com/v2/images", request);
        var result = await response.Content.ReadFromJsonAsync<BannerbearResponse>();

        return result?.ImageUrl ?? $"https://source.unsplash.com/1000x1500/?{Uri.EscapeDataString(keyword)}";
    }

    private record BannerbearResponse(string ImageUrl);
}
```

---

## Tool 4: WordPressTools.cs

Publica en WordPress via REST API.

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text;
using PinterestAffiliate.Models;

namespace PinterestAffiliate.Tools;

public class WordPressTools
{
    private readonly HttpClient _http;
    private readonly string _wpUrl;
    private readonly string _authHeader;

    public WordPressTools(HttpClient http, IConfiguration config)
    {
        _http = http;
        _wpUrl = config["WordPress:Url"];
        
        var credentials = $"{config["WordPress:Username"]}:{config["WordPress:AppPassword"]}";
        _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
    }

    [KernelFunction]
    [Description("Publica un artículo en WordPress y retorna la URL")]
    public async Task<PublishResult> PublishPost(
        [Description("Título del post")] string title,
        [Description("Contenido HTML")] string content,
        [Description("Meta description")] string excerpt,
        [Description("Tags separados por coma")] string tags)
    {
        try
        {
            var post = new
            {
                title,
                content,
                excerpt,
                status = "publish",
                categories = new[] { 1 }, // Categoría default
                tags = tags.Split(',').Select(t => t.Trim()).ToArray()
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_wpUrl}/posts")
            {
                Content = JsonContent.Create(post)
            };
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", _authHeader);

            var response = await _http.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new PublishResult { Success = false, Error = error };
            }

            var result = await response.Content.ReadFromJsonAsync<WpPostResponse>();
            
            return new PublishResult 
            { 
                Success = true, 
                PostUrl = result?.Link ?? ""
            };
        }
        catch (Exception ex)
        {
            return new PublishResult { Success = false, Error = ex.Message };
        }
    }

    private record WpPostResponse(string Link);
}
```

---

## Tool 5: PinterestTools.cs

Crea pins en Pinterest.

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;
using PinterestAffiliate.Models;

namespace PinterestAffiliate.Tools;

public class PinterestTools
{
    private readonly HttpClient _http;
    private readonly string _accessToken;
    private readonly string _defaultBoardId;

    public PinterestTools(HttpClient http, IConfiguration config)
    {
        _http = http;
        _accessToken = config["Pinterest:AccessToken"];
        _defaultBoardId = config["Pinterest:DefaultBoardId"];
    }

    [KernelFunction]
    [Description("Crea un pin en Pinterest y retorna el ID")]
    public async Task<PinResult> CreatePin(
        [Description("Título del pin (max 100 chars)")] string title,
        [Description("Descripción del pin")] string description,
        [Description("URL del artículo de destino")] string linkUrl,
        [Description("URL de la imagen del pin")] string imageUrl)
    {
        try
        {
            var pin = new
            {
                board_id = _defaultBoardId,
                title = title.Length > 100 ? title[..100] : title,
                description = description.Length > 500 ? description[..500] : description,
                link = linkUrl,
                media_source = new
                {
                    source_type = "image_url",
                    url = imageUrl
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pinterest.com/v5/pins")
            {
                Content = JsonContent.Create(pin)
            };
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            var response = await _http.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new PinResult { Success = false, Error = error };
            }

            var result = await response.Content.ReadFromJsonAsync<PinterestPinResponse>();
            
            return new PinResult 
            { 
                Success = true, 
                PinId = result?.Id ?? ""
            };
        }
        catch (Exception ex)
        {
            return new PinResult { Success = false, Error = ex.Message };
        }
    }

    private record PinterestPinResponse(string Id);
}
```

---

## Registrar Tools en el Kernel

Actualizar `Program.cs` para registrar los tools:

```csharp
// Después de crear el kernel, registrar los tools
services.AddSingleton(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    
    // Registrar cada tool como plugin
    kernel.Plugins.AddFromObject(sp.GetRequiredService<ResearchTools>(), "Research");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<ContentTools>(), "Content");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<ImageTools>(), "Images");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<WordPressTools>(), "WordPress");
    kernel.Plugins.AddFromObject(sp.GetRequiredService<PinterestTools>(), "Pinterest");
    
    return kernel;
});
```

---

## Siguiente

Una vez implementados los tools, ir a `03-ORCHESTRATOR.md` para crear la Durable Function que los ejecuta.
