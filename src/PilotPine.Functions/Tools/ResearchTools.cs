using System.ComponentModel;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Infrastructure;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Tools de investigación de keywords.
///
/// Keywords de viaje: Lista estática (seed) + futuros trends APIs.
/// Keywords de productos: ML Trends API (/trends/MLA) dinámico.
///
/// GetKeywords se llama directamente desde el orchestrator (mecánico).
/// MarkAsPublished se puede exponer como tool si se necesita.
/// </summary>
public class ResearchTools
{
    private readonly StateManager _stateManager;
    private readonly MercadoLibreTools _mlTools;
    private readonly ILogger<ResearchTools> _logger;

    public ResearchTools(StateManager stateManager, MercadoLibreTools mlTools, ILogger<ResearchTools> logger)
    {
        _stateManager = stateManager;
        _mlTools = mlTools;
        _logger = logger;
    }

    [Description("Gets travel keywords for content creation. Returns keywords that haven't been published yet.")]
    public async Task<List<KeywordResult>> GetKeywords(
        [Description("Number of keywords to return")] int count = 3)
    {
        // Cargar keywords ya publicadas
        var published = await _stateManager.LoadAsync<HashSet<string>>("published-keywords")
            ?? [];

        // Fase 1: Lista estática (reemplazar con API en Fase 2)
        var allKeywords = GetSeedKeywords();

        var available = allKeywords
            .Where(k => !published.Contains(k.Keyword.ToLowerInvariant()))
            .Take(count)
            .ToList();

        _logger.LogInformation(
            "Keywords: {Available} available, {Published} already published",
            available.Count,
            published.Count
        );

        return available;
    }

    [Description("Marks a keyword as published to avoid duplicates in future runs.")]
    public async Task MarkAsPublished(
        [Description("The keyword that was published")] string keyword)
    {
        await _stateManager.UpdateAsync(
            "published-keywords",
            new HashSet<string>(),
            existing =>
            {
                existing.Add(keyword.ToLowerInvariant());
                return existing;
            }
        );

        _logger.LogInformation("Marked as published: {Keyword}", keyword);
    }

    /// <summary>
    /// Obtiene keywords trending de ML para product posts.
    /// Filtra los ya publicados para evitar duplicados.
    /// </summary>
    [Description("Gets trending product keywords from Mercado Libre for product post generation.")]
    public async Task<List<string>> GetProductKeywords(
        [Description("Number of keywords to return")] int count = 2)
    {
        var published = await _stateManager.LoadAsync<HashSet<string>>("published-product-keywords")
            ?? [];

        try
        {
            var trending = await _mlTools.GetTrendingKeywordsAsync(count + 5);

            var available = trending
                .Where(k => !published.Contains(k.ToLowerInvariant()))
                .Take(count)
                .ToList();

            _logger.LogInformation(
                "Product keywords: {Available} available from {Total} trending, {Published} already published",
                available.Count, trending.Count, published.Count);

            return available;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trending product keywords, using fallback");
            // Fallback: keywords estáticos de productos
            var fallback = GetFallbackProductKeywords();
            return fallback
                .Where(k => !published.Contains(k.ToLowerInvariant()))
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Marca un keyword de producto como publicado.
    /// </summary>
    public async Task MarkProductKeywordAsPublished(string keyword)
    {
        await _stateManager.UpdateAsync(
            "published-product-keywords",
            new HashSet<string>(),
            existing =>
            {
                existing.Add(keyword.ToLowerInvariant());
                return existing;
            }
        );

        _logger.LogInformation("Product keyword marked as published: {Keyword}", keyword);
    }

    /// <summary>
    /// Keywords semilla para artículos de viaje.
    /// </summary>
    private static List<KeywordResult> GetSeedKeywords() =>
    [
        new() { Keyword = "hidden beaches portugal", ArticleType = "listicle", SearchVolume = "medium", Competition = "low" },
        new() { Keyword = "budget travel europe 2025", ArticleType = "guide", SearchVolume = "high", Competition = "medium" },
        new() { Keyword = "best hiking trails switzerland", ArticleType = "listicle", SearchVolume = "medium", Competition = "medium" },
        new() { Keyword = "romantic hotels paris", ArticleType = "listicle", SearchVolume = "high", Competition = "high" },
        new() { Keyword = "greek islands guide", ArticleType = "guide", SearchVolume = "high", Competition = "medium" },
        new() { Keyword = "amsterdam travel tips", ArticleType = "guide", SearchVolume = "medium", Competition = "low" },
        new() { Keyword = "best hostels barcelona", ArticleType = "listicle", SearchVolume = "medium", Competition = "low" },
        new() { Keyword = "iceland road trip", ArticleType = "guide", SearchVolume = "high", Competition = "medium" },
        new() { Keyword = "italian coastal towns", ArticleType = "listicle", SearchVolume = "medium", Competition = "low" },
        new() { Keyword = "scotland castles visit", ArticleType = "listicle", SearchVolume = "low", Competition = "low" },
    ];

    /// <summary>
    /// Fallback de keywords de productos si ML trends falla.
    /// </summary>
    private static List<string> GetFallbackProductKeywords() =>
    [
        "auriculares bluetooth",
        "zapatillas running",
        "smartwatch",
        "cargador inalámbrico",
        "mochila notebook",
        "parlante portátil",
        "mouse gamer",
        "silla ergonómica",
    ];
}
