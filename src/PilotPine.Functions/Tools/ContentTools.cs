using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Tools de contenido expuestos al agente (Claude/GPT).
///
/// Estas funciones se registran via AIFunctionFactory.Create() y se pasan
/// al AIAgent del Agent Framework. El agente decide cuándo y cómo usarlas.
///
/// Flujo típico:
///   1. El agente genera el contenido del artículo
///   2. Llama a CreateArticleStructure para empaquetarlo
///   3. Los affiliate links se insertan automáticamente
/// </summary>
public class ContentTools
{
    private readonly IConfiguration _config;
    private readonly ILogger<ContentTools> _logger;

    public ContentTools(IConfiguration config, ILogger<ContentTools> logger)
    {
        _config = config;
        _logger = logger;
    }

    [Description("Structures a complete blog article with affiliate link placeholders replaced. Call this after generating content to create the final article.")]
    public Task<Article> CreateArticleStructure(
        [Description("Main keyword for the article")] string keyword,
        [Description("Type: listicle or guide")] string articleType,
        [Description("Generated article title")] string title,
        [Description("Full HTML content of the article")] string content,
        [Description("Meta description, max 155 characters")] string metaDescription)
    {
        // Reemplazar placeholders de affiliate con links reales
        var processedContent = ReplaceAffiliatePlaceholders(content, keyword);

        var article = new Article
        {
            Title = title,
            Content = processedContent,
            MetaDescription = metaDescription.Length > 155
                ? metaDescription[..155]
                : metaDescription,
            Category = "travel",
            Tags = BuildTags(keyword)
        };

        _logger.LogInformation("Article structured: {Title} ({WordCount} chars)", title, content.Length);
        return Task.FromResult(article);
    }

    [Description("Generates headline variations for Pinterest pins based on the article title.")]
    public Task<List<string>> GeneratePinHeadlines(
        [Description("The article title")] string articleTitle,
        [Description("Number of variations to generate")] int count = 3)
    {
        var headlines = new List<string>
        {
            articleTitle,
            $"Must See: {articleTitle}",
            $"Your Complete Guide: {articleTitle}",
            $"Top Picks: {articleTitle}"
        };

        return Task.FromResult(headlines.Take(count).ToList());
    }

    /// <summary>
    /// Reemplaza los placeholders de affiliate links con URLs reales.
    /// Soporta: [HOTEL_LINK], [TOUR_LINK], [AFFILIATE:booking], [AFFILIATE:tours]
    /// </summary>
    private string ReplaceAffiliatePlaceholders(string content, string keyword)
    {
        var searchTerm = Uri.EscapeDataString(keyword);

        var bookingId = _config["Affiliates:BookingId"] ?? "";
        var gygId = _config["Affiliates:GetYourGuideId"] ?? "";

        var bookingUrl = string.IsNullOrEmpty(bookingId)
            ? $"https://www.booking.com/searchresults.html?ss={searchTerm}"
            : $"https://www.booking.com/searchresults.html?ss={searchTerm}&aid={bookingId}";

        var gygUrl = string.IsNullOrEmpty(gygId)
            ? $"https://www.getyourguide.com/s/?q={searchTerm}"
            : $"https://www.getyourguide.com/s/?q={searchTerm}&partner_id={gygId}";

        return content
            .Replace("[HOTEL_LINK]", bookingUrl)
            .Replace("[TOUR_LINK]", gygUrl)
            .Replace("[AFFILIATE:booking]", bookingUrl)
            .Replace("[AFFILIATE:tours]", gygUrl);
    }

    private static string[] BuildTags(string keyword)
    {
        var baseTags = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. baseTags, "travel", "europe"];
    }
}
