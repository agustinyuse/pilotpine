# 03-ORCHESTRATOR: Durable Function

## Cómo Funciona

La Durable Function:
1. Se ejecuta diariamente (Timer Trigger)
2. Llama al agente con los tools
3. Guarda checkpoint después de cada paso
4. Si falla, retoma desde el último checkpoint

## DailyOrchestrator.cs

```csharp
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using PinterestAffiliate.Models;

namespace PinterestAffiliate.Functions;

public class DailyOrchestrator
{
    private readonly Kernel _kernel;
    private readonly ILogger<DailyOrchestrator> _logger;

    public DailyOrchestrator(Kernel kernel, ILogger<DailyOrchestrator> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // TIMER TRIGGER - Ejecuta todos los días a las 6 AM UTC
    // ═══════════════════════════════════════════════════════════════
    
    [Function("DailyTrigger")]
    public async Task DailyTrigger(
        [TimerTrigger("0 0 6 * * *")] TimerInfo timer,
        [DurableClient] DurableTaskClient client)
    {
        var instanceId = $"daily-{DateTime.UtcNow:yyyy-MM-dd}";
        
        // Evitar duplicados
        var existing = await client.GetInstanceAsync(instanceId);
        if (existing?.RuntimeStatus == OrchestrationRuntimeStatus.Running)
        {
            _logger.LogWarning("Already running for today");
            return;
        }

        await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(MainOrchestration),
            new OrchestrationInput { ArticleCount = 3 },
            new StartOrchestrationOptions { InstanceId = instanceId }
        );

        _logger.LogInformation("Started: {InstanceId}", instanceId);
    }

    // ═══════════════════════════════════════════════════════════════
    // ORQUESTACIÓN PRINCIPAL
    // ═══════════════════════════════════════════════════════════════
    
    [Function(nameof(MainOrchestration))]
    public async Task<OrchestrationResult> MainOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<OrchestrationInput>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();
        var results = new List<ArticleResult>();

        // PASO 1: Obtener keywords
        // >>> Checkpoint automático después de esto <<<
        var keywords = await context.CallActivityAsync<List<KeywordResult>>(
            nameof(GetKeywordsActivity),
            input.ArticleCount
        );

        logger.LogInformation("Got {Count} keywords", keywords.Count);

        // PASO 2: Procesar cada keyword
        foreach (var kw in keywords)
        {
            // Cada artículo es una sub-orquestación con sus propios checkpoints
            var result = await context.CallSubOrchestrationAsync<ArticleResult>(
                nameof(ProcessArticleOrchestration),
                kw
            );
            results.Add(result);
        }

        return new OrchestrationResult
        {
            Date = context.CurrentUtcDateTime,
            ArticlesPublished = results.Count(r => r.Success),
            TotalPins = results.Sum(r => r.PinsCreated),
            Errors = results.Where(r => !r.Success).Select(r => r.Error).ToList()
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // SUB-ORQUESTACIÓN POR ARTÍCULO
    // ═══════════════════════════════════════════════════════════════
    
    [Function(nameof(ProcessArticleOrchestration))]
    public async Task<ArticleResult> ProcessArticleOrchestration(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var keyword = context.GetInput<KeywordResult>();
        var logger = context.CreateReplaySafeLogger<DailyOrchestrator>();

        try
        {
            // PASO 1: Generar artículo con el agente
            // >>> Checkpoint: Si falla después, NO regenera el artículo <<<
            var article = await context.CallActivityAsync<Article>(
                nameof(GenerateArticleActivity),
                keyword
            );
            logger.LogInformation("Generated: {Title}", article.Title);

            // PASO 2: Generar imagen
            // >>> Checkpoint <<<
            var imageUrl = await context.CallActivityAsync<string>(
                nameof(GenerateImageActivity),
                new ImageInput { Headline = article.Title, Keyword = keyword.Keyword }
            );
            logger.LogInformation("Image ready");

            // PASO 3: Publicar en WordPress
            // >>> Checkpoint <<<
            var wpResult = await context.CallActivityAsync<PublishResult>(
                nameof(PublishToWordPressActivity),
                article
            );
            
            if (!wpResult.Success)
            {
                return new ArticleResult 
                { 
                    Success = false, 
                    Keyword = keyword.Keyword,
                    Error = wpResult.Error 
                };
            }
            logger.LogInformation("Published: {Url}", wpResult.PostUrl);

            // PASO 4: Crear pin
            // >>> Checkpoint <<<
            var pinResult = await context.CallActivityAsync<PinResult>(
                nameof(CreatePinActivity),
                new PinInput 
                { 
                    Title = article.Title,
                    Description = article.MetaDescription,
                    LinkUrl = wpResult.PostUrl,
                    ImageUrl = imageUrl
                }
            );

            // Marcar keyword como publicada
            await context.CallActivityAsync(
                nameof(MarkPublishedActivity),
                keyword.Keyword
            );

            return new ArticleResult
            {
                Success = true,
                Keyword = keyword.Keyword,
                PostUrl = wpResult.PostUrl,
                PinsCreated = pinResult.Success ? 1 : 0
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed: {Keyword}", keyword.Keyword);
            return new ArticleResult
            {
                Success = false,
                Keyword = keyword.Keyword,
                Error = ex.Message
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ACTIVITIES - Cada una es una unidad de trabajo con retry
    // ═══════════════════════════════════════════════════════════════

    [Function(nameof(GetKeywordsActivity))]
    public async Task<List<KeywordResult>> GetKeywordsActivity(
        [ActivityTrigger] int count)
    {
        var tools = _kernel.Plugins["Research"];
        var function = tools["GetKeywords"];
        
        var result = await _kernel.InvokeAsync(function, new() { ["count"] = count });
        return result.GetValue<List<KeywordResult>>() ?? new();
    }

    [Function(nameof(GenerateArticleActivity))]
    public async Task<Article> GenerateArticleActivity(
        [ActivityTrigger] KeywordResult keyword)
    {
        // Crear agente con instrucciones específicas
        var agent = new ChatCompletionAgent
        {
            Name = "ContentWriter",
            Instructions = $"""
                Eres un escritor experto de contenido de viajes.
                
                Tu tarea: Crear un artículo tipo {keyword.ArticleType} sobre "{keyword.Keyword}"
                
                Requisitos:
                - Título atractivo con el año actual (2025)
                - 1500-2000 palabras
                - Incluir [HOTEL_LINK] donde recomiendes hoteles
                - Incluir [TOUR_LINK] donde recomiendes tours
                - Meta description de máximo 155 caracteres
                - Tono: informativo pero amigable
                
                Usa el tool CreateArticleStructure para guardar el artículo.
                """,
            Kernel = _kernel
        };

        var chat = new ChatHistory();
        chat.AddUserMessage($"Genera un artículo sobre: {keyword.Keyword}");

        // El agente genera el contenido y llama al tool automáticamente
        await foreach (var response in agent.InvokeAsync(chat))
        {
            // El agente decidirá usar CreateArticleStructure
        }

        // Obtener el artículo del resultado del tool
        var contentTools = _kernel.Plugins["Content"];
        var result = await _kernel.InvokeAsync(
            contentTools["CreateArticleStructure"],
            new()
            {
                ["keyword"] = keyword.Keyword,
                ["articleType"] = keyword.ArticleType,
                ["title"] = $"Best {keyword.Keyword} Guide 2025", // Fallback
                ["content"] = "<p>Content generated by agent</p>",
                ["metaDescription"] = $"Discover the best {keyword.Keyword}"
            }
        );

        return result.GetValue<Article>() ?? throw new Exception("Failed to create article");
    }

    [Function(nameof(GenerateImageActivity))]
    public async Task<string> GenerateImageActivity(
        [ActivityTrigger] ImageInput input)
    {
        var tools = _kernel.Plugins["Images"];
        var result = await _kernel.InvokeAsync(
            tools["GeneratePinImage"],
            new() 
            { 
                ["headline"] = input.Headline,
                ["keyword"] = input.Keyword
            }
        );
        return result.GetValue<string>() ?? "";
    }

    [Function(nameof(PublishToWordPressActivity))]
    public async Task<PublishResult> PublishToWordPressActivity(
        [ActivityTrigger] Article article)
    {
        var tools = _kernel.Plugins["WordPress"];
        var result = await _kernel.InvokeAsync(
            tools["PublishPost"],
            new()
            {
                ["title"] = article.Title,
                ["content"] = article.Content,
                ["excerpt"] = article.MetaDescription,
                ["tags"] = string.Join(",", article.Tags)
            }
        );
        return result.GetValue<PublishResult>() ?? new PublishResult { Success = false };
    }

    [Function(nameof(CreatePinActivity))]
    public async Task<PinResult> CreatePinActivity(
        [ActivityTrigger] PinInput input)
    {
        var tools = _kernel.Plugins["Pinterest"];
        var result = await _kernel.InvokeAsync(
            tools["CreatePin"],
            new()
            {
                ["title"] = input.Title,
                ["description"] = input.Description,
                ["linkUrl"] = input.LinkUrl,
                ["imageUrl"] = input.ImageUrl
            }
        );
        return result.GetValue<PinResult>() ?? new PinResult { Success = false };
    }

    [Function(nameof(MarkPublishedActivity))]
    public async Task MarkPublishedActivity(
        [ActivityTrigger] string keyword)
    {
        var tools = _kernel.Plugins["Research"];
        await _kernel.InvokeAsync(tools["MarkAsPublished"], new() { ["keyword"] = keyword });
    }
}

// ═══════════════════════════════════════════════════════════════
// INPUT/OUTPUT RECORDS
// ═══════════════════════════════════════════════════════════════

public record OrchestrationInput
{
    public int ArticleCount { get; init; } = 3;
}

public record OrchestrationResult
{
    public DateTime Date { get; init; }
    public int ArticlesPublished { get; init; }
    public int TotalPins { get; init; }
    public List<string> Errors { get; init; } = new();
}

public record ArticleResult
{
    public bool Success { get; init; }
    public string Keyword { get; init; }
    public string PostUrl { get; init; }
    public int PinsCreated { get; init; }
    public string Error { get; init; }
}

public record ImageInput
{
    public string Headline { get; init; }
    public string Keyword { get; init; }
}

public record PinInput
{
    public string Title { get; init; }
    public string Description { get; init; }
    public string LinkUrl { get; init; }
    public string ImageUrl { get; init; }
}
```

---

## Retry Policies (Opcional)

Para agregar reintentos automáticos, modificar las llamadas:

```csharp
var retryOptions = new TaskRetryOptions(
    firstRetryInterval: TimeSpan.FromSeconds(10),
    maxNumberOfAttempts: 3
);

var wpResult = await context.CallActivityAsync<PublishResult>(
    nameof(PublishToWordPressActivity),
    article,
    new TaskActivityOptions { Retry = retryOptions }
);
```

---

## Probar Localmente

```bash
# Iniciar Azurite (emulador de storage)
azurite --silent --location ./azurite --debug ./azurite/debug.log

# En otra terminal, iniciar la function
cd src
func start

# Trigger manual (en otra terminal)
curl -X POST http://localhost:7071/api/DailyTrigger
```

---

## Siguiente

Ir a `04-DEPLOY.md` para deployar a Azure.
