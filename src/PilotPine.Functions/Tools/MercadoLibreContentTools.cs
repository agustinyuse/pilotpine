using System.ComponentModel;
using Microsoft.Extensions.Logging;
using PilotPine.Functions.Models;

namespace PilotPine.Functions.Tools;

/// <summary>
/// Tools de contenido de productos ML expuestos al agente (Claude).
///
/// Se registran via AIFunctionFactory.Create() y se pasan
/// al AIAgent ProductWriter. El agente decide cómo generar
/// el contenido y llama a CreateProductPost para empaquetarlo.
/// </summary>
public class MercadoLibreContentTools
{
    private readonly ILogger<MercadoLibreContentTools> _logger;

    public MercadoLibreContentTools(ILogger<MercadoLibreContentTools> logger)
    {
        _logger = logger;
    }

    [Description("Structures a product recommendation post with product links. Call this after generating the post content to save the final result.")]
    public Task<ProductPost> CreateProductPost(
        [Description("Search keyword used to find these products")] string keyword,
        [Description("Post title, catchy and including key product category")] string title,
        [Description("Full post content with product recommendations. Use [PRODUCT_LINK:ITEM_ID] placeholders for product links.")] string content,
        [Description("Comma-separated list of ML product IDs included in the post (e.g. MLA123,MLA456)")] string productIds,
        [Description("Short meta description, max 155 characters")] string metaDescription)
    {
        var ids = productIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Reemplazar placeholders [PRODUCT_LINK:ID] con los permalinks reales
        // Los permalinks se resolverán después en el orchestrator
        var processedContent = content;
        foreach (var id in ids)
        {
            var placeholder = $"[PRODUCT_LINK:{id}]";
            // Dejar el placeholder por ahora — el orchestrator lo reemplaza con el permalink real
            if (!processedContent.Contains(placeholder))
            {
                _logger.LogWarning("Product placeholder not found in content: {Placeholder}", placeholder);
            }
        }

        var post = new ProductPost
        {
            Title = title,
            Content = processedContent,
            MetaDescription = metaDescription.Length > 155
                ? metaDescription[..155]
                : metaDescription,
            Tags = BuildTags(keyword)
        };

        _logger.LogInformation("Product post structured: {Title} ({ProductCount} products)",
            title, ids.Length);

        return Task.FromResult(post);
    }

    private static string[] BuildTags(string keyword)
    {
        var baseTags = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. baseTags, "mercadolibre", "ofertas", "argentina"];
    }
}
