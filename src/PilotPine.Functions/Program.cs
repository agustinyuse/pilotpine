using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AzureFunctions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Tools;

// ─── Configuration ───────────────────────────────────────────────
var builder = FunctionsApplication.CreateBuilder(args);
var config = builder.Configuration;

var foundryEndpoint = config["Foundry:Endpoint"]
    ?? throw new InvalidOperationException("Foundry:Endpoint is required");
var defaultModel = config["Foundry:DefaultModel"] ?? "claude-sonnet-4-5";
var apiKey = config["Foundry:ApiKey"]; // Optional: null = DefaultAzureCredential

// ─── Multi-Model Foundry Provider ────────────────────────────────
// Permite usar diferentes modelos (Claude, GPT, Mistral, etc.)
// a través de Azure AI Foundry con un solo endpoint.
var foundryProvider = new FoundryModelProvider(foundryEndpoint, defaultModel, apiKey);

builder.Services.AddSingleton(foundryProvider);

// ─── State Manager (persistencia en archivo) ─────────────────────
builder.Services.AddSingleton<StateManager>(sp =>
{
    var storagePath = config["State:StoragePath"] ?? "./state";
    var logger = sp.GetRequiredService<ILogger<StateManager>>();
    return new StateManager(storagePath, logger);
});

// ─── HTTP Client ─────────────────────────────────────────────────
builder.Services.AddHttpClient();

// ─── In-Memory Cache (para cachear posts generados 30 min) ───────
builder.Services.AddMemoryCache();

// ─── Tools: Agent-facing (expuestos al LLM via Agent Framework) ──
builder.Services.AddSingleton<ResearchTools>();
builder.Services.AddSingleton<ContentTools>();

// ─── Tools: Agent-facing (ML products) ───────────────────────────
builder.Services.AddSingleton<MercadoLibreContentTools>();

// ─── Tools: Direct-call (llamados desde el orchestrator) ─────────
// Estos NO se registran como tools del agente.
// Son llamadas directas porque publicar es mecánico.
builder.Services.AddSingleton<WordPressTools>();
builder.Services.AddSingleton<PinterestTools>();
builder.Services.AddSingleton<ImageTools>();
builder.Services.AddSingleton<MercadoLibreTools>();

// ─── Observability ───────────────────────────────────────────────
builder.Services.AddApplicationInsightsTelemetryWorkerService();

// ─── Agent Framework: Durable Agents ─────────────────────────────
// Registra agentes con el framework. Cada agente tiene nombre,
// instrucciones, modelo y (opcionalmente) tools.
const string ContentWriterName = "ContentWriter";
const string ContentWriterInstructions =
    """
    You are an expert travel content writer.

    Requirements for every article you write:
    - Catchy title including the current year
    - 1500-2000 words in HTML format
    - Use [HOTEL_LINK] where you recommend hotels
    - Use [TOUR_LINK] where you recommend tours/activities
    - Meta description of max 155 characters
    - Informative but friendly tone
    - Include practical tips and insider knowledge

    IMPORTANT: After generating the content, you MUST call the CreateArticleStructure
    tool to save the article with all fields filled in. This is required to complete
    the task. Never return content without calling the tool.
    """;

// ─── ProductWriter Agent ─────────────────────────────────────────
const string ProductWriterName = "ProductWriter";
const string ProductWriterInstructions =
    """
    Sos un experto en recomendaciones de productos para el mercado argentino.
    Escribís posts cortos para redes sociales en español argentino (100-300 palabras).

    Formato para cada producto:
    - Emoji llamativo + título del producto
    - 2-3 características clave como bullet points (✅)
    - Precio con porcentaje de descuento si aplica
    - Badge de envío gratis si está disponible
    - Usá [PRODUCT_LINK:ITEM_ID] como placeholder del link (reemplazá ITEM_ID con el ID real del producto)

    Tono: casual, directo, persuasivo. Usá español argentino (vos, comprá, mirá, etc.)

    IMPORTANTE: Después de generar el contenido, DEBÉS llamar al tool CreateProductPost
    para guardar el post con todos los campos completos. Nunca devuelvas contenido
    sin llamar al tool.
    """;

builder
    .ConfigureFunctionsWebApplication()
    .ConfigureDurableAgents(options =>
    {
        // ContentWriter: agente con tools para generar artículos de viaje.
        options.AddAIAgentFactory(ContentWriterName, sp =>
        {
            var contentTools = sp.GetRequiredService<ContentTools>();

            return foundryProvider.GetChatClient().AsAIAgent(
                instructions: ContentWriterInstructions,
                name: ContentWriterName,
                services: sp,
                tools: [
                    AIFunctionFactory.Create(contentTools.CreateArticleStructure),
                    AIFunctionFactory.Create(contentTools.GeneratePinHeadlines),
                ]);
        });

        // ProductWriter: agente para posts de productos ML.
        options.AddAIAgentFactory(ProductWriterName, sp =>
        {
            var mlContentTools = sp.GetRequiredService<MercadoLibreContentTools>();

            return foundryProvider.GetChatClient().AsAIAgent(
                instructions: ProductWriterInstructions,
                name: ProductWriterName,
                services: sp,
                tools: [
                    AIFunctionFactory.Create(mlContentTools.CreateProductPost),
                ]);
        });
    });

using var app = builder.Build();
app.Run();
