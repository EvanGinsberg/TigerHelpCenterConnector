namespace StaffSyncService.Models;

/// <summary>
/// Configuration settings for Tiger API integration
/// Matches the configuration structure used in StreamSets pipeline
/// </summary>
public class TigerApiConfiguration
{
    public const string SectionName = "TigerApi";
    
    /// <summary>
    /// Base URL for Tiger API (e.g., https://integration-toolkit.vialto.com)
    /// </summary>
    public string BaseUrl { get; set; } = "https://integration-toolkit.vialto.com";
    
    /// <summary>
    /// Path to the SSL keystore file for certificate authentication
    /// Default matches StreamSets configuration: /share/sd_certs/snow-tiger.jks
    /// </summary>
    public string KeystorePath { get; set; } = "/share/sd_certs/snow-tiger.jks";
    
    /// <summary>
    /// Password for the SSL keystore
    /// Default matches StreamSets configuration
    /// </summary>
    public string KeystorePassword { get; set; } = "r.JUu3sD2E3D";
    
    /// <summary>
    /// Deactivation endpoint URL
    /// Default matches StreamSets configuration
    /// </summary>
    public string DeactivateEndpoint { get; set; } = "https://tigerhelp.vialto.com/api/piasg/tiger/hquser/deactivate";
    
    /// <summary>
    /// Timeout in milliseconds for HTTP requests
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;
    
    /// <summary>
    /// Maximum number of retry attempts for failed requests
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Request timeout in seconds
    /// Matches StreamSets requestTimeout configuration
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 60;
}
