using System.Net.Http.Json;
using System.Text.Json;
using StaffSyncService.Models;

namespace StaffSyncService.Services;

/// <summary>
/// Service for interacting with the Tiger API for HQ User operations
/// </summary>
public interface IHQUserApiService
{
    Task<List<HQUserChangeEvent>> GetUserChangesAsync(int lastProcessedId);
    Task<List<HQUserChangeEvent>> GetUserChangesAsync(int lastProcessedId, int limit);
    Task<HQUserDetails> GetUserDetailsAsync(string userId);
    Task<List<HQUserPriorityData>> GetUserPriorityDataAsync(string userId);
    Task UpdateUserPriorityAsync(HQUserPriorityData priorityData);
}

/// <summary>
/// Implementation of the Tiger API service for HQ User operations
/// </summary>
public class HQUserApiService : IHQUserApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HQUserApiService> _logger;
    private readonly IConfiguration _configuration;

    public HQUserApiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration, 
        ILogger<HQUserApiService> logger)
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
    }    public async Task<List<HQUserChangeEvent>> GetUserChangesAsync(int lastProcessedId)
    {
        try
        {
            _logger.LogInformation("Fetching HQ user changes after ID: {LastId}", lastProcessedId);

            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";

            // Call the Tiger API to get HQ user changes - Updated to match StreamSets endpoint
            var response = await client.GetAsync($"{baseUrl}/mymobility-users/events/after/{lastProcessedId}?limit=500");
            
            // Enhanced error handling to match StreamSets pipeline behavior
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Tiger API returned error {StatusCode}: {ErrorContent}", 
                    response.StatusCode, errorContent);
                
                // Return empty list rather than throwing to match StreamSets error handling
                return new List<HQUserChangeEvent>();
            }

            // Parse XML response based on StreamSets configuration
            var content = await response.Content.ReadAsStringAsync();
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Received empty response from Tiger API");
                return new List<HQUserChangeEvent>();
            }
            
            var xmlDoc = new System.Xml.XmlDocument();
            try
            {
                xmlDoc.LoadXml(content);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogError(ex, "Failed to parse XML response from Tiger API. Content: {Content}", content);
                return new List<HQUserChangeEvent>();
            }

            var events = new List<HQUserChangeEvent>();
            // Create XML namespaces for proper XPath query
            var nsManager = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
            nsManager.AddNamespace("atom", "http://www.w3.org/2005/Atom");
            nsManager.AddNamespace("def", "http://www.w3.org/2005/Atom"); // Default namespace

            // Extract entries from XML using the default namespace
            var entries = xmlDoc.SelectNodes("//def:feed/def:entry", nsManager);
            if (entries != null)
            {
                foreach (System.Xml.XmlNode entry in entries)
                {
                    var idNode = entry.SelectSingleNode("./def:id", nsManager);
                    var categoryNode = entry.SelectSingleNode("./def:category", nsManager);
                    var contentNode = entry.SelectSingleNode("./def:content/myMobilityUser/Uri", nsManager);

                    if (idNode != null)
                    {                        // Extract event type from category term attribute
                        string eventType = "Unknown";
                        if (categoryNode != null)
                        {
                            var termAttribute = categoryNode.Attributes?["term"];
                            if (termAttribute != null)
                            {
                                eventType = termAttribute.Value;
                            }
                        }

                        // Extract user ID from URI
                        string userId = string.Empty;
                        if (contentNode != null)
                        {
                            // Parse the URI to extract the user GUID
                            // Format: http://integration-toolkit.vialto.com/myMobilityHQ-users/{userId}/engagements
                            string uri = contentNode.InnerText;
                            int startIndex = uri.IndexOf("myMobilityHQ-users/") + "myMobilityHQ-users/".Length;
                            int endIndex = uri.IndexOf("/engagements");

                            if (startIndex > 0 && endIndex > startIndex)
                            {
                                userId = uri.Substring(startIndex, endIndex - startIndex);
                            }
                            else
                            {
                                userId = uri; // Fallback to using the full URI if parsing fails
                            }
                        }

                        events.Add(new HQUserChangeEvent
                        {
                            Id = idNode.InnerText,
                            UserId = userId,
                            EventType = eventType
                        });
                    }
                }
            }            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching HQ user changes");
            return new List<HQUserChangeEvent>();
        }
    }

    /// <summary>
    /// Gets HQ user changes with pagination, matching HTTPClient_11 endpoint: ${tigerFeedUri}/mymobility-users/events/after/${record:value('/eventId')}?limit=${recordSize}
    /// </summary>
    public async Task<List<HQUserChangeEvent>> GetUserChangesAsync(int lastProcessedId, int limit)
    {
        try
        {
            _logger.LogInformation("Fetching HQ user changes after ID: {LastId} with limit: {Limit}", lastProcessedId, limit);

            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";

            // Call the Tiger API using the exact endpoint from StreamSets HTTPClient_11
            var response = await client.GetAsync($"{baseUrl}/mymobility-users/events/after/{lastProcessedId}?limit={limit}");
            
            // Enhanced error handling to match StreamSets pipeline behavior
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Tiger API returned error {StatusCode}: {ErrorContent}", 
                    response.StatusCode, errorContent);
                
                // Return empty list rather than throwing to match StreamSets error handling
                return new List<HQUserChangeEvent>();
            }

            // Parse XML response based on StreamSets configuration (dataFormat = "XML")
            var content = await response.Content.ReadAsStringAsync();
            
            // Handle empty response
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogInformation("Received empty response from Tiger API");
                return new List<HQUserChangeEvent>();
            }
            
            var xmlDoc = new System.Xml.XmlDocument();
            try
            {
                xmlDoc.LoadXml(content);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogError(ex, "Failed to parse XML response from Tiger API. Content: {Content}", content);
                return new List<HQUserChangeEvent>();
            }

            var events = new List<HQUserChangeEvent>();
            
            // Create XML namespaces matching the StreamSets feed structure
            var nsManager = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
            nsManager.AddNamespace("ns1", "http://www.w3.org/2005/Atom");
            nsManager.AddNamespace("atom", "http://www.w3.org/2005/Atom");

            // Extract entries from XML using the namespaces that match StreamSets JavaScriptEvaluator_10
            var entries = xmlDoc.SelectNodes("//ns1:feed/ns1:entry", nsManager) ?? 
                         xmlDoc.SelectNodes("//atom:feed/atom:entry", nsManager) ??
                         xmlDoc.SelectNodes("//feed/entry", nsManager);

            if (entries != null)
            {
                foreach (System.Xml.XmlNode entry in entries)
                {
                    var idNode = entry.SelectSingleNode("./ns1:id", nsManager) ?? 
                                entry.SelectSingleNode("./atom:id", nsManager) ??
                                entry.SelectSingleNode("./id", nsManager);
                    
                    var categoryNode = entry.SelectSingleNode("./ns1:category", nsManager) ??
                                      entry.SelectSingleNode("./atom:category", nsManager) ??
                                      entry.SelectSingleNode("./category", nsManager);
                    
                    var contentNode = entry.SelectSingleNode("./ns1:content", nsManager) ??
                                     entry.SelectSingleNode("./atom:content", nsManager) ??
                                     entry.SelectSingleNode("./content", nsManager);

                    if (idNode != null)
                    {
                        // Extract event type from category term attribute (matches JavaScriptEvaluator_10)
                        string eventType = "Unknown";
                        if (categoryNode != null)
                        {
                            var termAttribute = categoryNode.Attributes?["term"];
                            if (termAttribute != null)
                            {
                                eventType = termAttribute.Value;
                            }
                        }

                        // Extract user ID - matches JavaScriptEvaluator_10 logic for both Created/Deleted and EngagementChanged
                        string userId = string.Empty;
                        
                        if (eventType == "MyMobilityUserEngagementChanged")
                        {
                            // For engagement changes: extract from URI (matches JavaScriptEvaluator_10 Uri extraction)
                            var uriNode = contentNode?.SelectSingleNode(".//Uri") ?? 
                                         contentNode?.SelectSingleNode(".//myMobilityUser/Uri") ??
                                         contentNode?.SelectSingleNode(".//myMobilityUser/Uri/value");
                            
                            if (uriNode != null)
                            {
                                string uri = uriNode.InnerText;
                                // Matches StreamSets: uri.replace('https://integration-toolkit.vialto.com/myMobilityHQ-users/','').replace('/engagements','')
                                userId = uri.Replace("https://integration-toolkit.vialto.com/myMobilityHQ-users/", "")
                                           .Replace("/engagements", "");
                            }
                        }
                        else if (eventType == "MyMobilityUserCreated" || eventType == "MyMobilityUserDeleted")
                        {
                            // For created/deleted: extract from Id field (matches JavaScriptEvaluator_10 Id extraction)
                            var userIdNode = contentNode?.SelectSingleNode(".//Id") ??
                                           contentNode?.SelectSingleNode(".//myMobilityUser/Id") ??
                                           contentNode?.SelectSingleNode(".//myMobilityUser/Id/value");
                            
                            if (userIdNode != null)
                            {
                                userId = userIdNode.InnerText;
                            }
                        }

                        // Parse event ID as integer to match StreamSets logic
                        if (int.TryParse(idNode.InnerText, out int eventId))
                        {
                            events.Add(new HQUserChangeEvent
                            {
                                Id = idNode.InnerText,
                                UserId = userId,
                                EventType = eventType,
                                EventIdInt = eventId  // For StreamSets comparison logic
                            });
                        }
                    }
                }
            }

            _logger.LogInformation("Successfully parsed {EventCount} events from Tiger API response", events.Count);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching paginated HQ user changes");
            return new List<HQUserChangeEvent>();
        }
    }

    public async Task<HQUserDetails> GetUserDetailsAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Fetching details for HQ user ID: {UserId}", userId);
            
            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
              // Updated to match StreamSets endpoint
            var response = await client.GetAsync($"{baseUrl}/myMobilityHQ-users/{userId}");
            
            // Enhanced error handling to match StreamSets pipeline behavior
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get user details for {UserId}. Status: {StatusCode}, Error: {ErrorContent}", 
                    userId, response.StatusCode, errorContent);
                
                // Return default user with ID rather than throwing to match StreamSets error handling
                return new HQUserDetails { Id = userId, Active = false };
            }
            
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response for user {UserId}", userId);
                return new HQUserDetails { Id = userId, Active = false };
            }
            
            // Assume JSON response
            var userDetails = await response.Content.ReadFromJsonAsync<HQUserDetails>() 
                ?? new HQUserDetails { Id = userId, Active = false };
            
            _logger.LogDebug("Successfully retrieved user details for {UserId}: Active={Active}, Role={Role}", 
                userId, userDetails.Active, userDetails.Role);
                
            return userDetails;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response for user {UserId}", userId);
            return new HQUserDetails { Id = userId, Active = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching HQ user details for ID {UserId}", userId);
            return new HQUserDetails { Id = userId, Active = false };
        }
    }

    public async Task<List<HQUserPriorityData>> GetUserPriorityDataAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Fetching priority data for HQ user ID: {UserId}", userId);
            
            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
            
            // Updated to match StreamSets pattern (inferred from similar endpoints)
            var response = await client.GetAsync($"{baseUrl}/myMobilityHQ-users/{userId}/priority");
            response.EnsureSuccessStatusCode();
            
            var priorityData = await response.Content.ReadFromJsonAsync<List<HQUserPriorityData>>() 
                ?? new List<HQUserPriorityData>();
            return priorityData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching priority data for HQ user ID {UserId}", userId);
            return new List<HQUserPriorityData>();
        }
    }    public async Task UpdateUserPriorityAsync(HQUserPriorityData priorityData)
    {
        try
        {
            _logger.LogInformation("Updating priority data for HQ user ID: {UserId}", priorityData.UserId);
            if (string.IsNullOrEmpty(priorityData.UserId))
            {
                _logger.LogWarning("User ID is null or empty, cannot update priority data");
                return;
            }
            // Get the Tiger API client with certificate authentication
            var client = GetTigerApiClient();
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
            
            // Create a payload for the priority data
            var json = JsonSerializer.Serialize(priorityData);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            
            // Updated to match StreamSets pattern (inferred from similar endpoints)
            var response = await client.PostAsync($"{baseUrl}/myMobilityHQ-users/priority/update", content);
            
            // Enhanced error handling to match StreamSets pipeline behavior
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to update priority data for user {UserId}. Status: {StatusCode}, Error: {ErrorContent}", 
                    priorityData.UserId, response.StatusCode, errorContent);
                return; // Don't throw, just log and continue to match StreamSets behavior
            }
            
            _logger.LogInformation("Successfully updated priority data for user {UserId}", priorityData.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating priority data for HQ user ID {UserId}", priorityData.UserId);
            // Don't re-throw to match StreamSets error handling pattern
        }
    }
}