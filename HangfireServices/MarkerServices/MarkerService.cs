using System.Text.Json;
using Hangfire.MemoryStorage.Database;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StaffSyncService.Models;

namespace HangfireServices.MarkerServices;

/// <summary>
/// Generic implementation of a marker service that handles JSON serialization
/// </summary>
public class MarkerService<T> : IMarkerService<T> where T : class, new()
{
    private readonly ILogger<MarkerService<T>> _logger;
    private readonly string _markerFilePath;

    public MarkerService(string markerFilePath, ILogger<MarkerService<T>> logger)
    {
        _markerFilePath = markerFilePath;
        _logger = logger;
    }

    public async Task<T> GetMarkerAsync(string filePath)
    {
        try
        {
            if (!File.Exists(_markerFilePath))
            {
                _logger.LogInformation("Marker file not found at {Path}. Creating a new one.", _markerFilePath);
                return new T();
            }

            var json = await File.ReadAllTextAsync(_markerFilePath);
            _logger.LogInformation("Retrieved marker from {Path}", _markerFilePath);
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading marker file from {Path}", _markerFilePath);
            return new T();
        }
    }

    public async Task UpdateMarkerAsync(T marker, string filePath)
    {
        try
        {
            var json = JsonConvert.SerializeObject(marker);
            await File.WriteAllTextAsync(_markerFilePath, json);
            _logger.LogInformation("Updated marker file at {Path}", _markerFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing marker file to {Path}", _markerFilePath);
        }
    }

    public async Task ClearMarkerAsync(string filePath)
    {
        try
        {
            await File.WriteAllTextAsync(_markerFilePath, "[]");
            _logger.LogInformation("Cleared priority data in {Path}", _markerFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing priority data in {Path}", _markerFilePath);
        }
    }

    public async Task BulkMarkerupdateAsync(List<T> markerData, string filePath)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(markerData);
            await File.WriteAllTextAsync(_markerFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding priority data to {Path}", _markerFilePath);
        }
    }

    public async Task<List<T>> GetMarkerDataListAsync(string filePath)
    {
        try
        {
            if (!File.Exists(_markerFilePath))
            {
                _logger.LogInformation("Priority data file not found at {Path}. Creating empty list.", _markerFilePath);
                await File.WriteAllTextAsync(_markerFilePath, "[]");
                return new List<T>();
            }

            var json = await File.ReadAllTextAsync(_markerFilePath);
            var data = System.Text.Json.JsonSerializer.Deserialize<List<T>>(json)
                ?? new List<T>();

            _logger.LogInformation("Retrieved {Count} priority data items from {Path}", data.Count, _markerFilePath);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading priority data file from {Path}", _markerFilePath);
            return new List<T>();
        }
    }
}