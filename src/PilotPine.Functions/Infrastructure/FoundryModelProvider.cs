using Azure.AI.Inference;
using Azure.Identity;
using Microsoft.SemanticKernel;

namespace PilotPine.Functions.Infrastructure;

/// <summary>
/// Proveedor multi-modelo para Azure AI Foundry.
///
/// Foundry expone un único endpoint que rutea a diferentes modelos:
///   - claude-sonnet-4-5 (Anthropic via Foundry)
///   - gpt-4o (OpenAI via Foundry)
///   - mistral-large (Mistral via Foundry)
///   - etc.
///
/// Esto permite cambiar de modelo por tarea sin cambiar la infraestructura.
/// Por ejemplo: Claude para contenido creativo, GPT para structured output.
/// </summary>
public class FoundryModelProvider
{
    private readonly string _endpoint;
    private readonly string _defaultModelId;
    private readonly DefaultAzureCredential _credential;

    public FoundryModelProvider(string endpoint, string defaultModelId)
    {
        _endpoint = endpoint;
        _defaultModelId = defaultModelId;
        _credential = new DefaultAzureCredential();
    }

    public string Endpoint => _endpoint;
    public string DefaultModelId => _defaultModelId;

    /// <summary>
    /// Crea un Kernel de Semantic Kernel configurado para un modelo específico.
    /// Útil cuando una activity necesita un modelo diferente al default.
    ///
    /// Ejemplo:
    ///   var kernel = provider.CreateKernelForModel("gpt-4o");
    ///   // Usar GPT-4o para structured output (JSON mode)
    ///
    ///   var kernel = provider.CreateKernelForModel("claude-sonnet-4-5");
    ///   // Usar Claude para contenido creativo largo
    /// </summary>
    public Kernel CreateKernelForModel(string? modelId = null)
    {
        var builder = Kernel.CreateBuilder();

        builder.AddAzureAIInferenceChatCompletion(
            endpoint: new Uri(_endpoint),
            credential: _credential,
            modelId: modelId ?? _defaultModelId
        );

        return builder.Build();
    }

    /// <summary>
    /// Crea un ChatCompletionsClient directo para Azure AI Inference.
    /// Útil para llamadas directas sin Semantic Kernel (approach tradicional).
    ///
    /// Ver docs/TOOLS-VS-DIRECT-CALLS.md para cuándo usar cada approach.
    /// </summary>
    public ChatCompletionsClient CreateDirectClient()
    {
        return new ChatCompletionsClient(
            new Uri(_endpoint),
            _credential
        );
    }
}
