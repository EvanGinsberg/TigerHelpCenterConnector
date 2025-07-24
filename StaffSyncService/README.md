# Staff and HQUser Priority Sync Service

A Windows Service that implements two StreamSets pipelines:
1. Staff synchronization pipeline
2. HQUser Priority management pipeline

## Features

- Handles both Staff and HQUser Priority pipelines in a single service
- Configurable sync intervals for each pipeline
- Uses certificate-based authentication for Tiger API interactions
- Uses basic authentication for Help Center API interactions
- Tracks processing state using marker files
- Handles XML and JSON responses
- Runs as a Windows Service

## Pipeline Functionality

### Staff Pipeline
- Polls the Tiger API for staff changes at configurable intervals (default: 60 seconds)
- Uses the `/staff/events/after/{id}?limit=500` endpoint to retrieve staff changes
- Processes different types of staff events (joins, separations, profile changes)
- Tracks the last processed record using a marker file
- Retrieves detailed staff information via the `/staff/{id}` endpoint
- For staff separations, deactivates staff members via the Help Center API

### HQUser Priority Pipeline
- Polls the Tiger API for HQ user changes at configurable intervals (default: 5 minutes)
- Uses the `/hqusers/events/after/{id}?limit=500` endpoint to retrieve HQ user changes
- Manages priority data for users
- Creates default priority entries for new users
- Updates existing priority data
- Maintains a separate marker file for tracking HQ user changes

## Installation

### Prerequisites

- .NET 9.0 SDK or higher
- Windows OS with permissions to install services
- SSL certificate for Tiger API access (converted from JKS to PFX format)

### Certificate Setup

The StreamSets pipeline uses a Java KeyStore (JKS) file for certificate authentication with the Tiger API.
For the C# Windows Service, you need to convert this to a PFX (PKCS#12) format:

```bash
# Using keytool and openssl (requires Java and OpenSSL installed)
keytool -importkeystore -srckeystore snow-tiger.jks -destkeystore snow-tiger.p12 -srcstoretype JKS -deststoretype PKCS12 -srcstorepass r.JUu3sD2E3D -deststorepass r.JUu3sD2E3D
openssl pkcs12 -in snow-tiger.p12 -out snow-tiger.pfx -nodes
```

Place the resulting PFX file in the appropriate directory (default: C:\ProgramData\StaffSyncService\).

### Build the Service

```powershell
dotnet publish -c Release
```

### Running in Debug Mode

For development and debugging purposes, you can run the service with enhanced logging:

1. Configure the debug logging level in appsettings.json:

```json
"Logging": {
  "LogLevel": {
    "Default": "Debug",
    "Microsoft.Hosting.Lifetime": "Information"
  }
}
```

2. Run the service interactively with the Development environment:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run
```

3. For detailed HTTP request/response logging, modify Program.cs to add HTTP logging:

```csharp
builder.Services.AddHttpClient("TigerApi")
    .ConfigureHttpClient(client => {
        // Existing code
    })
    .AddHttpMessageHandler(() => new HttpLoggingHandler(LogLevel.Debug));
```

4. Set breakpoints in Visual Studio or Visual Studio Code to debug specific components.

5. View real-time logs in the console window while the service is running.

### Install as a Windows Service

1. Create necessary directories:

```powershell
mkdir C:\ProgramData\StaffSyncService
# Copy the PFX certificate file to this directory
```

2. Create the service using sc.exe (run as Administrator):

```powershell
sc create StaffSyncService binPath= "path\to\StaffSyncService.exe"
sc description StaffSyncService "Staff and HQUser Priority synchronization service"
sc config StaffSyncService start= auto
```

3. Start the service:

```powershell
sc start StaffSyncService
```

### Alternative: Run interactively for testing

```powershell
dotnet run
```

## Configuration

Configuration settings are stored in appsettings.json:

### API Configuration:

- `TigerApi:BaseUrl`: The base URL of the Tiger API
- `TigerApi:KeystorePath`: Path to the PFX certificate file
- `TigerApi:KeystorePassword`: Password for the certificate file
- `HelpCenterApi:BaseUrl`: The base URL of the Tiger Help Center API
- `HelpCenterApi:Username`: Username for Help Center API
- `HelpCenterApi:Password`: Password for Help Center API

### Staff Pipeline Configuration:

- `StaffSync:Enabled`: Whether the Staff pipeline is enabled (default: true)
- `StaffSync:IntervalSeconds`: Interval between staff synchronization runs (default: 60 seconds)
- `StaffSync:MarkerFilePath`: Path to the staff marker file

### HQUser Pipeline Configuration:

- `HQUserSync:Enabled`: Whether the HQUser priority pipeline is enabled (default: false, temporarily disabled)
- `HQUserSync:IntervalSeconds`: Interval between HQUser priority synchronization runs (default: 300 seconds)
- `HQUserSync:MarkerFilePath`: Path to the HQUser priority marker file
- `HQUserSync:DataFilePath`: Path to the HQUser priority data file

## Pipeline Management

The service allows individual pipelines to be enabled or disabled via configuration:

### Enabling/Disabling Pipelines

To temporarily disable a pipeline, set the appropriate `Enabled` flag to `false` in appsettings.json:

```json
"HQUserSync": {
  "Enabled": false,
  // Other settings...
}
```

When a pipeline is disabled:
- No processing will occur for that pipeline
- Resources associated with that pipeline will still be initialized
- Diagnostic logs will indicate the pipeline is disabled
- The service will continue running other enabled pipelines

### Re-enabling a Pipeline

To re-enable a disabled pipeline:
1. Set the `Enabled` flag back to `true` in appsettings.json
2. Restart the service

The service will pick up where it left off using the marker file, ensuring no data is lost.

## Logging

Logs are written to the Windows Event Log under the "StaffSyncService" source.

### Debug Logging

For debugging purposes, set the following in appsettings.Development.json:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "System.Net.Http.HttpClient": "Debug"
    },
    "Console": {
      "LogLevel": {
        "Default": "Debug"
      },
      "FormatterName": "json",
      "FormatterOptions": {
        "IncludeScopes": true,
        "TimestampFormat": "HH:mm:ss ",
        "UseUtcTimestamp": false
      }
    }
  }
}
```

