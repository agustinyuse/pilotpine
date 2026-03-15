namespace PilotPine.Functions.Models;

/// <summary>
/// Keyword encontrada en la fase de research.
/// </summary>
public record KeywordResult
{
    public required string Keyword { get; init; }
    public string ArticleType { get; init; } = "guide";
    public string SearchVolume { get; init; } = "unknown";
    public string Competition { get; init; } = "unknown";
}

/// <summary>
/// Artículo generado listo para publicar.
/// </summary>
public record Article
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string MetaDescription { get; init; } = "";
    public string Category { get; init; } = "travel";
    public string[] Tags { get; init; } = [];
}

/// <summary>
/// Resultado de publicar en WordPress.
/// </summary>
public record PublishResult
{
    public bool Success { get; init; }
    public string? PostUrl { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Resultado de crear un pin en Pinterest.
/// </summary>
public record PinResult
{
    public bool Success { get; init; }
    public string? PinId { get; init; }
    public string? Error { get; init; }
}

// ─── Mercado Libre Models ────────────────────────────────────────

/// <summary>
/// Producto de Mercado Libre obtenido via API pública.
/// </summary>
public record MercadoLibreProduct
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public decimal Price { get; init; }
    public decimal? OriginalPrice { get; init; }
    public string CurrencyId { get; init; } = "ARS";
    public required string Permalink { get; init; }
    public string Thumbnail { get; init; } = "";
    public string Condition { get; init; } = "new";
    public int SoldQuantity { get; init; }
    public bool FreeShipping { get; init; }
    public string SellerName { get; init; } = "";
}

/// <summary>
/// Post de producto generado por Claude, listo para compartir.
/// </summary>
public record ProductPost
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string MetaDescription { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public List<MercadoLibreProduct> Products { get; init; } = [];
    /// <summary>
    /// Lista de permalinks para convertir en el Generador de links de ML.
    /// </summary>
    public List<string> PermalinksToConvert { get; init; } = [];
}
