namespace PilotPine.Functions.Models;

/// <summary>
/// Input para iniciar la orquestación diaria.
/// </summary>
public record PipelineInput
{
    public int ArticleCount { get; init; } = 3;
    public DateTime Date { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Resultado general de la ejecución diaria.
/// </summary>
public record PipelineResult
{
    public DateTime Date { get; init; }
    public int ArticlesPublished { get; init; }
    public int TotalPinsCreated { get; init; }
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Input para procesar un artículo individual.
/// </summary>
public record ArticleInput
{
    public required string Keyword { get; init; }
    public string ArticleType { get; init; } = "guide";
}

/// <summary>
/// Resultado del procesamiento de un artículo.
/// </summary>
public record ArticleResult
{
    public bool Success { get; init; }
    public string Keyword { get; init; } = "";
    public string? PostUrl { get; init; }
    public int PinsCreated { get; init; }
    public string? Error { get; init; }
}
