using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using StaffSyncService.Models;

namespace StaffSyncService.Services;

/// <summary>
/// Service for interacting with the Tiger API
/// </summary>
public interface IStaffApiService
{
    Task<(List<StaffChangeEvent> Events, string NextLink)> GetStaffChangesAsync(string lastProcessedId, string? nextLink = null);
    Task<StaffDetails> GetStaffDetailsAsync(string staffId);
    Task DeactivateStaffAsync(string staffId);
    Task<bool> AddOrUpdateStaffAsync(StaffDetails staffDetails);
}

/// <summary>
/// Implementation of the Tiger API service that handles both Tiger API and Help Center API interactions
/// </summary>
public class StaffApiService : IStaffApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StaffApiService> _logger;
    private readonly IConfiguration _configuration;

    public StaffApiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration, 
        ILogger<StaffApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets a configured HTTP client for the Tiger API with certificate authentication
    /// </summary>
    private HttpClient GetTigerApiClient()
    {
        var client = _httpClientFactory.CreateClient("TigerApi");
        
        // Add standard headers
        client.DefaultRequestHeaders.Add("X-Source-System", "StaffSyncService");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }
    
    /// <summary>
    /// Gets a configured HTTP client for the Help Center API with basic authentication
    /// </summary>
    private HttpClient GetHelpCenterApiClient()
    {
        var client = _httpClientFactory.CreateClient("HelpCenterApi");
        
        // Add Basic Authentication
        var username = _configuration["HelpCenterApi:Username"] ?? "TigerAPIUser";
        var password = _configuration["HelpCenterApi:Password"] ?? "";
        
        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            var authBytes = Encoding.ASCII.GetBytes($"{username}:{password}");
            var authHeader = Convert.ToBase64String(authBytes);
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
        }
        
        // Add standard headers
        client.DefaultRequestHeaders.Add("X-Source-System", "StaffSyncService");
        client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        return client;
    }    public async Task<(List<StaffChangeEvent> Events, string NextLink)> GetStaffChangesAsync(string lastProcessedId, string? nextLink = null)
    {
        try
        {
            _logger.LogInformation("Fetching staff changes since ID: {LastId}", lastProcessedId);

            if(string.IsNullOrEmpty(lastProcessedId) && string.IsNullOrEmpty(nextLink))
            {
                lastProcessedId = "1000"; // Default to 1000 if no ID is provided and no nextLink
                _logger.LogWarning("Last processed ID is null or empty, defaulting to {DefaultId}", lastProcessedId);                
            }

            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
            
            // Use nextLink if provided, otherwise build the URL
            var requestUrl = !string.IsNullOrEmpty(nextLink)
                ? nextLink
                : $"{baseUrl}/staff/events/after/{lastProcessedId}?limit=500";
                
            _logger.LogInformation("Requesting staff changes from: {Url}", requestUrl);
            var response = await client.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();

            // The API might return XML that needs to be converted to JSON
            if (response.Content.Headers.ContentType?.MediaType?.Contains("xml") == true)
            {                // Read XML and convert to our model - handle Atom feed format
                var xmlContent = await response.Content.ReadAsStringAsync();
                var xmlDoc = XDocument.Parse(xmlContent);
                 
                // XML Namespace for Atom
                var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");
                  // Extract change events from XML (Atom feed)
                var events = from entry in xmlDoc.Descendants(atomNs + "entry")
                             let id = entry.Element(atomNs + "id")?.Value ?? ""
                             let updated = entry.Element(atomNs + "updated")?.Value ?? DateTime.UtcNow.ToString()
                             let category = entry.Element(atomNs + "category")?.Attribute("term")?.Value ?? ""
                             let staffLink = entry.Elements(atomNs + "link")
                                            .FirstOrDefault(l => l.Attribute("rel")?.Value == "staff")
                                            ?.Attribute("href")?.Value ?? ""
                             let staffId = !string.IsNullOrEmpty(staffLink) ? 
                                            staffLink.Split('/').Last() : ""
                             select new StaffChangeEvent
                             {
                                 Id = id,
                                 Timestamp = DateTime.Parse(updated),
                                 EventType = category,
                                 StaffId = staffId
                             };                // Extract next link from the Atom feed
                var nextLinkElement = xmlDoc.Descendants(atomNs + "link")
                    .FirstOrDefault(l => l.Attribute("rel")?.Value == "next");
                var extractedNextLink = nextLinkElement?.Attribute("href")?.Value ?? string.Empty;
                
                return (events.ToList(), extractedNextLink);
            }
            else
            {                // Assume JSON response
                var events = await response.Content.ReadFromJsonAsync<List<StaffChangeEvent>>() 
                    ?? new List<StaffChangeEvent>();
                
                // For JSON response, we might not get a next link directly
                // We would need to extract it from response headers or implement pagination logic
                var extractedNextLink = string.Empty;
                return (events, extractedNextLink);
            }
        }        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching staff changes");
            return (new List<StaffChangeEvent>(), string.Empty);
        }
    }

    private async Task<OfficeInfo?> GetOfficeInfoAsync(string officeUri)
    {
        if (string.IsNullOrWhiteSpace(officeUri))
            return null;
        try
        {
            var client = GetTigerApiClient();
            var response = await client.GetAsync(officeUri);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var officeInfo = JsonSerializer.Deserialize<OfficeInfo>(content);
            return officeInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching office info from URI: {OfficeUri}", officeUri);
            return null;
        }
    }

    public async Task<StaffDetails> GetStaffDetailsAsync(string staffId)
    {
        try
        {
            _logger.LogInformation("Fetching details for staff ID: {StaffId}", staffId);
            
            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
            var response = await client.GetAsync($"{baseUrl}/staff/{staffId}");
            response.EnsureSuccessStatusCode();
            
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                // Assume JSON response
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Staff Details: Received JSON content: {Content}", responseContent);
                var staffDetails = JsonSerializer.Deserialize<StaffDetails>(responseContent);
                if (staffDetails == null)
                {
                    _logger.LogWarning("Unable to deserialize staff details, creating empty object");
                    return new StaffDetails { Uri = staffId };
                }
                if (string.IsNullOrEmpty(staffDetails.Uri))
                {
                    staffDetails.Uri = staffId;
                }
                // Try to extract office URI from the JSON (if available)
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                string? officeUri = null;
                if (root.TryGetProperty("office", out var officeElement) && officeElement.TryGetProperty("uri", out var uriElement))
                {
                    officeUri = uriElement.GetString();
                }
                // If officeUri is found, fetch office info
                if (!string.IsNullOrEmpty(officeUri))
                {
                    var officeInfo = await GetOfficeInfoAsync(officeUri);
                    if (officeInfo != null)
                    {
                        staffDetails.Office = officeInfo;
                    }
                }
                return staffDetails;
            }
            else
            {
                _logger.LogWarning("Expected JSON response for staff details, but got: {ContentType}", response.Content.Headers.ContentType?.MediaType);
                return new StaffDetails { Uri = staffId };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching staff details for ID {StaffId}", staffId);
            return new StaffDetails { Uri = staffId };
        }
    }

    public async Task DeactivateStaffAsync(string staffId)
    {
        try
        {
            _logger.LogInformation("Deactivating staff with ID: {StaffId}", staffId);
            
            // Get the Help Center API client with basic authentication
            var client = GetHelpCenterApiClient();
            var baseUrl = _configuration["HelpCenterApi:BaseUrl"] ?? "https://tigerhelp.vialto.com";
            
            // Create a payload to deactivate the staff member
            var content = new StringContent($$"""
                {
                    "id": "{{staffId}}"
                }
                """, Encoding.UTF8, "application/json");
            
            // Call the Help Center API to deactivate staff
            var response = await client.PostAsync($"{baseUrl}/api/piasg/tiger/staff/deactivate", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Help Center API call failed: {StatusCode} {ReasonPhrase} - Response Body: {Body}",
                    (int)response.StatusCode, response.ReasonPhrase, errorBody);
                response.EnsureSuccessStatusCode();
            }
            _logger.LogInformation("Successfully deactivated staff with ID: {StaffId}", staffId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating staff with ID {StaffId}", staffId);
            throw;
        }
    }

    public async Task<bool> AddOrUpdateStaffAsync(StaffDetails staffDetails)
    {
        try
        {
            var client = GetHelpCenterApiClient();
            var baseUrl = _configuration["HelpCenterApi:BaseUrl"] ?? "https://tigerhelp.vialto.com";
            var url = $"{baseUrl}/api/piasg/tiger/staff";
            var json = JsonSerializer.Serialize(staffDetails);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("Help Center API call failed: {StatusCode} {ReasonPhrase} - Response Body: {Body}",
                    (int)response.StatusCode, response.ReasonPhrase, errorBody);
                response.EnsureSuccessStatusCode();
            }
            _logger.LogInformation("Successfully added/updated staff with ID: {StaffId}", staffDetails.Uri);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding/updating staff with ID {StaffId}", staffDetails.Uri);
            return false;
        }
    }
}