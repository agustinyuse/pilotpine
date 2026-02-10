# Pinterest + Blog + Affiliate: Sistema Autónomo con Microsoft Agent Framework

## Resumen Ejecutivo

Sistema 100% autónomo para generar $100 USD/semana mediante contenido automatizado sobre viajes, monetizado con ads y affiliate marketing.

**Stack:**
- Microsoft Agent Framework (Semantic Kernel + AutoGen unificado)
- Claude Sonnet 4.5 en Microsoft Foundry
- Azure Durable Functions (orquestación y resiliencia)
- Tools nativos en C# (sin MCP servers)

**Costo estimado:** ~$10-15 USD/mes

---

## 1. Arquitectura General

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         MICROSOFT FOUNDRY                               │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │                    DURABLE AZURE FUNCTION                         │  │
│  │                    Timer Trigger: 0 6 * * * (6 AM daily)          │  │
│  │                                                                   │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │              MICROSOFT AGENT FRAMEWORK                      │  │  │
│  │  │                                                             │  │  │
│  │  │  ┌─────────────────────────────────────────────────────┐   │  │  │
│  │  │  │  CONTENT PIPELINE AGENT                             │   │  │  │
│  │  │  │  Model: Claude Sonnet 4.5                           │   │  │  │
│  │  │  │                                                     │   │  │  │
│  │  │  │  Instructions:                                      │   │  │  │
│  │  │  │  "You are an autonomous content creator that        │   │  │  │
│  │  │  │   researches trends, generates SEO articles,        │   │  │  │
│  │  │  │   creates Pinterest pins, and publishes content     │   │  │  │
│  │  │  │   to WordPress and Pinterest automatically."        │   │  │  │
│  │  │  └─────────────────────────────────────────────────────┘   │  │  │
│  │  │                          │                                  │  │  │
│  │  │                          ▼                                  │  │  │
│  │  │  ┌─────────────────────────────────────────────────────┐   │  │  │
│  │  │  │              NATIVE TOOLS (C#)                      │   │  │  │
│  │  │  │                                                     │   │  │  │
│  │  │  │  ResearchTools      ContentTools     ImageTools     │   │  │  │
│  │  │  │  ├─ GetTrends()     ├─ GenArticle()  ├─ GenPin()   │   │  │  │
│  │  │  │  └─ GetKeywords()   └─ GenPinCopy()  └─ GenBanner()│   │  │  │
│  │  │  │                                                     │   │  │  │
│  │  │  │  WordPressTools     PinterestTools   StorageTools   │   │  │  │
│  │  │  │  ├─ PublishPost()   ├─ CreatePin()   ├─ SaveState()│   │  │  │
│  │  │  │  └─ UploadMedia()   └─ SchedulePin() └─ GetState() │   │  │  │
│  │  │  └─────────────────────────────────────────────────────┘   │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  │                                                                   │  │
│  │  Durable Entities: Estado persistente entre ejecuciones           │  │
│  │  Checkpointing: Recuperación automática ante fallos               │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  Observability: Application Insights + OpenTelemetry                    │
│  Governance: Foundry Control Plane                                      │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Estructura del Proyecto

```
/PinterestAffiliateAgent
│
├── /src
│   ├── /Agents
│   │   └── ContentPipelineAgent.cs      # Agente principal
│   │
│   ├── /Tools
│   │   ├── ResearchTools.cs             # Búsqueda de keywords y trends
│   │   ├── ContentTools.cs              # Generación de artículos
│   │   ├── ImageTools.cs                # Generación de imágenes
│   │   ├── WordPressTools.cs            # Publicación en WordPress
│   │   ├── PinterestTools.cs            # Publicación en Pinterest
│   │   └── StorageTools.cs              # Persistencia de estado
│   │
│   ├── /Functions
│   │   ├── DailyOrchestrator.cs         # Durable Function principal
│   │   ├── ContentOrchestration.cs      # Sub-orquestación por artículo
│   │   └── TimerTrigger.cs              # Trigger diario
│   │
│   ├── /Models
│   │   ├── Keyword.cs
│   │   ├── Article.cs
│   │   ├── PinContent.cs
│   │   └── PublishResult.cs
│   │
│   ├── /Config
│   │   ├── appsettings.json
│   │   ├── affiliates.json              # Links de afiliados por categoría
│   │   └── prompts.json                 # Prompts del agente
│   │
│   └── Program.cs
│
├── /tests
│   ├── Tools.Tests/
│   └── Integration.Tests/
│
├── host.json
├── local.settings.json
└── PinterestAffiliateAgent.csproj
```

---

## 3. Implementación de Tools Nativos

### 3.1 ResearchTools.cs

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PinterestAffiliateAgent.Tools;

public class ResearchTools
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ResearchTools> _logger;

    public ResearchTools(HttpClient httpClient, ILogger<ResearchTools> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    [KernelFunction]
    [Description("Gets trending travel topics from Pinterest Trends API for content ideas")]
    public async Task<List<string>> GetTrendingTopics(
        [Description("Category to search, e.g., 'travel', 'europe', 'beaches'")] string category,
        [Description("Number of trends to return")] int count = 10)
    {
        // Pinterest Trends API o scraping de trends
        // Retorna lista de temas trending
        _logger.LogInformation("Fetching {Count} trending topics for {Category}", count, category);
        
        // Implementación real aquí
        var trends = new List<string>
        {
            "hidden beaches portugal",
            "affordable europe destinations 2025",
            "best hiking trails alps"
        };
        
        return trends;
    }

    [KernelFunction]
    [Description("Analyzes a keyword for SEO potential including search volume and competition")]
    public async Task<KeywordAnalysis> AnalyzeKeyword(
        [Description("The keyword to analyze")] string keyword)
    {
        // Integración con herramienta SEO (ej: DataForSEO, SEMrush API)
        return new KeywordAnalysis
        {
            Keyword = keyword,
            SearchVolume = "medium",
            Competition = "low",
            SuggestedArticleType = "listicle",
            AffiliateOpportunities = new[] { "booking", "tours" }
        };
    }

    [KernelFunction]
    [Description("Gets list of keywords that haven't been published yet")]
    public async Task<List<string>> GetUnpublishedKeywords(
        [Description("Maximum keywords to return")] int maxCount = 5)
    {
        // Lee del storage qué keywords ya se publicaron
        // Retorna las pendientes
        return new List<string>();
    }
}

public record KeywordAnalysis
{
    public string Keyword { get; init; }
    public string SearchVolume { get; init; }
    public string Competition { get; init; }
    public string SuggestedArticleType { get; init; }
    public string[] AffiliateOpportunities { get; init; }
}
```

### 3.2 ContentTools.cs

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PinterestAffiliateAgent.Tools;

public class ContentTools
{
    private readonly AffiliateLinksService _affiliateService;

    public ContentTools(AffiliateLinksService affiliateService)
    {
        _affiliateService = affiliateService;
    }

    [KernelFunction]
    [Description("Generates a complete SEO-optimized blog article about a travel topic")]
    public async Task<Article> GenerateArticle(
        [Description("Main keyword for the article")] string keyword,
        [Description("Type of article: listicle, guide, comparison")] string articleType,
        [Description("Target word count")] int wordCount = 2500)
    {
        // El LLM genera el contenido, este tool estructura y post-procesa
        // Inserta affiliate links automáticamente
        
        var affiliateLinks = await _affiliateService.GetLinksForKeyword(keyword);
        
        return new Article
        {
            Title = $"15 Best {keyword} You Need to Visit in 2025",
            MetaDescription = $"Discover the best {keyword}. Local tips, insider secrets, and everything you need to plan your trip.",
            Content = "", // El agente llena esto
            AffiliatePlaceholders = affiliateLinks,
            Category = DetermineCategory(keyword),
            Tags = GenerateTags(keyword)
        };
    }

    [KernelFunction]
    [Description("Generates Pinterest pin copy variations for an article")]
    public async Task<List<PinCopy>> GeneratePinCopy(
        [Description("Article title")] string title,
        [Description("Brief article summary")] string summary,
        [Description("Number of variations to generate")] int variations = 5)
    {
        // Genera variaciones de copy para pins
        return new List<PinCopy>
        {
            new PinCopy { Headline = $"🌍 {title}", Description = summary, Style = "curiosity" },
            new PinCopy { Headline = $"You won't believe these {title}", Description = summary, Style = "clickbait" },
            new PinCopy { Headline = $"Complete guide: {title}", Description = summary, Style = "practical" }
        };
    }

    [KernelFunction]
    [Description("Inserts affiliate links into article content at appropriate positions")]
    public string InsertAffiliateLinks(
        [Description("Raw article content")] string content,
        [Description("JSON string of affiliate links to insert")] string affiliateLinksJson)
    {
        // Reemplaza placeholders con links reales
        var links = JsonSerializer.Deserialize<Dictionary<string, string>>(affiliateLinksJson);
        
        foreach (var (placeholder, url) in links)
        {
            content = content.Replace($"[AFFILIATE:{placeholder}]", url);
        }
        
        return content;
    }

    private string DetermineCategory(string keyword) => "travel";
    private string[] GenerateTags(string keyword) => keyword.Split(' ');
}

public record Article
{
    public string Title { get; init; }
    public string MetaDescription { get; init; }
    public string Content { get; set; }
    public Dictionary<string, string> AffiliatePlaceholders { get; init; }
    public string Category { get; init; }
    public string[] Tags { get; init; }
}

public record PinCopy
{
    public string Headline { get; init; }
    public string Description { get; init; }
    public string Style { get; init; }
}
```

### 3.3 ImageTools.cs

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PinterestAffiliateAgent.Tools;

public class ImageTools
{
    private readonly BannerbearClient _bannerbear;
    private readonly IConfiguration _config;

    public ImageTools(BannerbearClient bannerbear, IConfiguration config)
    {
        _bannerbear = bannerbear;
        _config = config;
    }

    [KernelFunction]
    [Description("Generates a Pinterest pin image with text overlay using a template")]
    public async Task<string> GeneratePinImage(
        [Description("Main headline text for the pin")] string headline,
        [Description("Background image URL or keyword for stock photo")] string backgroundSource,
        [Description("Template style: travel, listicle, guide, tips")] string templateStyle = "travel")
    {
        var templateId = _config[$"Bannerbear:Templates:{templateStyle}"];
        
        var modifications = new[]
        {
            new { name = "headline", text = headline },
            new { name = "background", image_url = backgroundSource }
        };

        var result = await _bannerbear.CreateImageAsync(templateId, modifications);
        
        return result.ImageUrl; // URL de la imagen generada
    }

    [KernelFunction]
    [Description("Gets a royalty-free stock photo URL for a given search term")]
    public async Task<string> GetStockPhoto(
        [Description("Search term for the photo")] string searchTerm,
        [Description("Orientation: portrait, landscape, square")] string orientation = "portrait")
    {
        // Integración con Unsplash, Pexels, o similar
        // Pinterest pins son 1000x1500 (portrait)
        return $"https://source.unsplash.com/1000x1500/?{Uri.EscapeDataString(searchTerm)}";
    }

    [KernelFunction]
    [Description("Generates multiple pin image variations for A/B testing")]
    public async Task<List<string>> GeneratePinVariations(
        [Description("List of headlines as JSON array")] string headlinesJson,
        [Description("Background image URL")] string backgroundUrl,
        [Description("Template style")] string templateStyle = "travel")
    {
        var headlines = JsonSerializer.Deserialize<List<string>>(headlinesJson);
        var imageUrls = new List<string>();

        foreach (var headline in headlines)
        {
            var url = await GeneratePinImage(headline, backgroundUrl, templateStyle);
            imageUrls.Add(url);
        }

        return imageUrls;
    }
}
```

### 3.4 WordPressTools.cs

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;
using WordPressPCL;

namespace PinterestAffiliateAgent.Tools;

public class WordPressTools
{
    private readonly WordPressClient _wpClient;
    private readonly ILogger<WordPressTools> _logger;

    public WordPressTools(IConfiguration config, ILogger<WordPressTools> logger)
    {
        _wpClient = new WordPressClient(config["WordPress:Url"]);
        _wpClient.Auth.UseBasicAuth(config["WordPress:Username"], config["WordPress:AppPassword"]);
        _logger = logger;
    }

    [KernelFunction]
    [Description("Publishes a blog post to WordPress and returns the post URL")]
    public async Task<PublishResult> PublishPost(
        [Description("Post title")] string title,
        [Description("Post content in HTML format")] string content,
        [Description("Meta description for SEO")] string metaDescription,
        [Description("Category slug")] string category,
        [Description("Comma-separated tags")] string tags,
        [Description("Featured image URL")] string featuredImageUrl)
    {
        try
        {
            // Subir imagen destacada primero
            var mediaId = await UploadMediaFromUrl(featuredImageUrl, title);

            var post = new Post
            {
                Title = new Title(title),
                Content = new Content(content),
                Excerpt = new Excerpt(metaDescription),
                Status = Status.Publish,
                FeaturedMedia = mediaId,
                Categories = await GetCategoryIds(category),
                Tags = await GetOrCreateTagIds(tags.Split(','))
            };

            var createdPost = await _wpClient.Posts.CreateAsync(post);
            
            _logger.LogInformation("Published post: {Url}", createdPost.Link);

            return new PublishResult
            {
                Success = true,
                PostId = createdPost.Id,
                PostUrl = createdPost.Link,
                PublishedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish post: {Title}", title);
            return new PublishResult { Success = false, Error = ex.Message };
        }
    }

    [KernelFunction]
    [Description("Uploads an image to WordPress media library from a URL")]
    public async Task<int> UploadMediaFromUrl(
        [Description("URL of the image to upload")] string imageUrl,
        [Description("Alt text for the image")] string altText)
    {
        using var httpClient = new HttpClient();
        var imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
        var fileName = $"{Guid.NewGuid()}.jpg";

        var media = await _wpClient.Media.CreateAsync(imageBytes, fileName, "image/jpeg");
        
        // Actualizar alt text
        media.AltText = altText;
        await _wpClient.Media.UpdateAsync(media);

        return media.Id;
    }

    [KernelFunction]
    [Description("Checks if a post with similar title already exists")]
    public async Task<bool> PostExists(
        [Description("Post title to check")] string title)
    {
        var posts = await _wpClient.Posts.QueryAsync(new PostsQueryBuilder
        {
            Search = title,
            PerPage = 1
        });

        return posts.Any();
    }

    private async Task<int[]> GetCategoryIds(string categorySlug)
    {
        var categories = await _wpClient.Categories.GetAllAsync();
        var category = categories.FirstOrDefault(c => c.Slug == categorySlug);
        return category != null ? new[] { category.Id } : Array.Empty<int>();
    }

    private async Task<int[]> GetOrCreateTagIds(string[] tagNames)
    {
        var tagIds = new List<int>();
        var existingTags = await _wpClient.Tags.GetAllAsync();

        foreach (var tagName in tagNames.Select(t => t.Trim().ToLower()))
        {
            var existing = existingTags.FirstOrDefault(t => t.Name.ToLower() == tagName);
            if (existing != null)
            {
                tagIds.Add(existing.Id);
            }
            else
            {
                var newTag = await _wpClient.Tags.CreateAsync(new Tag { Name = tagName });
                tagIds.Add(newTag.Id);
            }
        }

        return tagIds.ToArray();
    }
}

public record PublishResult
{
    public bool Success { get; init; }
    public int PostId { get; init; }
    public string PostUrl { get; init; }
    public DateTime PublishedAt { get; init; }
    public string Error { get; init; }
}
```

### 3.5 PinterestTools.cs

```csharp
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace PinterestAffiliateAgent.Tools;

public class PinterestTools
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<PinterestTools> _logger;
    private readonly string _accessToken;

    public PinterestTools(HttpClient httpClient, IConfiguration config, ILogger<PinterestTools> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _accessToken = config["Pinterest:AccessToken"];
    }

    [KernelFunction]
    [Description("Creates a Pinterest pin and returns the pin ID")]
    public async Task<PinResult> CreatePin(
        [Description("Board ID where to post the pin")] string boardId,
        [Description("Pin title (max 100 chars)")] string title,
        [Description("Pin description (max 500 chars)")] string description,
        [Description("Destination URL (your blog post)")] string linkUrl,
        [Description("Image URL for the pin")] string imageUrl)
    {
        var requestBody = new
        {
            board_id = boardId,
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
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var result = JsonSerializer.Deserialize<PinterestPinResponse>(content);
            _logger.LogInformation("Created pin: {PinId}", result.Id);
            
            return new PinResult
            {
                Success = true,
                PinId = result.Id,
                PinUrl = $"https://pinterest.com/pin/{result.Id}"
            };
        }

        _logger.LogError("Failed to create pin: {Error}", content);
        return new PinResult { Success = false, Error = content };
    }

    [KernelFunction]
    [Description("Gets all boards for the authenticated Pinterest account")]
    public async Task<List<PinterestBoard>> GetBoards()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.pinterest.com/v5/boards");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        
        var result = JsonSerializer.Deserialize<PinterestBoardsResponse>(content);
        return result.Items;
    }

    [KernelFunction]
    [Description("Selects the best board for a given topic based on board names")]
    public async Task<string> SelectBoardForTopic(
        [Description("Topic or keyword of the content")] string topic)
    {
        var boards = await GetBoards();
        
        // Lógica simple: buscar match en nombre del board
        var topicLower = topic.ToLower();
        var matchingBoard = boards.FirstOrDefault(b => 
            b.Name.ToLower().Contains(topicLower) ||
            topicLower.Contains(b.Name.ToLower()));

        return matchingBoard?.Id ?? boards.First().Id; // Default al primer board
    }

    [KernelFunction]
    [Description("Creates multiple pins for the same article with different images/copy")]
    public async Task<List<PinResult>> CreatePinVariations(
        [Description("Board ID")] string boardId,
        [Description("Blog post URL")] string postUrl,
        [Description("JSON array of pin variations with title, description, imageUrl")] string variationsJson)
    {
        var variations = JsonSerializer.Deserialize<List<PinVariation>>(variationsJson);
        var results = new List<PinResult>();

        foreach (var variation in variations)
        {
            // Rate limiting: Pinterest permite ~10 pins/hora por board
            await Task.Delay(TimeSpan.FromSeconds(30));
            
            var result = await CreatePin(
                boardId,
                variation.Title,
                variation.Description,
                postUrl,
                variation.ImageUrl
            );
            results.Add(result);
        }

        return results;
    }
}

public record PinResult
{
    public bool Success { get; init; }
    public string PinId { get; init; }
    public string PinUrl { get; init; }
    public string Error { get; init; }
}

public record PinVariation
{
    public string Title { get; init; }
    public string Description { get; init; }
    public string ImageUrl { get; init; }
}

public record PinterestBoard
{
    public string Id { get; init; }
    public string Name { get; init; }
}
```

---

## 4. Durable Function Orchestrator

### 4.1 DailyOrchestrator.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;

namespace PinterestAffiliateAgent.Functions;

public class DailyOrchestrator
{
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(ILogger<DailyOrchestrator> logger)
    {
        _logger = logger;
    }

    // Timer trigger: runs daily at 6 AM UTC
    [Function("DailyTrigger")]
    public async Task DailyTrigger(
        [TimerTrigger("0 0 6 * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client)
    {
        var instanceId = $"daily-{DateTime.UtcNow:yyyy-MM-dd}";
        
        // Verificar si ya hay una ejecución hoy
        var existing = await client.GetInstanceAsync(instanceId);
        if (existing?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        {
            _logger.LogWarning("Orchestration already running for today");
            return;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ContentPipelineOrchestration),
            new PipelineInput { ArticlesToGenerate = 3, Date = DateTime.UtcNow },
            new StartOrchestrationOptions { InstanceId = instanceId }
        );

        _logger.LogInformation("Started daily orchestration: {InstanceId}", instanceId);
    }

    [Function(nameof(ContentPipelineOrchestration))]
    public async Task<PipelineResult> ContentPipelineOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<PipelineInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();
        var results = new List<ArticleResult>();

        // Step 1: Research trending keywords
        var keywords = await context.CallActivityAsync<List<string>>(
            nameof(ResearchKeywordsActivity),
            input.ArticlesToGenerate
        );

        logger.LogInformation("Found {Count} keywords to process", keywords.Count);

        // Step 2: Process each keyword (can be parallelized)
        var tasks = keywords.Select(keyword =>
            context.CallSubOrchestrationAsync<ArticleResult>(
                nameof(ArticleOrchestration),
                new ArticleInput { Keyword = keyword }
            )
        );

        var articleResults = await Task.WhenAll(tasks);
        results.AddRange(articleResults);

        // Step 3: Log results and update state
        await context.CallActivityAsync(
            nameof(SaveDailyResultsActivity),
            results
        );

        return new PipelineResult
        {
            Date = input.Date,
            ArticlesPublished = results.Count(r => r.Success),
            TotalPinsCreated = results.Sum(r => r.PinsCreated),
            Errors = results.Where(r => !r.Success).Select(r => r.Error).ToList()
        };
    }

    [Function(nameof(ArticleOrchestration))]
    public async Task<ArticleResult> ArticleOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ArticleInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        try
        {
            // Step 1: Generate article content using Agent
            var article = await context.CallActivityAsync<Article>(
                nameof(GenerateArticleActivity),
                input.Keyword
            );

            // Step 2: Generate pin images
            var pinImages = await context.CallActivityAsync<List<string>>(
                nameof(GeneratePinImagesActivity),
                new PinImageInput { Title = article.Title, Keyword = input.Keyword }
            );

            // Step 3: Publish to WordPress
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article
            );

            if (!wpResult.Success)
            {
                return new ArticleResult 
                { 
                    Success = false, 
                    Error = $"WordPress publish failed: {wpResult.Error}" 
                };
            }

            // Step 4: Create Pinterest pins
            var pinResults = await context.CallActivityAsync<List<PinResult>>(
                nameof(CreatePinterestPinsActivity),
                new PinterestInput
                {
                    PostUrl = wpResult.PostUrl,
                    Title = article.Title,
                    PinImages = pinImages
                }
            );

            return new ArticleResult
            {
                Success = true,
                Keyword = input.Keyword,
                PostUrl = wpResult.PostUrl,
                PinsCreated = pinResults.Count(p => p.Success)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process article for keyword: {Keyword}", input.Keyword);
            return new ArticleResult
            {
                Success = false,
                Keyword = input.Keyword,
                Error = ex.Message
            };
        }
    }
}

// Input/Output records
public record PipelineInput
{
    public int ArticlesToGenerate { get; init; }
    public DateTime Date { get; init; }
}

public record PipelineResult
{
    public DateTime Date { get; init; }
    public int ArticlesPublished { get; init; }
    public int TotalPinsCreated { get; init; }
    public List<string> Errors { get; init; }
}

public record ArticleInput
{
    public string Keyword { get; init; }
}

public record ArticleResult
{
    public bool Success { get; init; }
    public string Keyword { get; init; }
    public string PostUrl { get; init; }
    public int PinsCreated { get; init; }
    public string Error { get; init; }
}
```

### 4.2 Activities.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

namespace PinterestAffiliateAgent.Functions;

public class PipelineActivities
{
    private readonly Kernel _kernel;
    private readonly ResearchTools _researchTools;
    private readonly ContentTools _contentTools;
    private readonly ImageTools _imageTools;
    private readonly WordPressTools _wordPressTools;
    private readonly PinterestTools _pinterestTools;

    public PipelineActivities(
        Kernel kernel,
        ResearchTools researchTools,
        ContentTools contentTools,
        ImageTools imageTools,
        WordPressTools wordPressTools,
        PinterestTools pinterestTools)
    {
        _kernel = kernel;
        _researchTools = researchTools;
        _contentTools = contentTools;
        _imageTools = imageTools;
        _wordPressTools = wordPressTools;
        _pinterestTools = pinterestTools;
    }

    [Function(nameof(ResearchKeywordsActivity))]
    public async Task<List<string>> ResearchKeywordsActivity(
        [ActivityTrigger] int count)
    {
        var trends = await _researchTools.GetTrendingTopics("travel europe", count * 2);
        var unpublished = await _researchTools.GetUnpublishedKeywords(count);
        
        // Combinar trends nuevos con keywords pendientes
        return unpublished.Concat(trends).Distinct().Take(count).ToList();
    }

    [Function(nameof(GenerateArticleActivity))]
    public async Task<Article> GenerateArticleActivity(
        [ActivityTrigger] string keyword)
    {
        // Crear agente con Claude Sonnet
        var agent = new ChatCompletionAgent
        {
            Name = "ContentWriter",
            Instructions = """
                You are an expert travel content writer. Generate engaging, 
                SEO-optimized articles about travel destinations. 
                Include practical tips, insider knowledge, and always mention 
                where readers can book hotels and tours.
                Use [AFFILIATE:booking] and [AFFILIATE:tours] placeholders 
                where affiliate links should go.
                """,
            Kernel = _kernel
        };

        var analysis = await _researchTools.AnalyzeKeyword(keyword);
        
        var prompt = $"""
            Write a comprehensive {analysis.SuggestedArticleType} article about: {keyword}
            
            Requirements:
            - 2000-2500 words
            - Include a catchy title with the current year
            - Write an engaging meta description (155 chars max)
            - Use H2 and H3 headers appropriately
            - Include [AFFILIATE:booking] placeholders for hotel recommendations
            - Include [AFFILIATE:tours] placeholders for tour recommendations
            - Add a FAQ section at the end
            - Be informative but conversational
            """;

        var response = await agent.InvokeAsync(prompt);
        var content = response.Content;

        // Post-procesar con ContentTools
        var article = await _contentTools.GenerateArticle(keyword, analysis.SuggestedArticleType);
        article.Content = content;
        
        // Insertar affiliate links
        var affiliateLinks = JsonSerializer.Serialize(article.AffiliatePlaceholders);
        article.Content = _contentTools.InsertAffiliateLinks(article.Content, affiliateLinks);

        return article;
    }

    [Function(nameof(GeneratePinImagesActivity))]
    public async Task<List<string>> GeneratePinImagesActivity(
        [ActivityTrigger] PinImageInput input)
    {
        var stockPhoto = await _imageTools.GetStockPhoto(input.Keyword, "portrait");
        
        var headlines = new List<string>
        {
            input.Title,
            $"🌍 {input.Title}",
            $"Don't miss: {input.Title}"
        };

        var images = await _imageTools.GeneratePinVariations(
            JsonSerializer.Serialize(headlines),
            stockPhoto,
            "travel"
        );

        return images;
    }

    [Function(nameof(PublishToWordPressActivity))]
    public async Task<PublishResult> PublishToWordPressActivity(
        [ActivityTrigger] Article article)
    {
        // Verificar si ya existe
        if (await _wordPressTools.PostExists(article.Title))
        {
            return new PublishResult 
            { 
                Success = false, 
                Error = "Article with similar title already exists" 
            };
        }

        return await _wordPressTools.PublishPost(
            article.Title,
            article.Content,
            article.MetaDescription,
            article.Category,
            string.Join(",", article.Tags),
            "" // Featured image se genera aparte
        );
    }

    [Function(nameof(CreatePinterestPinsActivity))]
    public async Task<List<PinResult>> CreatePinterestPinsActivity(
        [ActivityTrigger] PinterestInput input)
    {
        var boardId = await _pinterestTools.SelectBoardForTopic(input.Title);
        var results = new List<PinResult>();

        for (int i = 0; i < input.PinImages.Count; i++)
        {
            var pinCopy = await _contentTools.GeneratePinCopy(input.Title, "", 1);
            var copy = pinCopy.First();

            var result = await _pinterestTools.CreatePin(
                boardId,
                copy.Headline,
                copy.Description,
                input.PostUrl,
                input.PinImages[i]
            );

            results.Add(result);

            // Rate limiting
            await Task.Delay(TimeSpan.FromSeconds(30));
        }

        return results;
    }

    [Function(nameof(SaveDailyResultsActivity))]
    public async Task SaveDailyResultsActivity(
        [ActivityTrigger] List<ArticleResult> results)
    {
        // Guardar en Table Storage o Cosmos DB
        // Para tracking y analytics
    }
}

public record PinImageInput
{
    public string Title { get; init; }
    public string Keyword { get; init; }
}

public record PinterestInput
{
    public string PostUrl { get; init; }
    public string Title { get; init; }
    public List<string> PinImages { get; init; }
}
```

---

## 5. Configuración del Agente

### 5.1 Program.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Azure.Identity;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Configurar Semantic Kernel con Claude en Foundry
        services.AddKernel()
            .AddAzureAIInferenceChatCompletion(
                endpoint: new Uri(config["Foundry:Endpoint"]),
                credential: new DefaultAzureCredential(),
                modelId: "claude-sonnet-4-5" // Claude Sonnet 4.5 en Foundry
            );

        // Registrar Tools como servicios
        services.AddSingleton<ResearchTools>();
        services.AddSingleton<ContentTools>();
        services.AddSingleton<ImageTools>();
        services.AddSingleton<WordPressTools>();
        services.AddSingleton<PinterestTools>();
        
        // Servicios auxiliares
        services.AddSingleton<AffiliateLinksService>();
        services.AddSingleton<BannerbearClient>();
        
        // HttpClient para APIs externas
        services.AddHttpClient();

        // Registrar Tools en el Kernel
        services.AddSingleton(sp =>
        {
            var kernel = sp.GetRequiredService<Kernel>();
            
            kernel.Plugins.AddFromObject(sp.GetRequiredService<ResearchTools>(), "Research");
            kernel.Plugins.AddFromObject(sp.GetRequiredService<ContentTools>(), "Content");
            kernel.Plugins.AddFromObject(sp.GetRequiredService<ImageTools>(), "Images");
            kernel.Plugins.AddFromObject(sp.GetRequiredService<WordPressTools>(), "WordPress");
            kernel.Plugins.AddFromObject(sp.GetRequiredService<PinterestTools>(), "Pinterest");
            
            return kernel;
        });

        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

await host.RunAsync();
```

### 5.2 appsettings.json

```json
{
  "Foundry": {
    "Endpoint": "https://<your-resource>.services.ai.azure.com",
    "ModelId": "claude-sonnet-4-5"
  },
  "WordPress": {
    "Url": "https://yourblog.com/wp-json/wp/v2",
    "Username": "your-username",
    "AppPassword": "your-app-password"
  },
  "Pinterest": {
    "AccessToken": "your-pinterest-access-token",
    "DefaultBoardId": "your-default-board-id"
  },
  "Bannerbear": {
    "ApiKey": "your-bannerbear-api-key",
    "Templates": {
      "travel": "template-id-1",
      "listicle": "template-id-2",
      "guide": "template-id-3"
    }
  },
  "Affiliates": {
    "Booking": {
      "BaseUrl": "https://www.booking.com",
      "AffiliateId": "your-affiliate-id"
    },
    "GetYourGuide": {
      "BaseUrl": "https://www.getyourguide.com",
      "PartnerId": "your-partner-id"
    }
  },
  "Storage": {
    "ConnectionString": "your-storage-connection-string"
  }
}
```

### 5.3 host.json

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true,
        "excludedTypes": "Request"
      },
      "enableLiveMetricsFilters": true
    }
  },
  "extensions": {
    "durableTask": {
      "storageProvider": {
        "type": "DurableTaskScheduler",
        "connectionStringName": "DurableTaskScheduler"
      },
      "maxConcurrentActivityFunctions": 3,
      "maxConcurrentOrchestratorFunctions": 5
    }
  },
  "functionTimeout": "00:30:00"
}
```

---

## 6. Storage Providers y Checkpointing (Recuperación de Fallos)

### 6.1 ¿Qué es el Checkpointing?

Durable Functions guarda automáticamente el estado después de cada `await`. Si falla, retoma desde el último punto exitoso **sin repetir trabajo**.

```
Ejecución normal:
    │
    [ResearchKeywords] ✓ ──► checkpoint guardado
    │
    [GenerateArticle] ✓ ──► checkpoint guardado  ($0.06 de Claude ya gastados)
    │
    [GeneratePinImages] ✗ ──► FALLA AQUÍ (ej: Bannerbear timeout)
    │
    
Cuando se reinicia (automático o manual):
    │
    [ResearchKeywords] ⏭️ ──► SKIP (usa resultado guardado)
    │
    [GenerateArticle] ⏭️ ──► SKIP (usa resultado guardado, NO gasta Claude de nuevo)
    │
    [GeneratePinImages] 🔄 ──► RE-EJECUTA desde aquí
    │
    [PublishWordPress] ▶️ ──► continúa normal
```

**Beneficio clave:** Si Claude generó un artículo ($0.06) y falla después, NO volvés a pagar por regenerarlo.

### 6.2 Opciones de Storage Provider

| Provider | Costo Mensual | Complejidad | Recomendación |
|----------|---------------|-------------|---------------|
| **Azure Storage (default)** | ~$0.50-2 | Ninguna | ✅ Para tu caso |
| Durable Task Scheduler (Consumption) | ~$0.01 por 10K acciones | Baja | ✅ Alternativa moderna |
| Durable Task Scheduler (Dedicated) | ~$70+/mes | Baja | ❌ Overkill |
| MSSQL (Azure SQL Serverless) | ~$5+/mes | Media | ⚠️ Si ya tenés SQL |
| Netherite | Event Hubs + Storage | Alta | ❌ End of support 2028 |

### 6.3 Opción Recomendada: Azure Storage (Default)

Para tu volumen (3 ejecuciones/día, ~90/mes), Azure Storage es **la opción más económica**:

```
Cálculo de costo mensual:

Operaciones de storage:
├── Writes (checkpoints): ~500/mes × $0.0004 = $0.20
├── Reads (replays): ~200/mes × $0.0004 = $0.08
├── Queue messages: ~1000/mes × $0.0004 = $0.40
└── Blob storage: <1 GB × $0.02 = $0.02

Total: ~$0.70/mes
```

**Configuración (ya incluida por default):**

```json
// host.json - No necesita configuración extra
{
  "version": "2.0",
  "extensions": {
    "durableTask": {
      "storageProvider": {
        "type": "AzureStorage"  // Default, no hace falta especificar
      }
    }
  }
}
```

### 6.4 Alternativa Moderna: Durable Task Scheduler (Consumption SKU)

Si querés el nuevo servicio managed con mejor dashboard y monitoring:

```json
// host.json con Durable Task Scheduler
{
  "version": "2.0",
  "extensions": {
    "durableTask": {
      "storageProvider": {
        "type": "DurableTaskScheduler",
        "connectionStringName": "DurableTaskScheduler"
      }
    }
  }
}
```

**Pricing Consumption SKU:**
- $0.01 USD por cada 10,000 acciones
- Tu caso: ~500 acciones/día × 30 = 15,000/mes = **~$0.015/mes**
- Retención de datos: 30 días
- Dashboard de monitoreo incluido

### 6.5 Implementación de Retry y Recovery

```csharp
[Function(nameof(ArticleOrchestration))]
public async Task<ArticleResult> ArticleOrchestration(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    var input = context.GetInput<ArticleInput>();
    var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

    // ═══════════════════════════════════════════════════════════════
    // RETRY POLICIES - Diferentes estrategias según el tipo de fallo
    // ═══════════════════════════════════════════════════════════════

    // Para APIs externas (Pinterest, WordPress) - más reintentos
    var externalApiRetry = new TaskRetryOptions(
        firstRetryInterval: TimeSpan.FromSeconds(10),
        maxNumberOfAttempts: 5
    )
    {
        BackoffCoefficient = 2.0,        // Exponential: 10s, 20s, 40s, 80s, 160s
        MaxRetryInterval = TimeSpan.FromMinutes(3),
        RetryTimeout = TimeSpan.FromMinutes(10)
    };

    // Para Claude/LLM - menos reintentos (si falla, algo está mal)
    var llmRetry = new TaskRetryOptions(
        firstRetryInterval: TimeSpan.FromSeconds(30),
        maxNumberOfAttempts: 3
    )
    {
        BackoffCoefficient = 1.5,
        MaxRetryInterval = TimeSpan.FromMinutes(2)
    };

    // Para generación de imágenes - timeout más largo
    var imageRetry = new TaskRetryOptions(
        firstRetryInterval: TimeSpan.FromSeconds(15),
        maxNumberOfAttempts: 4
    )
    {
        BackoffCoefficient = 2.0,
        MaxRetryInterval = TimeSpan.FromMinutes(5)
    };

    try
    {
        // ════════════════════════════════════════════════════════
        // STEP 1: Generar artículo (checkpoint automático después)
        // ════════════════════════════════════════════════════════
        // Si falla aquí y se reinicia, NO ejecuta de nuevo
        var article = await context.CallActivityAsync<Article>(
            nameof(GenerateArticleActivity),
            input.Keyword,
            new TaskActivityOptions { Retry = llmRetry }
        );
        
        logger.LogInformation("✓ Article generated: {Title}", article.Title);
        // >>> CHECKPOINT: artículo guardado en storage <<<

        // ════════════════════════════════════════════════════════
        // STEP 2: Generar imágenes (puede tardar, retry largo)
        // ════════════════════════════════════════════════════════
        var pinImages = await context.CallActivityAsync<List<string>>(
            nameof(GeneratePinImagesActivity),
            new PinImageInput { Title = article.Title, Keyword = input.Keyword },
            new TaskActivityOptions { Retry = imageRetry }
        );
        
        logger.LogInformation("✓ Generated {Count} pin images", pinImages.Count);
        // >>> CHECKPOINT: imágenes guardadas <<<

        // ════════════════════════════════════════════════════════
        // STEP 3: Publicar en WordPress
        // ════════════════════════════════════════════════════════
        var wpResult = await context.CallActivityAsync<PublishResult>(
            nameof(PublishToWordPressActivity),
            article,
            new TaskActivityOptions { Retry = externalApiRetry }
        );

        if (!wpResult.Success)
        {
            // Guardar error pero continuar con siguiente artículo
            logger.LogWarning("WordPress failed: {Error}", wpResult.Error);
            return new ArticleResult 
            { 
                Success = false, 
                Keyword = input.Keyword,
                Error = $"WordPress: {wpResult.Error}",
                PartialData = new { Article = article.Title } // Guardar lo que se generó
            };
        }
        
        logger.LogInformation("✓ Published to WordPress: {Url}", wpResult.PostUrl);
        // >>> CHECKPOINT: URL del post guardada <<<

        // ════════════════════════════════════════════════════════
        // STEP 4: Crear pins en Pinterest (rate limits frecuentes)
        // ════════════════════════════════════════════════════════
        var pinResults = await context.CallActivityAsync<List<PinResult>>(
            nameof(CreatePinterestPinsActivity),
            new PinterestInput
            {
                PostUrl = wpResult.PostUrl,
                Title = article.Title,
                PinImages = pinImages
            },
            new TaskActivityOptions { Retry = externalApiRetry }
        );

        var successfulPins = pinResults.Count(p => p.Success);
        logger.LogInformation("✓ Created {Count}/{Total} pins", successfulPins, pinResults.Count);

        return new ArticleResult
        {
            Success = true,
            Keyword = input.Keyword,
            PostUrl = wpResult.PostUrl,
            PinsCreated = successfulPins,
            PinsFailed = pinResults.Count - successfulPins
        };
    }
    catch (TaskFailedException ex) when (ex.FailureDetails.IsCausedBy<TimeoutException>())
    {
        // Timeout específico - guardar progreso parcial
        logger.LogError("Timeout in orchestration for {Keyword}", input.Keyword);
        return new ArticleResult
        {
            Success = false,
            Keyword = input.Keyword,
            Error = "Timeout - will retry next run",
            ShouldRetryNextDay = true
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled error for keyword: {Keyword}", input.Keyword);
        return new ArticleResult
        {
            Success = false,
            Keyword = input.Keyword,
            Error = ex.Message
        };
    }
}
```

### 6.6 Estado Persistente entre Días (Durable Entities)

Para recordar qué keywords ya publicaste (evitar duplicados):

```csharp
// ═══════════════════════════════════════════════════════════════
// DURABLE ENTITY: Estado que persiste para siempre
// ═══════════════════════════════════════════════════════════════

[Function(nameof(PublishedContentEntity))]
public static Task PublishedContentEntity(
    [EntityTrigger] TaskEntityDispatcher dispatcher)
{
    return dispatcher.DispatchAsync<PublishedContentState>();
}

public class PublishedContentState
{
    // Keywords ya publicadas
    public HashSet<string> PublishedKeywords { get; set; } = new();
    
    // Historial de publicaciones
    public List<PublicationRecord> History { get; set; } = new();
    
    // Keywords que fallaron (para reintentar)
    public Queue<string> FailedKeywords { get; set; } = new();

    // ─── Operaciones ───
    
    public void MarkPublished(string keyword, string postUrl)
    {
        PublishedKeywords.Add(keyword.ToLowerInvariant());
        History.Add(new PublicationRecord
        {
            Keyword = keyword,
            PostUrl = postUrl,
            PublishedAt = DateTime.UtcNow
        });
        
        // Mantener solo últimos 1000 registros
        if (History.Count > 1000)
            History.RemoveAt(0);
    }

    public bool IsPublished(string keyword) 
        => PublishedKeywords.Contains(keyword.ToLowerInvariant());

    public void AddFailedKeyword(string keyword)
    {
        if (!FailedKeywords.Contains(keyword))
            FailedKeywords.Enqueue(keyword);
    }

    public string? GetNextFailedKeyword()
        => FailedKeywords.TryDequeue(out var kw) ? kw : null;

    public int GetPublishedCount() => PublishedKeywords.Count;
    
    public List<PublicationRecord> GetRecentHistory(int count) 
        => History.TakeLast(count).ToList();
}

public record PublicationRecord
{
    public string Keyword { get; init; }
    public string PostUrl { get; init; }
    public DateTime PublishedAt { get; init; }
}
```

**Uso en el Orchestrator:**

```csharp
[Function(nameof(ContentPipelineOrchestration))]
public async Task<PipelineResult> ContentPipelineOrchestration(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    var input = context.GetInput<PipelineInput>();
    
    // Obtener referencia a la entidad de estado
    var stateId = new EntityInstanceId(nameof(PublishedContentEntity), "global");
    var state = context.Entities.GetEntityProxy<PublishedContentState>(stateId);

    // Obtener keywords del research
    var allKeywords = await context.CallActivityAsync<List<string>>(
        nameof(ResearchKeywordsActivity),
        input.ArticlesToGenerate * 2  // Pedir el doble por si hay duplicados
    );

    // Filtrar las que ya se publicaron
    var newKeywords = new List<string>();
    foreach (var keyword in allKeywords)
    {
        if (!await state.IsPublished(keyword))
        {
            newKeywords.Add(keyword);
            if (newKeywords.Count >= input.ArticlesToGenerate)
                break;
        }
    }

    // Agregar keywords que fallaron anteriormente
    var failedKeyword = await state.GetNextFailedKeyword();
    if (failedKeyword != null && !newKeywords.Contains(failedKeyword))
    {
        newKeywords.Insert(0, failedKeyword);  // Priorizar retry
    }

    // Procesar cada keyword
    var results = new List<ArticleResult>();
    foreach (var keyword in newKeywords.Take(input.ArticlesToGenerate))
    {
        var result = await context.CallSubOrchestrationAsync<ArticleResult>(
            nameof(ArticleOrchestration),
            new ArticleInput { Keyword = keyword }
        );

        if (result.Success)
        {
            // Marcar como publicado (persiste para siempre)
            await state.MarkPublished(keyword, result.PostUrl);
        }
        else if (result.ShouldRetryNextDay)
        {
            // Guardar para reintentar mañana
            await state.AddFailedKeyword(keyword);
        }

        results.Add(result);
    }

    return new PipelineResult
    {
        Date = input.Date,
        ArticlesPublished = results.Count(r => r.Success),
        TotalPinsCreated = results.Sum(r => r.PinsCreated),
        TotalKeywordsPublished = await state.GetPublishedCount(),
        Errors = results.Where(r => !r.Success).Select(r => r.Error).ToList()
    };
}
```

### 6.7 Visualización del Recovery en Azure Portal

Cuando hay fallos, podés ver exactamente dónde quedó:

```
Orchestration: daily-2025-01-03
Status: Completed (with partial failures)
Duration: 12m 34s

├── ResearchKeywords: ✅ Completed (45s)
│   └── Output: ["hidden beaches algarve", "budget paris tips", "swiss alps hiking"]
│
├── ArticleOrchestration["hidden beaches algarve"]: ✅ Completed
│   ├── GenerateArticle: ✅ Completed (2m 15s)
│   ├── GeneratePinImages: ✅ Completed (45s)
│   ├── PublishWordPress: ✅ Completed (12s)
│   └── CreatePins: ✅ Completed (3 pins created)
│
├── ArticleOrchestration["budget paris tips"]: ✅ Completed
│   ├── GenerateArticle: ✅ Completed (2m 30s)
│   ├── GeneratePinImages: ✅ Completed (50s)
│   ├── PublishWordPress: ✅ Completed (10s)
│   └── CreatePins: ✅ Completed (3 pins created)
│
└── ArticleOrchestration["swiss alps hiking"]: ⚠️ Partial
    ├── GenerateArticle: ✅ Completed (2m 45s)
    ├── GeneratePinImages: ✅ Completed (55s)
    ├── PublishWordPress: ✅ Completed (11s)
    └── CreatePins: ⚠️ Partial (2/3 pins, 1 rate limited)
        ├── Attempt 1: ❌ Rate limited
        ├── Attempt 2: ❌ Rate limited
        ├── Attempt 3: ✅ 2 pins created
        └── Attempt 4: ❌ Max retries reached for 1 pin

Summary:
├── Articles published: 3/3
├── Pins created: 8/9
├── Claude tokens used: ~18,000
├── Estimated cost: $0.36
└── Failed keywords queued for retry: 0
```

### 6.8 Comparación de Costos por Provider

| Provider | Setup | Costo 90 orquestaciones/mes | Features |
|----------|-------|---------------------------|----------|
| **Azure Storage** | Ninguno | ~$0.70 | Básico, confiable |
| **DTS Consumption** | Crear resource | ~$0.02 | Dashboard, managed |
| DTS Dedicated | Crear resource | ~$70+ | Enterprise, HA |
| Azure SQL Serverless | DB + config | ~$5+ | Portable, SQL queries |

**Recomendación final:** Empezá con **Azure Storage** (default). Si querés mejor observability, migrá a **DTS Consumption** cuando esté más maduro.

---

## 7. Costos Detallados

### 6.1 Claude Sonnet 4.5 en Foundry

| Operación | Tokens Input | Tokens Output | Costo |
|-----------|--------------|---------------|-------|
| Research (1x) | 500 | 1,000 | $0.02 |
| Article generation (1x) | 2,000 | 3,500 | $0.06 |
| Pin copy generation (5x) | 1,500 | 2,500 | $0.04 |
| **Total por artículo** | ~4,000 | ~7,000 | **~$0.12** |

**90 artículos/mes = ~$10.80**

Con prompt caching (contenido repetido):
**Estimado real: ~$6-8/mes**

### 6.2 Azure Functions (Flex Consumption)

| Recurso | Uso Estimado | Costo |
|---------|--------------|-------|
| Ejecuciones | ~3,000/mes | Gratis (1M incluidas) |
| GB-segundos | ~10,000/mes | ~$0.16 |
| Durable storage | ~1 GB | ~$0.05 |
| **Total** | | **~$0.21/mes** |

### 6.3 Servicios Externos

| Servicio | Uso | Costo |
|----------|-----|-------|
| WordPress hosting (Hostinger) | 1 sitio | $3-5/mes |
| Bannerbear (imágenes) | 100 imágenes | $0 (free tier) o $15 |
| Unsplash | Stock photos | $0 (gratis) |
| Pinterest API | Gratis | $0 |
| **Total** | | **$3-20/mes** |

### 6.4 Resumen de Costos

| Escenario | Costo Mensual |
|-----------|---------------|
| Mínimo (free tiers) | ~$10-12 |
| Típico | ~$15-20 |
| Máximo (todo pago) | ~$30-35 |

---

## 7. Flujo de Ejecución Diaria

```
06:00 UTC - Timer Trigger dispara
    │
    ▼
[DailyOrchestrator] Inicia orquestación
    │
    ├── Verifica si ya corrió hoy
    │
    ▼
[ResearchKeywordsActivity]
    │
    ├── Obtiene trending topics
    ├── Filtra keywords ya publicadas
    └── Retorna 3 keywords
    │
    ▼
[ArticleOrchestration] x3 (paralelo)
    │
    ├── [GenerateArticleActivity]
    │   ├── Agent + Claude genera artículo
    │   ├── Inserta affiliate links
    │   └── Retorna Article
    │
    ├── [GeneratePinImagesActivity]
    │   ├── Obtiene stock photo
    │   ├── Genera 3 variaciones de pin
    │   └── Retorna URLs de imágenes
    │
    ├── [PublishToWordPressActivity]
    │   ├── Sube imagen destacada
    │   ├── Publica post
    │   └── Retorna URL del post
    │
    └── [CreatePinterestPinsActivity]
        ├── Selecciona board apropiado
        ├── Crea 3 pins con diferentes copies
        └── Retorna resultados
    │
    ▼
[SaveDailyResultsActivity]
    │
    └── Guarda métricas en Storage
    │
    ▼
~06:30 UTC - Orquestación completa
    │
    └── 3 artículos publicados
        9 pins creados
        Logs en Application Insights
```

---

## 8. Setup Paso a Paso

### Semana 1: Infraestructura Base

```bash
# Día 1-2: Azure Setup
- [ ] Crear Resource Group: rg-pinterest-affiliate
- [ ] Crear Azure AI Foundry resource
- [ ] Deployar Claude Sonnet 4.5 desde el catálogo
- [ ] Crear Function App (Flex Consumption, .NET 8)
- [ ] Crear Storage Account para Durable Functions

# Día 3-4: WordPress Setup
- [ ] Comprar dominio
- [ ] Configurar hosting (Hostinger/similar)
- [ ] Instalar WordPress
- [ ] Instalar plugins: Yoast SEO, WP Rocket
- [ ] Crear Application Password para API
- [ ] Crear categorías iniciales

# Día 5-6: Pinterest Setup
- [ ] Crear cuenta Business
- [ ] Verificar dominio
- [ ] Crear 5-10 boards temáticos
- [ ] Obtener API credentials (developer.pinterest.com)
- [ ] Testear API con Postman

# Día 7: Affiliate Setup
- [ ] Aplicar a Booking.com Affiliate Partner
- [ ] Aplicar a GetYourGuide Partner
- [ ] Aplicar a Viator Affiliate
- [ ] Configurar links en affiliates.json
```

### Semana 2: Desarrollo Core

```bash
# Día 8-10: Proyecto Base
- [ ] Crear proyecto .NET 8 Azure Functions
- [ ] Configurar Microsoft Agent Framework
- [ ] Configurar Semantic Kernel con Claude
- [ ] Implementar ResearchTools.cs
- [ ] Implementar ContentTools.cs
- [ ] Tests unitarios

# Día 11-13: Tools de Publicación
- [ ] Implementar WordPressTools.cs
- [ ] Implementar PinterestTools.cs
- [ ] Implementar ImageTools.cs (Bannerbear)
- [ ] Tests de integración

# Día 14: Durable Functions
- [ ] Implementar DailyOrchestrator.cs
- [ ] Implementar Activities.cs
- [ ] Configurar Timer Trigger
- [ ] Test local con Azurite
```

### Semana 3: Integración y Testing

```bash
# Día 15-17: Integración
- [ ] Deploy a Azure
- [ ] Configurar Application Insights
- [ ] Configurar alertas
- [ ] Test end-to-end manual

# Día 18-19: Ajustes
- [ ] Optimizar prompts
- [ ] Ajustar rate limiting Pinterest
- [ ] Verificar affiliate links
- [ ] Mejorar calidad de contenido

# Día 20-21: Go Live
- [ ] Activar Timer Trigger
- [ ] Monitorear primera ejecución
- [ ] Verificar publicaciones
- [ ] Ajustes finales
```

### Semana 4: Optimización

```bash
# Día 22-28: Iteración
- [ ] Analizar métricas de contenido
- [ ] Optimizar keywords
- [ ] A/B test de pin designs
- [ ] Ajustar frecuencia de publicación
- [ ] Documentar learnings
```

---

## 9. Comandos para Claude CLI

```bash
# Crear estructura del proyecto
claude "Crea la estructura de carpetas para el proyecto 
PinterestAffiliateAgent según la sección 2 del plan"

# Implementar ResearchTools
claude "Implementa ResearchTools.cs con las funciones GetTrendingTopics, 
AnalyzeKeyword y GetUnpublishedKeywords usando el patrón de la sección 3.1"

# Implementar ContentTools
claude "Implementa ContentTools.cs según la sección 3.2, incluyendo 
GenerateArticle, GeneratePinCopy e InsertAffiliateLinks"

# Implementar WordPressTools
claude "Implementa WordPressTools.cs usando WordPressPCL para .NET, 
con PublishPost, UploadMediaFromUrl y PostExists según sección 3.4"

# Implementar PinterestTools
claude "Implementa PinterestTools.cs con la Pinterest API v5, 
incluyendo CreatePin, GetBoards y CreatePinVariations según sección 3.5"

# Implementar Orchestrator
claude "Implementa DailyOrchestrator.cs con Durable Functions, 
incluyendo el timer trigger y las orquestaciones según sección 4"

# Configurar Program.cs
claude "Configura Program.cs para registrar Semantic Kernel con 
Claude Sonnet 4.5 en Foundry y todos los tools según sección 5.1"

# Tests
claude "Crea tests unitarios para ResearchTools y ContentTools 
usando xUnit y Moq"
```

---

## 10. Monitoreo y Métricas

### 10.1 KPIs a Trackear

| Métrica | Objetivo Mes 1 | Objetivo Mes 3 | Objetivo Mes 6 |
|---------|----------------|----------------|----------------|
| Artículos/mes | 60-90 | 90 | 90 |
| Pins/mes | 180-270 | 300 | 300 |
| Pageviews/mes | 1,000 | 15,000 | 50,000 |
| Pinterest impressions | 10,000 | 100,000 | 500,000 |
| Ad revenue | $0 | $50 | $200 |
| Affiliate revenue | $0 | $100 | $300 |
| **Total** | **$0** | **$150** | **$500** |

### 10.2 Alertas en Application Insights

```kusto
// Alerta: Orquestación falló
customEvents
| where name == "ArticleOrchestration"
| where customDimensions.Success == "false"
| summarize count() by bin(timestamp, 1h)

// Alerta: Rate limit Pinterest
traces
| where message contains "rate limit"
| summarize count() by bin(timestamp, 1h)

// Métricas diarias
customMetrics
| where name in ("ArticlesPublished", "PinsCreated", "TokensUsed")
| summarize sum(value) by name, bin(timestamp, 1d)
```

---

## 11. Troubleshooting Común

| Problema | Causa Probable | Solución |
|----------|----------------|----------|
| "WordPress 401" | App password expiró | Regenerar en WP |
| "Pinterest 429" | Rate limit | Aumentar delays entre pins |
| "Claude timeout" | Prompt muy largo | Reducir contexto |
| "Durable Task failed" | Storage connection | Verificar connection string |
| "No keywords found" | Trends API cambió | Revisar scraping/API |

---

## 12. Próximos Pasos Inmediatos

### Esta Semana:

1. **Decidir nicho y nombre**
   - [ ] Elegir: Travel Europe / Hidden Gems / Budget Travel
   - [ ] Verificar dominio disponible
   - [ ] Verificar username Pinterest

2. **Setup Azure**
   - [ ] Crear cuenta Azure (si no tenés)
   - [ ] Crear Foundry resource
   - [ ] Deployar Claude Sonnet 4.5

3. **Setup WordPress**
   - [ ] Comprar dominio y hosting
   - [ ] Instalar WordPress básico

4. **Empezar desarrollo**
   - [ ] Crear proyecto .NET
   - [ ] Implementar primer tool (ResearchTools)

---

## Notas Finales

Este plan está diseñado para:
- **Mínimo costo:** ~$10-15/mes
- **Máxima autonomía:** Corre solo una vez configurado
- **Tu stack conocido:** .NET, Azure, Semantic Kernel
- **Escalable:** Fácil agregar más artículos/día

El factor crítico de éxito es la **consistencia**: el sistema necesita 3-6 meses para que SEO y Pinterest maduren.

---

*Plan actualizado para Microsoft Agent Framework + Durable Functions + Claude Sonnet 4.5*
*Generado para uso con Claude CLI*
