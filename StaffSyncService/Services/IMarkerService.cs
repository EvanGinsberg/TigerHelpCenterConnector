using System.Text.Json;

namespace StaffSyncService.Services;

/// <summary>
/// Generic interface for managing marker files that track the last processed record
/// </summary>
public interface IMarkerService<T>
{
    Task<T> GetMarkerAsync();
    Task UpdateMarkerAsync(T marker);
}

/// <summary>
/// Generic implementation of a marker service that handles JSON serialization
/// </summary>
public class GenericMarkerService<T> : IMarkerService<T> where T : class, new()
{
    private readonly ILogger<GenericMarkerService<T>> _logger;
    private readonly string _markerFilePath;

    public GenericMarkerService(string markerFilePath, ILogger<GenericMarkerService<T>> logger)
    {
        _markerFilePath = markerFilePath;
        _logger = logger;
    }

    public async Task<T> GetMarkerAsync()
    {
        try
        {
            if (!File.Exists(_markerFilePath))
            {
                _logger.LogInformation("Marker file not found at {Path}. Creating a new one.", _markerFilePath);
                return new T();
            }

            var json = await File.ReadAllTextAsync(_markerFilePath);
            var marker = JsonSerializer.Deserialize<T>(json) ?? new T();
            _logger.LogInformation("Retrieved marker from {Path}", _markerFilePath);
            return marker;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading marker file from {Path}", _markerFilePath);
            return new T();
        }
    }

    public async Task UpdateMarkerAsync(T marker)
    {
        try
        {
            var json = JsonSerializer.Serialize(marker);
            await File.WriteAllTextAsync(_markerFilePath, json);
            _logger.LogInformation("Updated marker file at {Path}", _markerFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing marker file to {Path}", _markerFilePath);
        }
    }
}