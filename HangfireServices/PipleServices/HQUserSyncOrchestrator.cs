using HangfireServices.MarkerServices;
using StaffSyncService.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace StaffSyncService.Services;

/// <summary>
/// Service that orchestrates the HQ User synchronization pipeline
/// </summary>
public class HQUserSyncOrchestrator
{
    private readonly IHQUserApiService _hqUserApiService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HQUserSyncOrchestrator> _logger;
    private readonly IMarkerService<HQUserPriorityMarker> _hqUserPriorityMarkerService;
    private readonly IMarkerService<HQUserPriorityData> _hqUserPriorityDataService;
    private readonly string _blockListPath;
    private readonly string _markerFilePath;
    private readonly string _dataFilePath;

    public HQUserSyncOrchestrator(
        IHQUserApiService hqUserApiService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IMarkerService<HQUserPriorityMarker> hqUserPriorityMarkerService,
        IMarkerService<HQUserPriorityData> hqUserPriorityDataService,
        ILogger<HQUserSyncOrchestrator> logger)
    {
        _hqUserApiService = hqUserApiService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _hqUserPriorityMarkerService = hqUserPriorityMarkerService;
        _hqUserPriorityDataService = hqUserPriorityDataService;

        _markerFilePath = string.IsNullOrEmpty(configuration["HQUserSync:MarkerFilePath"]) ?
             Path.Combine(AppContext.BaseDirectory, configuration["HQUserSync:MarkerFilePath"]) :
          Path.Combine(AppContext.BaseDirectory, "HQUserPriorityMarker.json");

        _dataFilePath = string.IsNullOrEmpty(configuration["HQUserSync:DataFilePath"]) ?
            Path.Combine(AppContext.BaseDirectory, configuration["HQUserSync:DataFilePath"]) :
         Path.Combine(AppContext.BaseDirectory, "HQUserPriorityData.json");

        // Get the block list path from configuration or use a default
        _blockListPath = configuration["HQUserSync:BlockListPath"] ??
            Path.Combine(AppContext.BaseDirectory, "BlockList.json");
    }
    public async Task SynchronizeHQUsersAsync()
    {
        try
        {
            _logger.LogInformation("Starting HQ user synchronization process");

            // Check if we should do full processing based on time (matching StreamSets JavaScript processor logic)
            bool shouldProcessFull = ShouldProcessBasedOnTime();

            _logger.LogInformation("Time-based processing decision: Full processing = {ShouldProcess}", shouldProcessFull);

            // Implementation of StreamSelector_07 logic
            if (shouldProcessFull)
            {
                // This matches the StreamSelector_07OutputLane1677623939815 path when process=1
                _logger.LogInformation("Executing full HQ User synchronization (process=1 path)");

                // First, process existing priority data (matches JavaScriptEvaluator_05)
                await ProcessExistingPriorityDataAsync();
            }
            else
            {
                // This matches the StreamSelector_07OutputLane1677623922412 path (default path)
                _logger.LogInformation("Executing minimal HQ User synchronization (default path)");

                // On this path, we just need to check for the latest event ID 
                // but don't process the full pipeline
                await UpdateMarkerWithLatestEventIdOnly();
            }

            _logger.LogInformation("HQ User synchronization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during HQ user synchronization");
        }
    }
    /// <summary>
    /// Determines if full processing should occur based on the current time
    /// This matches the StreamSets JavaScript processor and StreamSelector_07 logic
    /// JavaScript logic: if(m==0 || m==10 || m==20 || m==30 || m==40 || m==50) { p=1; }
    /// </summary>
    private bool ShouldProcessBasedOnTime()
    {
        // StreamSets logic: process=1 if minute is 0, 10, 20, 30, 40, or 50
        var currentMinute = DateTime.UtcNow.Minute;
        var shouldProcess = (currentMinute % 10 == 0);

        _logger.LogInformation("Time-based processing check: Current minute is {Minute}, process flag: {ProcessFlag}",
            currentMinute, shouldProcess ? 1 : 0);

        // Additional logging to match StreamSets behavior    
        _logger.LogInformation("UTC time: {UtcTime}, Local time: {LocalTime}",
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        return shouldProcess;
    }    /// <summary>
         /// Updates the marker with the latest event ID without full processing
         /// This matches the default path in StreamSelector_07 that goes through HTTPClient_10 → JavaScriptEvaluator_08 → StreamSelector_08
         /// </summary>
    private async Task UpdateMarkerWithLatestEventIdOnly()
    {
        try
        {
            _logger.LogInformation("Executing default path: getting latest event ID and comparing with start ID");

            // Get the current marker (matches JavaScriptEvaluator_08 reading HQUserPriorityMarker.json)
            var marker = await _hqUserPriorityMarkerService.GetMarkerAsync(_markerFilePath);

            // Get the latest event ID (matches HTTPClient_10 "get latest eventId")
            var latestId = await GetLatestEventIdAsync();

            // This matches JavaScriptEvaluator_08 logic: creates record with latestId and startId
            _logger.LogInformation("StreamSelector_08 comparison on default path: Latest ID = {LatestId}, Start ID = {StartId}",
                latestId, marker.StartId);

            // Implementation of StreamSelector_08 logic (same comparison used on both paths)
            if (latestId >= marker.StartId)
            {
                _logger.LogInformation("Taking StreamSelector_08 true path on default - updating marker to latest ID");

                // Update marker with latest ID without processing changes
                marker.StartId = latestId;
                await _hqUserPriorityMarkerService.UpdateMarkerAsync(marker, _markerFilePath);

                _logger.LogInformation("Updated marker to latest event ID {LatestId} without processing changes", latestId);
            }
            else
            {
                _logger.LogInformation("Taking StreamSelector_08 default path - no marker update needed");
                _logger.LogInformation("Latest event ID ({LatestId}) is less than start ID ({StartId}). No update needed.",
                    latestId, marker.StartId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in default path processing");
        }
    }
    /// <summary>
    /// Gets the latest event ID from the Tiger API
    /// </summary>
    private async Task<int> GetLatestEventIdAsync()
    {
        try
        {
            // Create HTTP client
            var client = _httpClientFactory.CreateClient("TigerApi");

            // Add standard headers
            client.DefaultRequestHeaders.Add("X-Source-System", "StaffSyncService");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            // Get the base URL from configuration
            var baseUrl = _configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com";
            // Get the latest event ID - this matches the StreamSets pipeline endpoint: ${tigerFeedUri}/mymobility-users/events?limit=1
            var response = await client.GetAsync($"{baseUrl}/mymobility-users/events?limit=1");

            // Enhanced error handling
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to get latest event ID. Status: {StatusCode}, Error: {ErrorContent}",
                    response.StatusCode, errorContent);
                return 0;
            }

            // Parse response as XML - this matches the StreamSets conf.dataFormat = "XML"
            var content = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Received empty response when getting latest event ID");
                return 0;
            }

            var xmlDoc = new System.Xml.XmlDocument();
            try
            {
                xmlDoc.LoadXml(content);
            }
            catch (System.Xml.XmlException ex)
            {
                _logger.LogError(ex, "Failed to parse XML response when getting latest event ID. Content: {Content}", content);
                return 0;
            }

            // Create XML namespaces for proper XPath query
            var nsManager = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
            nsManager.AddNamespace("atom", "http://www.w3.org/2005/Atom");
            nsManager.AddNamespace("ns1", "http://www.w3.org/2005/Atom");

            // Try multiple XPath queries to extract the ID value from XML
            var idNode = xmlDoc.SelectSingleNode("//ns1:entry[1]/ns1:id[1]", nsManager)
                        ?? xmlDoc.SelectSingleNode("//entry[1]/id[1]", nsManager)
                        ?? xmlDoc.SelectSingleNode("//id[1]", nsManager);

            if (idNode != null && int.TryParse(idNode.InnerText, out int latestId))
            {
                _logger.LogInformation("Latest event ID is {LatestId}", latestId);
                return latestId;
            }

            _logger.LogWarning("Could not determine latest event ID from response. XML content: {XmlContent}", content);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest event ID");
            return 0;
        }
    }
    private async Task ProcessUserChangesAsync(HQUserPriorityMarker marker)
    {
        try
        {
            // Get the latest event ID to implement the StreamSelector_08 logic
            var latestId = await GetLatestEventIdAsync();

            _logger.LogInformation("StreamSelector_08 comparison: Latest ID = {LatestId}, Start ID = {StartId}",
                latestId, marker.StartId);

            // Implementation of StreamSelector_08 logic
            // If latestId >= startId, we process events (true path)
            // Otherwise, we skip processing (default path)
            if (latestId >= marker.StartId)
            {
                _logger.LogInformation("Taking StreamSelector_08 true path - processing events since latest ID >= start ID");

                // This matches JavaScriptEvaluator_09 pagination logic
                int lastProcessedId = await ProcessEventsPaginated(marker.StartId, latestId);

                // Implementation of JavaScriptEvaluator_11 logic: update marker with lastProcessed + 1
                await UpdateMarkerAfterProcessing(lastProcessedId, latestId);
            }
            else
            {
                // Taking the default path of StreamSelector_08 (false condition)
                // In the StreamSets pipeline, this path leads to a Trash component
                _logger.LogInformation("Taking StreamSelector_08 default path - skipping processing since latest ID < start ID");

                // We don't need to do anything here as this path leads to a Trash component in StreamSets
                // Just log that we're taking this path for monitoring
                _logger.LogInformation("Latest event ID ({LatestId}) is less than start ID ({StartId}). No processing needed.",
                    latestId, marker.StartId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HQ user changes");
            throw;
        }
    }
    /// <summary>
    /// Process events using pagination, matching JavaScriptEvaluator_09 → HTTPClient_11 → JavaScriptEvaluator_10 flow
    /// </summary>
    private async Task<int> ProcessEventsPaginated(int startId, int latestId)
    {
        try
        {
            _logger.LogInformation("Processing events with pagination from {StartId} to {LatestId}", startId, latestId);

            // Get recordSize from configuration (defaults to 100 like StreamSets)
            var recordSize = int.Parse(_configuration["TigerApi:RecordSize"] ?? "100");
            var currentStartId = startId;
            var processedItemCount = 0;
            var lastProcessedId = startId - 1; // Initialize to startId - 1

            // This matches JavaScriptEvaluator_09: while (startEvent <= endEvent)
            while (currentStartId <= latestId)
            {
                _logger.LogInformation("Fetching events from ID {CurrentStartId} with limit {RecordSize}",
                    currentStartId, recordSize);

                // This matches HTTPClient_11: get events after currentStartId with limit
                var events = await _hqUserApiService.GetUserChangesAsync(currentStartId, recordSize);

                if (events.Count == 0)
                {
                    _logger.LogInformation("No more events found, stopping pagination");
                    break;
                }

                // This matches JavaScriptEvaluator_10: process events and write to priority data file
                var newPriorityItems = await ProcessEventsAndCreatePriorityData(events, latestId);
                processedItemCount += newPriorityItems.Count;
                // Add to priority data file immediately (matches JavaScriptEvaluator_10 file writing)
                await _hqUserPriorityDataService.BulkMarkerupdateAsync(newPriorityItems, _dataFilePath);
                // Track the highest event ID processed (matches JavaScriptEvaluator_10 lastProcessed tracking)
                foreach (var item in newPriorityItems)
                {
                    if (item.Id > lastProcessedId)
                    {
                        lastProcessedId = item.Id;
                    }
                }

                // Move to next page (matches JavaScriptEvaluator_09: startEvent += recordSize)
                currentStartId += recordSize;

                _logger.LogInformation("Processed {EventCount} events, created {PriorityCount} priority data items",
                    events.Count, newPriorityItems.Count);
            }

            _logger.LogInformation("Pagination complete. Total priority data items created: {TotalCount}, Last processed ID: {LastProcessedId}",
                processedItemCount, lastProcessedId);

            return lastProcessedId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in paginated event processing");
            throw;
        }
    }

    /// <summary>
    /// Process events and create priority data items, matching JavaScriptEvaluator_10 logic
    /// </summary>
    private async Task<List<HQUserPriorityData>> ProcessEventsAndCreatePriorityData(List<HQUserChangeEvent> events, int latestId)
    {
        try
        {
            var priorityDataItems = new List<HQUserPriorityData>();

            foreach (var eventItem in events)
            {
                // Only process events up to latestId (matches JavaScriptEvaluator_10 logic)
                if (!int.TryParse(eventItem.Id, out int eventId) || eventId > latestId)
                {
                    continue;
                }

                // Check if user is in block list (matches JavaScriptEvaluator_10 BlockList.json check)
                if (await IsUserInBlockListAsync(eventItem.UserId))
                {
                    _logger.LogInformation("User {UserId} is in block list, skipping event {EventId}",
                        eventItem.UserId, eventItem.Id);
                    continue;
                }

                // Create priority data based on event type (matches JavaScriptEvaluator_10 event processing)
                var priorityData = new HQUserPriorityData
                {
                    Id = eventId,
                    UserId = eventItem.UserId,
                    Category = eventItem.EventType
                };

                priorityDataItems.Add(priorityData);

                _logger.LogDebug("Added priority data: ID={EventId}, UserId={UserId}, Category={Category}",
                    eventId, eventItem.UserId, eventItem.EventType);
            }

            return priorityDataItems;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing events and creating priority data");
            return new List<HQUserPriorityData>();
        }
    }

    /// <summary>
    /// Process any users that need to be deactivated
    /// This matches the deactivation endpoint in the StreamSets pipeline
    /// </summary>
    private async Task ProcessDeactivationsAsync()
    {
        try
        {
            _logger.LogInformation("Processing deactivations");

            // Get all priority data
            var priorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);

            // Find any users that need to be deactivated
            var usersToDeactivate = priorityData
                .Where(p => p.Category == "Deactivate")
                .ToList();

            if (usersToDeactivate.Count == 0)
            {
                _logger.LogInformation("No users to deactivate");
                return;
            }

            _logger.LogInformation("Found {Count} users to deactivate", usersToDeactivate.Count);

            // Process each deactivation
            foreach (var user in usersToDeactivate)
            {
                await DeactivateUserAsync(user.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing deactivations");
        }
    }
    /// <summary>
    /// Deactivates a user via the Tiger API
    /// This matches the HTTP Client processor in StreamSets that calls the deactivation endpoint
    /// </summary>
    private async Task DeactivateUserAsync(string userId)
    {
        try
        {
            _logger.LogInformation("Deactivating user: {UserId}", userId);

            // Create HTTP client for Help Center API (HTTPClient_09 uses BASIC auth)
            var client = _httpClientFactory.CreateClient("HelpCenterApi");

            // Add standard headers
            client.DefaultRequestHeaders.Add("X-Source-System", "StaffSyncService");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // Get endpoint from configuration or use the one from StreamSets
            var deactivateEndpoint = _configuration["TigerApi:DeactivateEndpoint"] ??
                "https://tigerhelp.vialto.com/api/piasg/tiger/hquser/deactivate";

            // Create request body
            var requestBody = new { id = userId };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // Call API
            var response = await client.PostAsync(deactivateEndpoint, content);

            // Enhanced error handling
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to deactivate user {UserId}. Status: {StatusCode}, Error: {ErrorContent}",
                    userId, response.StatusCode, errorContent);
                return;
            }

            _logger.LogInformation("Successfully deactivated user: {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating user: {UserId}", userId);
        }
    }
    private async Task ProcessHQUserChangeAsync(HQUserChangeEvent changeEvent)
    {
        _logger.LogInformation("Processing HQ user change: {EventType} for user ID: {UserId}",
            changeEvent.EventType, changeEvent.UserId);

        try
        {
            // Check if user is in block list
            if (await IsUserInBlockListAsync(changeEvent.UserId))
            {
                _logger.LogInformation("User {UserId} is in block list, skipping processing", changeEvent.UserId);
                return;
            }

            // For all user changes, we need to get the full user details
            var userDetails = await _hqUserApiService.GetUserDetailsAsync(changeEvent.UserId);

            // Handle different event types
            switch (changeEvent.EventType.ToLower())
            {
                case "created":
                case "mymobilityusercreated":
                    await ProcessUserCreatedEvent(userDetails, changeEvent.Id);
                    break;

                case "deleted":
                case "mymobilityuserdeleted":
                    await ProcessUserDeletedEvent(userDetails, changeEvent.Id);
                    break;

                case "updated":
                case "mymobilityuserengagementchanged":
                    await ProcessUserEngagementChangedEvent(userDetails, changeEvent.Id);
                    break;

                case "deactivated":
                    await ProcessDeactivatedUser(userDetails);
                    break;

                default:
                    _logger.LogWarning("Unknown event type: {EventType} for user ID: {UserId}",
                        changeEvent.EventType, changeEvent.UserId);
                    await ProcessActiveUser(userDetails);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing HQ user change for ID {UserId}", changeEvent.UserId);
        }
    }
    private async Task<bool> IsUserInBlockListAsync(string userId)
    {
        try
        {
            // Check if block list file exists
            if (!File.Exists(_blockListPath))
            {
                _logger.LogInformation("Block list file not found at {Path}, creating empty block list", _blockListPath);

                // Create an empty block list file
                await File.WriteAllTextAsync(_blockListPath, "[]");
                return false;
            }

            // Read block list
            var json = await File.ReadAllTextAsync(_blockListPath);

            // Handle empty file
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogInformation("Block list file is empty, creating empty array");
                await File.WriteAllTextAsync(_blockListPath, "[]");
                return false;
            }

            var blockList = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();

            bool isBlocked = blockList.Contains(userId);
            if (isBlocked)
            {
                _logger.LogInformation("User {UserId} found in block list with {Count} total blocked users", userId, blockList.Count);
            }

            return isBlocked;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid JSON in block list file {Path}, treating as empty list", _blockListPath);
            // Reset the file to empty array
            await File.WriteAllTextAsync(_blockListPath, "[]");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking block list for user {UserId}, allowing processing", userId);
            return false;
        }
    }

    private async Task ProcessUserCreatedEvent(HQUserDetails userDetails, string eventId)
    {
        _logger.LogInformation("Processing user created event for user {UserId}", userDetails.Id);

        if (!userDetails.Active)
        {
            await MarkUserForDeactivation(userDetails);
            return;
        }

        // Create priority data entry for this event
        var priorityData = new HQUserPriorityData
        {
            Id = int.TryParse(eventId, out int id) ? id : 0,
            UserId = userDetails.Id,
            Category = "MyMobilityUserCreated"
        };

        var existingPriorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync( _dataFilePath);
        existingPriorityData.Add(priorityData);
        await _hqUserPriorityDataService.BulkMarkerupdateAsync(existingPriorityData, _dataFilePath);

        // Continue with normal processing
        await ProcessActiveUser(userDetails);
    }

    private async Task ProcessUserDeletedEvent(HQUserDetails userDetails, string eventId)
    {
        _logger.LogInformation("Processing user deleted event for user {UserId}", userDetails.Id);

        // Create priority data entry for this event
        var priorityData = new HQUserPriorityData
        {
            Id = int.TryParse(eventId, out int id) ? id : 0,
            UserId = userDetails.Id,
            Category = "MyMobilityUserDeleted"
        };


        var existingPriorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);
        existingPriorityData.Add(priorityData);
        await _hqUserPriorityDataService.BulkMarkerupdateAsync(existingPriorityData, _dataFilePath);

        // Mark for deactivation
        await MarkUserForDeactivation(userDetails);
    }