To view detailed API call information, create a HttpLoggingHandler class:

```csharp
public class HttpLoggingHandler : DelegatingHandler
{
    private readonly LogLevel _logLevel;
    
    public HttpLoggingHandler(LogLevel logLevel)
    {
        _logLevel = logLevel;
        InnerHandler = new HttpClientHandler();
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Log request details
        Console.WriteLine($"Request: {request.Method} {request.RequestUri}");
        
        // Continue with the request
        var response = await base.SendAsync(request, cancellationToken);
        
        // Log response details
        Console.WriteLine($"Response: {(int)response.StatusCode} {response.ReasonPhrase}");
        
        return response;
    }
}
```

## Hangfire Dashboard

### Enabling the Dashboard (Development Mode)

When running in Development mode, the service will automatically start a minimal web server hosting the Hangfire Dashboard at:

    http://localhost:5001/hangfire

You can use this dashboard to monitor background jobs, view job history, and manually trigger jobs.

**Note:** The dashboard is only enabled in Development mode for security reasons. Do not expose this port in production environments.

### Accessing the Dashboard

1. Run the service in Development mode:

    ```powershell
    $env:DOTNET_ENVIRONMENT = "Development"
    dotnet run
    ```

2. Open your browser and navigate to:

    http://localhost:5001/hangfire

You should see the Hangfire Dashboard UI.

## Functional Comparison to StreamSets

This C# Windows Service implements all the key functionality from both StreamSets pipelines:

1. Authentication:
   - Certificate-based authentication for Tiger API
   - Basic authentication for Help Center API

2. Data Processing:
   - XML to JSON conversion
   - Staff event type handling
   - HQUser priority data management

3. State Management:
   - Marker-based tracking of last processed records
   - Separate tracking for each pipeline

4. Scheduling:
   - Configurable intervals for each pipeline
   - Independent execution paths
   - Feature flags to enable/disable specific pipelines

## Staff Data Structure

The service processes staff records with the following fields, which match the structure used in the StreamSets pipeline:

### StaffDetails Model

- `id` - The staff ID
- `firstName` - First name of the staff member
- `lastName` - Last name of the staff member  
- `emailAddress` - Email address
- `guids` - List of GUIDs associated with the staff member
- `active` - Boolean indicating if the staff member is active
- `status` - Status of the staff member (e.g., "Active", "Inactive")
- `access` - Access level/role
- `photo` - Photo URL or ID
- `mobilePhone` - Mobile phone number
- `officePhone` - Office phone number
- `officeCountry` - Country of office
- `officeID` - Office ID number
- `officeName` - Name of the office
- `officeType` - Type of office
- `profileUrl` - URL to profile (often empty)

### XML Processing

The service handles Tiger API's XML responses by:
1. Extracting the root employee details node
2. Parsing office information from the URI attribute
3. Converting GUIDs from XML elements into a C# List
4. Determining active status from the "status" field
5. Building a structured StaffDetails object with all required fields

This matches the data structure and processing flow that was implemented in the StreamSets pipeline's JavaScript evaluators and XML processors.

### Marker File Structure

The marker file format has been updated to match the StreamSets pipeline's structure:

```json
{
  "lastIdProcessed": 12345,
  "nextLink": "https://integration-toolkit.vialto.com/staff/events?after=12345&limit=500"
}
```

The service uses the `nextLink` parameter for improved pagination handling, which allows for more efficient processing of large data sets.