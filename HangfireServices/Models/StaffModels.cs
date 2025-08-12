using System.Text.Json.Serialization;

namespace StaffSyncService.Models;

/// <summary>
/// Represents a staff change event from the Tiger system
/// </summary>
public class StaffChangeEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [JsonPropertyName("eventType")]
    public string EventType { get; set; } = string.Empty;
    
    [JsonPropertyName("staffId")]
    public string StaffId { get; set; } = string.Empty;
}

/// <summary>
/// Represents detailed staff information based on the Tiger API employee details
/// </summary>
public class StaffDetails
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("givenName")]
    public string GivenName { get; set; } = string.Empty;

    [JsonPropertyName("familyName")]
    public string FamilyName { get; set; } = string.Empty;

    [JsonPropertyName("guids")]
    public List<string> Guids { get; set; } = new List<string>();

    [JsonPropertyName("practiceTypes")]
    public List<string> PracticeTypes { get; set; } = new List<string>();

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("access")]
    public string Access { get; set; } = string.Empty;

    [JsonPropertyName("office")]
    public OfficeInfo? Office { get; set; }

    [JsonPropertyName("officePhone")]
    public string OfficePhone { get; set; } = string.Empty;

    [JsonPropertyName("mobilePhone")]
    public string MobilePhone { get; set; } = string.Empty;

    [JsonPropertyName("emailAddress")]
    public string EmailAddress { get; set; } = string.Empty;

    [JsonPropertyName("photo")]
    public string Photo { get; set; } = string.Empty;
}

/// <summary>
/// Represents a marker to track the last processed record
/// </summary>
public class ProcessingMarker
{
    /// <summary>
    /// The ID of the last processed record
    /// </summary>
    [JsonPropertyName("lastIdProcessed")]
    public int LastIdProcessed { get; set; }

    /// <summary>
    /// URL for the next batch of records to process
    /// </summary>
    [JsonPropertyName("nextLink")]
    public string NextLink { get; set; } = string.Empty;
    
    /// <summary>
    /// Legacy property for compatibility with existing code
    /// </summary>
    [JsonIgnore]
    public string LastProcessedId 
    {
        get => LastIdProcessed.ToString();
        set
        {
            if (int.TryParse(value, out var id))
            {
                LastIdProcessed = id;
            }
        }
    }

    /// <summary>
    /// Last time a record was processed - not stored in the file but used in memory
    /// </summary>
    [JsonIgnore]
    public DateTime LastProcessedTime { get; set; }
}

/// <summary>
/// Represents office information
/// </summary>
public class OfficeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("officeType")]
    public string OfficeType { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;

    [JsonPropertyName("companyName")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("phoneNumber")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("faxNumber")]
    public string FaxNumber { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}