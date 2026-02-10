using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Azure.Identity;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Tools;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // ─── Multi-Model Foundry Provider ───────────────────────────
        // Permite usar diferentes modelos (Claude, GPT, Mistral, etc.)
        // a través de Azure AI Foundry con un solo endpoint.
        services.AddSingleton<FoundryModelProvider>(sp =>
        {
            var endpoint = config["Foundry:Endpoint"]
                ?? throw new InvalidOperationException("Foundry:Endpoint is required");
            var defaultModel = config["Foundry:DefaultModel"] ?? "claude-sonnet-4-5";

            return new FoundryModelProvider(endpoint, defaultModel);
        });

        // ─── Semantic Kernel (modelo por defecto) ───────────────────
        services.AddKernel();
        services.AddAzureAIInferenceChatCompletion(
            endpoint: new Uri(config["Foundry:Endpoint"]!),
            credential: new DefaultAzureCredential(),
            modelId: config["Foundry:DefaultModel"] ?? "claude-sonnet-4-5"
        );

        // ─── State Manager (persistencia en archivo) ────────────────
        // Guarda el estado en archivos JSON locales para no perder
        // contexto si alguna tool da error. Económico y simple.
        services.AddSingleton<StateManager>(sp =>
        {
            var storagePath = config["State:StoragePath"] ?? "./state";
            var logger = sp.GetRequiredService<ILogger<StateManager>>();
            return new StateManager(storagePath, logger);
        });

        // ─── Tools ──────────────────────────────────────────────────
        services.AddSingleton<ResearchTools>();
        // Más tools se agregan aquí a medida que se implementen:
        // services.AddSingleton<ContentTools>();
        // services.AddSingleton<ImageTools>();
        // services.AddSingleton<WordPressTools>();
        // services.AddSingleton<PinterestTools>();

        // ─── HTTP Client ────────────────────────────────────────────
        services.AddHttpClient();

        // ─── Observability ──────────────────────────────────────────
        services.AddApplicationInsightsTelemetryWorkerService();
    })
    .Build();

await host.RunAsync();
