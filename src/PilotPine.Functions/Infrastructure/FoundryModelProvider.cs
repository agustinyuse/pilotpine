using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

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
/// Ahora usa AzureOpenAIClient (compatible con Foundry) en lugar de
/// Azure.AI.Inference, y crea AIAgent del Microsoft Agent Framework.
/// </summary>
public class FoundryModelProvider
{
    private readonly AzureOpenAIClient _client;
    private readonly string _defaultModelId;

    public FoundryModelProvider(string endpoint, string defaultModelId, string? apiKey = null)
    {
        _defaultModelId = defaultModelId;

        _client = !string.IsNullOrEmpty(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            : new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential());
    }

    public string DefaultModelId => _defaultModelId;
    public AzureOpenAIClient Client => _client;

    /// <summary>
    /// Obtiene un ChatClient para un modelo específico de Foundry.
    ///
    /// Ejemplo:
    ///   var chat = provider.GetChatClient("gpt-4o");
    ///   var chat = provider.GetChatClient("claude-sonnet-4-5");
    /// </summary>
    public ChatClient GetChatClient(string? modelId = null)
    {
        return _client.GetChatClient(modelId ?? _defaultModelId);
    }

    /// <summary>
    /// Crea un AIAgent del Agent Framework sin tools.
    /// Útil para agentes simples que solo necesitan instrucciones.
    /// </summary>
    public AIAgent CreateAgent(string name, string instructions, string? modelId = null)
    {
        return GetChatClient(modelId).AsAIAgent(instructions, name);
    }

    /// <summary>
    /// Crea un AIAgent del Agent Framework con tools.
    /// Útil para agentes que necesitan llamar funciones.
    /// </summary>
    public AIAgent CreateAgentWithTools(
        string name,
        string instructions,
        IServiceProvider services,
        AIFunction[] tools,
        string? modelId = null)
    {
        return GetChatClient(modelId).AsAIAgent(
            instructions: instructions,
            name: name,
            services: services,
            tools: tools);
    }
}