    private async Task ProcessUserEngagementChangedEvent(HQUserDetails userDetails, string eventId)
    {
        _logger.LogInformation("Processing user engagement changed event for user {UserId}", userDetails.Id);

        // Create priority data entry for this event
        var priorityData = new HQUserPriorityData
        {
            Id = int.TryParse(eventId, out int id) ? id : 0,
            UserId = userDetails.Id,
            Category = "MyMobilityUserEngagementChanged"
        };


        var existingPriorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);
        existingPriorityData.Add(priorityData);
        await _hqUserPriorityDataService.BulkMarkerupdateAsync(existingPriorityData, _dataFilePath);
        // Continue with normal processing
        await ProcessActiveUser(userDetails);
    }

    private async Task ProcessDeactivatedUser(HQUserDetails userDetails)
    {
        _logger.LogInformation("Processing deactivated user: {UserId}", userDetails.Id);
        await MarkUserForDeactivation(userDetails);
    }

    private async Task MarkUserForDeactivation(HQUserDetails userDetails)
    {
        var deactivationData = new HQUserPriorityData
        {
            Id = 0,
            UserId = userDetails.Id,
            Category = "Deactivate"
        };


        var existingPriorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);
        existingPriorityData.Add(deactivationData);
        await _hqUserPriorityDataService.BulkMarkerupdateAsync(existingPriorityData, _dataFilePath);
        _logger.LogInformation("Marked user for deactivation: {UserId}", userDetails.Id);
    }

    private async Task CreateDefaultPriorityData(HQUserDetails userDetails)
    {
        // Determine appropriate category based on user properties
        string category = DeterminePriorityCategory(userDetails);

        var defaultData = new HQUserPriorityData
        {
            Id = 0,
            UserId = userDetails.Id,
            Category = category
        };

        // Apply any additional rules
        ApplyPriorityRules(defaultData, userDetails);

        // Save to marker service
        var existingPriorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);
        existingPriorityData.Add(defaultData);
        await _hqUserPriorityDataService.BulkMarkerupdateAsync(existingPriorityData, _dataFilePath);

        // Also update the priority data in the source system
        await _hqUserApiService.UpdateUserPriorityAsync(defaultData);
    }

    /// <summary>
    /// Apply specialized rules from the StreamSets pipeline to the priority data
    /// </summary>
    private void ApplyPriorityRules(HQUserPriorityData data, HQUserDetails userDetails)
    {
        // Implement rules from the StreamSets JavaScript processors here

        // Example: Set category based on user role
        if (userDetails.Role?.ToLower() == "admin")
        {
            data.Category = "High";
        }
        else if (userDetails.AssociatedClients.Count > 5)
        {
            data.Category = "Medium";
        }
    }

    /// <summary>
    /// Determine the initial priority category for a user
    /// </summary>
    private string DeterminePriorityCategory(HQUserDetails userDetails)
    {
        // Implement any category determination logic from StreamSets

        // Example implementation
        if (userDetails.Role?.ToLower() == "admin")
        {
            return "High";
        }
        else if (userDetails.AssociatedClients.Count > 0)
        {
            return "Medium";
        }

        return "Default";
    }    /// <summary>
         /// Process existing priority data from the JSON file
         /// This matches the JavaScriptEvaluator_05 logic from StreamSets pipeline
         /// </summary>
    private async Task ProcessExistingPriorityDataAsync()
    {
        try
        {
            _logger.LogInformation("Processing existing priority data (JavaScriptEvaluator_05 logic)");

            // Get existing priority data from the JSON file
            var priorityData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);

            if (priorityData.Count == 0)
            {
                _logger.LogInformation("No existing priority data found, processing new user changes");

                // If no priority data exists, get marker and process new changes
                var marker = await _hqUserPriorityMarkerService.GetMarkerAsync(_markerFilePath);
                await ProcessUserChangesAsync(marker);
            }
            else
            {
                _logger.LogInformation("Found {Count} existing priority data items to process", priorityData.Count);

                // Keep track of processed items for cleanup
                var processedItems = new List<HQUserPriorityData>();

                // Process each existing priority data item (matches StreamSelector_03 and StreamSelector_04 logic)
                foreach (var data in priorityData)
                {
                    await ProcessExistingPriorityDataItemAsync(data);
                    processedItems.Add(data);
                }

                // Remove processed items from the priority data file (matches JavaScriptEvaluator_03)
                await RemoveProcessedPriorityDataAsync(processedItems);

                // After processing existing data, check for new changes
                var marker = await _hqUserPriorityMarkerService.GetMarkerAsync(_markerFilePath);
                await ProcessUserChangesAsync(marker);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing existing priority data");
        }
    }
    /// <summary>
    /// Process a single priority data item from existing data
    /// This matches the StreamSelector_04 logic that routes based on category:
    /// - MyMobilityUserCreated or MyMobilityUserEngagementChanged → HTTPClient_04 (get user data)
    /// - MyMobilityUserDeleted → HTTPClient_09 (deactivate user)  
    /// - default → Trash
    /// </summary>
    private async Task ProcessExistingPriorityDataItemAsync(HQUserPriorityData data)
    {
        try
        {
            _logger.LogInformation("Processing existing priority data item: ID={Id}, UserId={UserId}, Category={Category}",
                data.Id, data.UserId, data.Category);

            // Implementation of StreamSelector_04 logic
            if (data.Category == "MyMobilityUserCreated" || data.Category == "MyMobilityUserEngagementChanged")
            {
                // This matches StreamSelector_04OutputLane1622836742981 - user details path
                _logger.LogInformation("Processing created/engagement changed for user: {UserId}", data.UserId);

                // Get user details (matches HTTPClient_04)
                var userDetails = await _hqUserApiService.GetUserDetailsAsync(data.UserId);

                // Process the user based on their current state
                await ProcessActiveUser(userDetails);
            }
            else if (data.Category == "MyMobilityUserDeleted")
            {
                // This matches StreamSelector_04OutputLane16228334421860 - deactivation path
                _logger.LogInformation("Processing deletion for user: {UserId}", data.UserId);
                await DeactivateUserAsync(data.UserId);
            }
            else
            {
                // This matches StreamSelector_04OutputLane16228334421861 - default path (Trash)
                _logger.LogInformation("Unknown category '{Category}' for user {UserId}, skipping processing",
                    data.Category, data.UserId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing priority data item for user {UserId}", data.UserId);
        }
    }

    /// <summary>
    /// Removes processed priority data items from the JSON file
    /// This matches the JavaScriptEvaluator_03 "remove from Data file" logic from StreamSets
    /// </summary>
    private async Task RemoveProcessedPriorityDataAsync(List<HQUserPriorityData> processedItems)
    {
        try
        {
            if (processedItems.Count == 0)
            {
                _logger.LogInformation("No processed items to remove from priority data file");
                return;
            }

            _logger.LogInformation("Removing {Count} processed items from priority data file", processedItems.Count);

            // Get current priority data
            var currentData = await _hqUserPriorityDataService.GetMarkerDataListAsync(_dataFilePath);

            // Filter out processed items (matches JavaScriptEvaluator_03 filter logic)
            var filteredData = currentData
                .Where(item => !processedItems.Any(processed => processed.Id == item.Id))
                .ToList();

            // Write filtered data back to file
            var json = JsonSerializer.Serialize(filteredData);
            var dataFilePath = _configuration["HQUserSync:DataFilePath"] ??
                Path.Combine(AppContext.BaseDirectory, "HQUserPriorityData.json");

            await File.WriteAllTextAsync(dataFilePath, json);

            _logger.LogInformation("Removed {RemovedCount} items, {RemainingCount} items remain in priority data file",
                processedItems.Count, filteredData.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing processed items from priority data file");
        }
    }

    /// <summary>
    /// Process active user through the complete StreamSets flow:
    /// HTTPClient_04 → StreamSelector_06 → JavaScriptEvaluator_06 → StreamSelector_09 → HTTPClient_02 → StreamSelector_05
    /// </summary>
    private async Task ProcessActiveUser(HQUserDetails userDetails)
    {
        try
        {
            _logger.LogInformation("Processing active user: {UserId}", userDetails.Id);

            // Check if user data has required fields (matches StreamSelector_06 "User not found" check)
            if (string.IsNullOrEmpty(userDetails.Id) ||
                string.IsNullOrEmpty(userDetails.FirstName) ||
                string.IsNullOrEmpty(userDetails.LastName) ||
                string.IsNullOrEmpty(userDetails.EmailAddress))
            {
                _logger.LogWarning("User {UserId} missing required fields, skipping user creation/update", userDetails.Id);
                return;
            }

            // Build user payload (matches JavaScriptEvaluator_06 logic)
            var userPayload = BuildUserPayload(userDetails);

            // Check if payload is properly formatted (matches StreamSelector_09 HasProperData check)
            if (string.IsNullOrEmpty(userPayload))
            {
                _logger.LogWarning("Failed to build proper user payload for user {UserId}", userDetails.Id);
                return;
            }

            // Create/Update user in Help Center (matches HTTPClient_02)
            bool success = await CreateOrUpdateUserAsync(userDetails.Id, userPayload);

            if (success)
            {
                _logger.LogInformation("Successfully created/updated user {UserId}", userDetails.Id);
            }
            else
            {
                _logger.LogWarning("Failed to create/update user {UserId}", userDetails.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing active user {UserId}", userDetails.Id);
        }
    }

    /// <summary>
    /// Builds user payload for creation/update (matches JavaScriptEvaluator_06 outputStr building)
    /// </summary>
    private string BuildUserPayload(HQUserDetails userDetails)
    {
        try
        {            // Build associated clients array (matches JavaScriptEvaluator_06 assocClientsStr logic)
            var assocClientsArray = "[";
            var engagementsArray = "[";
            var roleStr = string.Empty;

            if (userDetails.AssociatedClients != null && userDetails.AssociatedClients.Count > 0)
            {
                for (int a = 0; a < userDetails.AssociatedClients.Count; a++)
                {
                    var client = userDetails.AssociatedClients[a];
                    if (client == null || client.ClientId == 0 || client.EngagementIds == null)
                    {
                        continue; // Skip if required nested objects are missing
                    }

                    assocClientsArray += client.ClientId + ",";

                    if (a == 0 && !string.IsNullOrEmpty(client.Role))
                    {
                        roleStr = client.Role;
                    }

                    foreach (var engagement in client.EngagementIds)
                    {
                        if (!string.IsNullOrEmpty(engagement))
                        {
                            engagementsArray += engagement + ",";
                        }
                    }
                }
            }

            assocClientsArray = assocClientsArray.TrimEnd(',') + "]";
            engagementsArray = engagementsArray.TrimEnd(',') + "]";

            // Build the JSON payload exactly as StreamSets does (matches JavaScriptEvaluator_06 outputStr)
            var outputStr = "{" +
                $"\"id\": \"{userDetails.Id}\"," +
                $"\"firstName\": \"{EscapeJsonString(userDetails.FirstName)}\"," +
                $"\"lastName\": \"{EscapeJsonString(userDetails.LastName)}\"," +
                $"\"emailAddress\": \"{userDetails.EmailAddress}\"," +
                $"\"role\": \"{roleStr}\"," +
                $"\"active\": \"true\"," +
                $"\"associatedClients\": {assocClientsArray}," +
                $"\"engagements\": {engagementsArray}" +
                "}";

            return outputStr;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building user payload for user {UserId}", userDetails.Id);
            return string.Empty;
        }
    }

    /// <summary>
    /// Escapes JSON strings (matches JavaScriptEvaluator_06 string replacement logic)
    /// </summary>
    private string EscapeJsonString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input
            .Replace("\"", "'")      // replace quotes with single quotes
            .Replace("\\", "/")      // replace backslashes with forward slashes  
            .Replace("\r", " ")      // replace carriage returns with spaces
            .Replace("\n", " ");     // replace newlines with spaces
    }

    /// <summary>
    /// Creates or updates user in Help Center API (matches HTTPClient_02)
    /// </summary>
    private async Task<bool> CreateOrUpdateUserAsync(string userId, string userPayload)
    {
        try
        {
            _logger.LogInformation("Creating/updating user {UserId} in Help Center", userId);

            // Create HTTP client for Help Center API (HTTPClient_02 uses BASIC auth)
            var client = _httpClientFactory.CreateClient("HelpCenterApi");

            // Add standard headers
            client.DefaultRequestHeaders.Add("X-Source-System", "StaffSyncService");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // HTTPClient_02 endpoint and configuration
            var endpoint = "https://tigerhelp.vialto.com/api/piasg/tiger/hquser";
            var content = new StringContent(userPayload, System.Text.Encoding.UTF8, "application/json");

            // Call API with POST (matches HTTPClient_02)
            var response = await client.PostAsync(endpoint, content);

            // Read response content for detailed logging
            var responseContent = await response.Content.ReadAsStringAsync();

            // Implementation of StreamSelector_05 logic for response checking
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully created/updated user {UserId}. Response: {Response}",
                    userId, responseContent);
                return true;
            }
            else
            {
                // Check for specific error conditions (matches StreamSelector_05 predicate logic)
                bool isAcceptableError =
                    responseContent.Contains("Criteria was not met to deactivate") ||
                    responseContent.Contains("No engagements where provided") ||
                    response.StatusCode == System.Net.HttpStatusCode.OK;

                if (isAcceptableError)
                {
                    _logger.LogInformation("User creation/update for {UserId} completed with acceptable condition: {Response}",
                        userId, responseContent);
                    return true;
                }
                else
                {
                    _logger.LogError("Failed to create/update user {UserId}. Status: {StatusCode}, Error: {ErrorContent}",
                        userId, response.StatusCode, responseContent);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating/updating user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Updates the marker after processing events, implementing JavaScriptEvaluator_11 logic
    /// Sets startId to lastProcessed + 1 for the next processing cycle
    /// </summary>
    private async Task UpdateMarkerAfterProcessing(int lastProcessedId, int latestId)
    {
        try
        {
            _logger.LogInformation("Updating marker after processing. LastProcessed: {LastProcessedId}, LatestId: {LatestId}",
                lastProcessedId, latestId);

            // Read current marker to preserve other fields
            var marker = await _hqUserPriorityMarkerService.GetMarkerAsync(_markerFilePath);

            // Implementation of JavaScriptEvaluator_11 logic: set startId to lastProcessed + 1
            marker.StartId = lastProcessedId + 1;

            // Update the marker file (matches JavaScriptEvaluator_11 FileWriter logic)
            await _hqUserPriorityMarkerService.UpdateMarkerAsync(marker, _markerFilePath);

            _logger.LogInformation("Updated marker startId to {NewStartId} (lastProcessed + 1)", marker.StartId);

            // Log completion status (matches JavaScriptEvaluator_11 status logging)
            if (lastProcessedId == latestId)
            {
                _logger.LogInformation("All entries have been processed (lastProcessed == latestId). Processing complete.");
            }
            else if (lastProcessedId < latestId)
            {
                _logger.LogInformation("Still need to process more entries (lastProcessed < latestId). Next processing will start from ID {NextStartId}",
                    marker.StartId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating marker after processing events");
            throw;
        }
    }
}