using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace HangfireServices.Middleware;

/// <summary>
/// Logging handler to provide detailed HTTP request/response logging for debugging
/// </summary>
public class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger _logger;
    private readonly LogLevel _logLevel;

    public HttpLoggingHandler(ILogger logger, LogLevel logLevel = LogLevel.Debug)
    {
        _logger = logger;
        _logLevel = logLevel;
        // Note: InnerHandler should be set by the calling code to preserve certificate configuration
        // We don't create a new HttpClientHandler here
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Log the request details
        await LogRequest(request);

        // Process the request
        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.Log(_logLevel, ex, "HTTP request error: {ExceptionMessage}", ex.Message);
            throw;
        }

        // Log the response details
        await LogResponse(response);

        return response;
    }

    private async Task LogRequest(HttpRequestMessage request)
    {
        var message = $"HTTP Request: {request.Method} {request.RequestUri}";
        
        if (request.Content != null)
        {
            message += $"\nContent-Type: {request.Content?.Headers?.ContentType}";
            if (request.Content?.Headers?.ContentType?.MediaType?.Contains("json") == true ||
                request.Content?.Headers?.ContentType?.MediaType?.Contains("xml") == true ||
                request.Content?.Headers?.ContentType?.MediaType?.Contains("text") == true)
            {
                var content = await request.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content))
                {
                    message += $"\nContent: {content}";
                }
            }
        }

        // Log headers (excluding authentication headers for security)
        message += LogHeaders(request.Headers, "Request Headers");

        _logger.Log(_logLevel, message);
    }

    private async Task LogResponse(HttpResponseMessage response)
    {
        var message = $"HTTP Response: {(int)response.StatusCode} {response.StatusCode} from {response.RequestMessage?.RequestUri}";
        
        if (response.Content != null)
        {
            message += $"\nContent-Type: {response.Content?.Headers?.ContentType}";
            if (response.Content?.Headers?.ContentType?.MediaType?.Contains("json") == true ||
                response.Content?.Headers?.ContentType?.MediaType?.Contains("xml") == true ||
                response.Content?.Headers?.ContentType?.MediaType?.Contains("text") == true)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(content))
                {
                    // Truncate long content to avoid overwhelming logs
                    if (content.Length > 1000)
                    {
                        content = content.Substring(0, 1000) + "... (truncated)";
                    }
                    message += $"\nContent: {content}";
                }
            }
        }

        // Log headers
        message += LogHeaders(response.Headers, "Response Headers");

        _logger.Log(_logLevel, message);
    }

    private string LogHeaders(HttpHeaders headers, string headerType)
    {
        if (headers == null || !headers.Any())
        {
            return string.Empty;
        }

        var headerMsg = $"\n{headerType}:";
        foreach (var header in headers)
        {
            // Skip logging sensitive authentication headers
            if (header.Key.ToLower() == "authorization" || 
                header.Key.ToLower() == "cookie" || 
                header.Key.ToLower() == "set-cookie")
            {
                headerMsg += $"\n  {header.Key}: [REDACTED]";
                continue;
            }

            headerMsg += $"\n  {header.Key}: {string.Join(", ", header.Value)}";
        }

        return headerMsg;
    }
}