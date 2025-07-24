using StaffSyncService.Models;
using System.Text.Json;
using Hangfire;
using Hangfire.Server;
using Hangfire.Console;

namespace StaffSyncService.Services;

/// <summary>
/// Service that orchestrates the staff synchronization pipeline
/// </summary>
public class StaffSyncOrchestrator
{
    private readonly IStaffApiService _staffApiService;
    private readonly IMarkerService _markerService;
    private readonly ILogger<StaffSyncOrchestrator> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public StaffSyncOrchestrator(
        IStaffApiService staffApiService,
        IMarkerService markerService,
        ILogger<StaffSyncOrchestrator> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _staffApiService = staffApiService;
        _markerService = markerService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task SynchronizeStaffAsync(PerformContext? context = null)
    {
        context?.WriteLine("Starting staff sync...");
        try
        {
            _logger.LogInformation("Starting staff synchronization process");
              // Get the last processed marker
            var marker = await _markerService.GetLastProcessedMarkerAsync();

            marker.LastProcessedId = "2150175";
            marker.NextLink = "https://integration-toolkit.vialto.com/staff/events/after/2150176?limit=100";


            // Get staff changes since the last processed record, using nextLink if available
            var (changes, nextLink) = await _staffApiService.GetStaffChangesAsync(marker.LastProcessedId, marker.NextLink);
            
            // Update the next link in the marker
            marker.NextLink = nextLink;
            
            if (changes.Count == 0)
            {
                _logger.LogInformation("No new staff changes to process");
                return;
            }
            
            _logger.LogInformation("Found {Count} staff changes to process", changes.Count);
            
            // Process each change in order
            foreach (var change in changes.OrderBy(c => c.Timestamp))
            {
                await ProcessStaffChangeAsync(change);
                  // Update marker after processing each change
                // Update both properties for backward compatibility
                if (int.TryParse(change.Id, out var id)) {
                    marker.LastIdProcessed = id;
                    marker.LastProcessedId = change.Id;
                }
                marker.LastProcessedTime = change.Timestamp;
                await _markerService.UpdateMarkerAsync(marker);
            }
            
            _logger.LogInformation("Staff synchronization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during staff synchronization");
        }
        context?.WriteLine("Staff sync completed.");
    }

    private async Task ProcessStaffChangeAsync(StaffChangeEvent changeEvent)
    {
        _logger.LogInformation("Processing staff change: {EventType} for staff ID: {StaffId}", 
            changeEvent.EventType, changeEvent.StaffId);
        
        try
        {
            switch (changeEvent.EventType)
            {
                case "StaffJoinedPractice":
                case "StaffProfileChanged":
                    // For joins and profile changes, retrieve full details
                    var staffDetails = await _staffApiService.GetStaffDetailsAsync(changeEvent.StaffId);
                    _logger.LogInformation("Retrieved details for {GivenName} {FamilyName} (Office: {OfficeName}, Status: {Status})", 
                        staffDetails.GivenName, staffDetails.FamilyName, staffDetails.Office?.Name, staffDetails.Status);
                    
                    // Process the staff details with all the new fields
                    await ProcessStaffDetailsAsync(staffDetails);
                    break;
                
                case "StaffSeparated":
                    // For staff separations, deactivate the staff member
                    await _staffApiService.DeactivateStaffAsync(changeEvent.StaffId);
                    _logger.LogInformation("Staff with ID {StaffId} has been deactivated", 
                        changeEvent.StaffId);
                    break;
                
                default:
                    _logger.LogWarning("Unknown event type: {EventType}", changeEvent.EventType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing staff change for ID {StaffId}", changeEvent.StaffId);
        }
    }

    private async Task ProcessStaffDetailsAsync(StaffDetails staffDetails)
    {
        try
        {
            _logger.LogInformation("Processing staff details for {GivenName} {FamilyName}",
                staffDetails.GivenName, staffDetails.FamilyName);

            // Validate required fields before serialization
            var officeIDStr = staffDetails.Office?.Uri != null && staffDetails.Office.Uri.Contains("offices/")
                ? staffDetails.Office.Uri.Substring(staffDetails.Office.Uri.IndexOf("offices/") + 8)
                : string.Empty;
            int officeID = 0;
            int.TryParse(officeIDStr, out officeID);
            bool isActive = staffDetails.Status?.Trim().ToLowerInvariant() == "active";
            string Sanitize(string? s) => (s ?? string.Empty)
                .Replace("\"", "'")
                .Replace("\\", "/")
                .Replace("\r", " ")
                .Replace("\n", " ");
            if (string.IsNullOrWhiteSpace(staffDetails.GivenName) ||
                string.IsNullOrWhiteSpace(staffDetails.FamilyName) ||
                string.IsNullOrWhiteSpace(staffDetails.Office?.OfficeType) ||
                officeID == 0)
            {
                _logger.LogError("Missing required staff fields: GivenName={GivenName}, FamilyName={FamilyName}, OfficeType={OfficeType}, OfficeID={OfficeID}",
                    staffDetails.GivenName, staffDetails.FamilyName, staffDetails.Office?.OfficeType, officeID);
                throw new InvalidOperationException("One or more required staff fields are missing or empty.");
            }

            var staffJson = JsonSerializer.Serialize(new
            {
                id = staffDetails.Uri,
                firstName = Sanitize(staffDetails.GivenName),
                lastName = Sanitize(staffDetails.FamilyName),
                emailAddress = Sanitize(staffDetails.EmailAddress),
                guids = staffDetails.Guids,
                active = isActive,
                status = staffDetails.Status,
                access = staffDetails.Access,
                photo = staffDetails.Photo,
                mobilePhone = staffDetails.MobilePhone,
                officePhone = staffDetails.OfficePhone,
                officeCountry = staffDetails.Office?.Country ?? string.Empty,
                officeID = officeID,
                officeName = staffDetails.Office?.Name ?? string.Empty,
                officeType = staffDetails.Office?.OfficeType ?? string.Empty,
                officeUri = staffDetails.Office?.Uri,
                profileUrl = staffDetails.Office?.Uri ?? string.Empty
            });

            _logger.LogDebug("Prepared staff details JSON: {Json}", staffJson);

            // Implement the actual API call to the Help Center
            var helpCenterClient = _httpClientFactory.CreateClient("HelpCenterApi");
            var baseUrl = Environment.GetEnvironmentVariable("HelpCenterApi__BaseUrl") ??
                          _configuration["HelpCenterApi:BaseUrl"] ??
                          "https://tigerhelp.vialto.com";
            var content = new StringContent(staffJson, System.Text.Encoding.UTF8, "application/json");
            var response = await helpCenterClient.PostAsync($"{baseUrl}/api/piasg/tiger/staff", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Help Center API call failed: {StatusCode} {ReasonPhrase} - Response Body: {Body}",
                    (int)response.StatusCode, response.ReasonPhrase, errorBody);
                response.EnsureSuccessStatusCode(); // Will still throw for upstream handling
            }
            _logger.LogInformation("Successfully posted staff details to Help Center for {GivenName} {FamilyName}",
                staffDetails.GivenName, staffDetails.FamilyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing staff details for {GivenName} {FamilyName}",
                staffDetails.GivenName, staffDetails.FamilyName);
        }
    }
}