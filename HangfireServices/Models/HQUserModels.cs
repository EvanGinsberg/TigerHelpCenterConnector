using System.Text.Json.Serialization;

namespace StaffSyncService.Models;

public class HQUserChangeEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    /// <summary>
    /// Parsed integer version of Id for StreamSets comparison logic
    /// </summary>
    public int EventIdInt { get; set; }
}

public class HQUserDetails
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;
    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = string.Empty;
    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
    [JsonPropertyName("associatedClients")]
    public List<AssociatedClient> AssociatedClients { get; set; } = new();
    [JsonPropertyName("engagements")]
    public List<string> Engagements { get; set; } = new();
}

public class AssociatedClient
{
    [JsonPropertyName("clientId")]
    public int ClientId { get; set; }
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    [JsonPropertyName("engagementIds")]
    public List<string> EngagementIds { get; set; } = new();
}

public class HQUserPriorityData
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("uId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("cat")]
    public string Category { get; set; } = string.Empty;
}

public class HQUserPriorityMarker
{
    [JsonPropertyName("startId")]
    public int StartId { get; set; }
}