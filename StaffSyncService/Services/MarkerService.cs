using System.Text.Json;
using StaffSyncService.Models;

namespace StaffSyncService.Services;

/// <summary>
/// Service for managing the marker file that tracks the last processed staff record
/// </summary>
public interface IMarkerService
{
    Task<ProcessingMarker> GetLastProcessedMarkerAsync();
    Task UpdateMarkerAsync(ProcessingMarker marker);
}

public class MarkerService : IMarkerService
{
    private readonly IMarkerService<ProcessingMarker> _genericMarkerService;

    public MarkerService(IConfiguration configuration, ILogger<MarkerService> logger)
    {
        // Get the marker file path from configuration using the new structure
        var markerFilePath = configuration["StaffSync:MarkerFilePath"] ?? 
            Path.Combine(AppContext.BaseDirectory, "StaffMarker.json");
            
        var markerLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var markerLogger = markerLoggerFactory.CreateLogger<GenericMarkerService<ProcessingMarker>>();
        _genericMarkerService = new GenericMarkerService<ProcessingMarker>(markerFilePath, markerLogger);
    }

    public async Task<ProcessingMarker> GetLastProcessedMarkerAsync()
    {
        return await _genericMarkerService.GetMarkerAsync();
    }

    public async Task UpdateMarkerAsync(ProcessingMarker marker)
    {
        await _genericMarkerService.UpdateMarkerAsync(marker);
    }
}