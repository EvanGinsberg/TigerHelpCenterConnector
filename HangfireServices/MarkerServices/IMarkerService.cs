using StaffSyncService.Models;
using System.Text.Json;

namespace HangfireServices.MarkerServices;

/// <summary>
/// Generic interface for managing marker files that track the last processed record
/// </summary>
public interface IMarkerService<T>
{
    Task<T> GetMarkerAsync(string filePath);
    Task UpdateMarkerAsync(T marker, string filePath);
    Task BulkMarkerupdateAsync(List<T> markerData, string filePath);
    Task<List<T>> GetMarkerDataListAsync(string filePath);
    Task ClearMarkerAsync(string filePath);

}

