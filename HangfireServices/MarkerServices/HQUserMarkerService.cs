using StaffSyncService.Models;

namespace HangfireServices.MarkerServices;

/// <summary>
/// Service for managing the marker files used by the HQUser Priority pipeline
/// </summary>
public interface IHQUserMarkerService
{
    Task<HQUserPriorityMarker> GetPriorityMarkerAsync();
    Task UpdatePriorityMarkerAsync(HQUserPriorityMarker marker);
    Task<List<HQUserPriorityData>> GetPriorityDataAsync();
    Task AddPriorityDataAsync(HQUserPriorityData data);
    Task ClearPriorityDataAsync();
}

public class HQUserMarkerService : IHQUserMarkerService
{
    private readonly IMarkerService<HQUserPriorityMarker> _markerService;
    private readonly ILogger<HQUserMarkerService> _logger;
    private readonly string _dataFilePath;

    public HQUserMarkerService(IConfiguration configuration, ILogger<HQUserMarkerService> logger)
    {
        _logger = logger;

        // Get the marker file paths from configuration using the new structure
        var markerFilePath = string.IsNullOrEmpty(configuration["HQUserSync:MarkerFilePath"]) ?
               Path.Combine(AppContext.BaseDirectory, configuration["HQUserSync:MarkerFilePath"]) :
            Path.Combine(AppContext.BaseDirectory, "HQUserPriorityMarker.json");

        var _dataFilePath = string.IsNullOrEmpty(configuration["HQUserSync:DataFilePath"]) ?
              Path.Combine(AppContext.BaseDirectory, configuration["HQUserSync:DataFilePath"]) :
           Path.Combine(AppContext.BaseDirectory, "HQUserPriorityData.json");

        var markerLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var markerLogger = markerLoggerFactory.CreateLogger<MarkerService<HQUserPriorityMarker>>();
        _markerService = new MarkerService<HQUserPriorityMarker>(markerFilePath, markerLogger);
    }

    public async Task<HQUserPriorityMarker> GetPriorityMarkerAsync()
    {
        return await _markerService.GetMarkerAsync(_dataFilePath);
    }

    public async Task UpdatePriorityMarkerAsync(HQUserPriorityMarker marker)
    {
        await _markerService.UpdateMarkerAsync(marker, _dataFilePath);
    }

    public async Task<List<HQUserPriorityData>> GetPriorityDataAsync()
    {
        try
        {
            if (!File.Exists(_dataFilePath))
            {
                _logger.LogInformation("Priority data file not found at {Path}. Creating empty list.", _dataFilePath);
                await File.WriteAllTextAsync(_dataFilePath, "[]");
                return new List<HQUserPriorityData>();
            }

            var json = await File.ReadAllTextAsync(_dataFilePath);
            var data = System.Text.Json.JsonSerializer.Deserialize<List<HQUserPriorityData>>(json)
                ?? new List<HQUserPriorityData>();

            _logger.LogInformation("Retrieved {Count} priority data items from {Path}", data.Count, _dataFilePath);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading priority data file from {Path}", _dataFilePath);
            return new List<HQUserPriorityData>();
        }
    }

    public async Task AddPriorityDataAsync(HQUserPriorityData data)
    {
        try
        {
            var currentData = await GetPriorityDataAsync();
            currentData.Add(data);

            var json = System.Text.Json.JsonSerializer.Serialize(currentData);
            await File.WriteAllTextAsync(_dataFilePath, json);

            _logger.LogInformation("Added priority data for user {UserId} with ID {Id} to {Path}",
                data.UserId, data.Id, _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding priority data to {Path}", _dataFilePath);
        }
    }

    public async Task ClearPriorityDataAsync()
    {
        try
        {
            await File.WriteAllTextAsync(_dataFilePath, "[]");
            _logger.LogInformation("Cleared priority data in {Path}", _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing priority data in {Path}", _dataFilePath);
        }
    }
}