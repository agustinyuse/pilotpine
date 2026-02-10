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
