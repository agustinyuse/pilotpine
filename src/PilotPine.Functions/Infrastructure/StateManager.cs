using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PilotPine.Functions.Infrastructure;

/// <summary>
/// Persiste estado en archivos JSON locales.
///
/// ¿Por qué archivos en vez de base de datos?
/// - En Azure Functions con Durable Tasks, el storage ya maneja checkpoints.
/// - Los archivos son para estado de aplicación (keywords publicadas, historial).
/// - En producción esto se puede migrar a Azure Blob Storage sin cambiar la interfaz.
/// - Para desarrollo local es perfecto: simple, debuggeable, sin dependencias.
///
/// El StateManager guarda un archivo por "dominio" de estado:
///   state/published-keywords.json
///   state/daily-results/2025-01-15.json
///   state/failed-retries.json
/// </summary>
public class StateManager
{
    private readonly string _basePath;
    private readonly ILogger<StateManager> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StateManager(string basePath, ILogger<StateManager> logger)
    {
        _basePath = basePath;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    /// <summary>
    /// Guarda estado de forma atómica (write-then-rename para evitar corrupción).
    /// </summary>
    public async Task SaveAsync<T>(string key, T data)
    {
        var filePath = GetFilePath(key);
        var directory = Path.GetDirectoryName(filePath);
        if (directory != null)
            Directory.CreateDirectory(directory);

        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(data, JsonOptions);

        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);

        _logger.LogDebug("State saved: {Key}", key);
    }

    /// <summary>
    /// Carga estado. Retorna default(T) si no existe.
    /// </summary>
    public async Task<T?> LoadAsync<T>(string key)
    {
        var filePath = GetFilePath(key);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("State not found: {Key}", key);
            return default;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// Carga estado o crea uno nuevo con el factory si no existe.
    /// </summary>
    public async Task<T> LoadOrCreateAsync<T>(string key, Func<T> factory)
    {
        var existing = await LoadAsync<T>(key);
        if (existing != null)
            return existing;

        var newState = factory();
        await SaveAsync(key, newState);
        return newState;
    }

    /// <summary>
    /// Actualiza estado de forma atómica: lee, aplica transform, guarda.
    /// Si el archivo no existe, empieza con el defaultValue.
    /// </summary>
    public async Task<T> UpdateAsync<T>(string key, T defaultValue, Func<T, T> transform)
    {
        var current = await LoadAsync<T>(key) ?? defaultValue;
        var updated = transform(current);
        await SaveAsync(key, updated);
        return updated;
    }

    public bool Exists(string key) => File.Exists(GetFilePath(key));

    private string GetFilePath(string key) => Path.Combine(_basePath, $"{key}.json");
}
