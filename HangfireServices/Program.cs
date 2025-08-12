using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using StaffSyncService;
using StaffSyncService.Services;
using System.Security.Cryptography.X509Certificates;
using Hangfire;
using Hangfire.Console;
using Hangfire.MemoryStorage;
using HangfireServices.Middleware;
using HangfireServices.MarkerServices;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
        // Configure as a Windows Service
        services.AddWindowsService(options =>
        {
            options.ServiceName = "StaffSyncService";
        });

        // Determine if we're in development mode
        var isDevelopment = hostContext.HostingEnvironment.IsDevelopment();
        var loggerFactory = LoggerFactory.Create(logging => 
        {
            logging.AddConsole();
            if (isDevelopment)
            {
                logging.AddDebug();
            }
        });
        var httpLogger = loggerFactory.CreateLogger("HttpClient");

        // Configure HTTP clients
        services.AddHttpClient("TigerApi")
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(hostContext.Configuration["TigerApi:BaseUrl"] ?? "https://integration-toolkit.vialto.com");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {        // Set up certificate-based authentication for Tiger API
                // Create the HttpClientHandler first for certificate configuration
                var clientHandler = new HttpClientHandler();
                
                // Create the final handler - either the client handler directly or wrapped in logging
                HttpMessageHandler handler;
                    
                var keystorePath = hostContext.Configuration["TigerApi:KeystorePath"] ?? "/share/sd_certs/snow-tiger.jks";
                var keystorePassword = hostContext.Configuration["TigerApi:KeystorePassword"] ?? "r.JUu3sD2E3D";
                try
                {
                    if (File.Exists(keystorePath))
                    {
#pragma warning disable SYSLIB0028 // Suppress obsolete warning
                        // While this method is marked obsolete, it works reliably for our use case
                        // The X509CertificateLoader approach is still evolving in .NET
                        var certificate = new X509Certificate2(keystorePath, keystorePassword);
#pragma warning restore SYSLIB0028

                        clientHandler.ClientCertificates.Add(certificate);
                        httpLogger.LogInformation("Successfully loaded certificate from {Path}", keystorePath);
                        
                        // Check certificate validity and report detailed information
                        var now = DateTime.Now;
                        if (now > certificate.NotAfter)
                        {
                            httpLogger.LogWarning("Certificate has expired on {ExpiryDate}", certificate.NotAfter);
                        }
                        else if (now < certificate.NotBefore)
                        {
                            httpLogger.LogWarning("Certificate is not yet valid until {StartDate}", certificate.NotBefore);
                        }
                        else
                        {
                            httpLogger.LogInformation("Certificate is valid from {StartDate} to {ExpiryDate} (Subject: {Subject})",
                                certificate.NotBefore, certificate.NotAfter, certificate.Subject);
                        }
                    }
                    else
                    {
                        // Log warning if certificate file is missing
                        httpLogger.LogWarning("Certificate file not found at {Path}", keystorePath);
                    }
                }
                catch (Exception ex)
                {
                    // Log the exception but continue (will attempt to connect without certificate)
                    httpLogger.LogError(ex, "Error loading certificate from {Path}: {Message}", keystorePath, ex.Message);
                }
                
                clientHandler.ServerCertificateCustomValidationCallback = 
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                    
                // If in development mode, wrap the handler with logging
                if (isDevelopment)
                {
                    var loggingHandler = new HttpLoggingHandler(httpLogger);
                    loggingHandler.InnerHandler = clientHandler;
                    handler = loggingHandler;
                }
                else
                {
                    handler = clientHandler;
                }
                
                return handler;
            });

        services.AddHttpClient("HelpCenterApi")
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(hostContext.Configuration["HelpCenterApi:BaseUrl"] ?? "https://tigerhelp.vialto.com");
            })
            .ConfigureHttpClient(client =>
            {
                // Set up basic authentication for Help Center API
                var username = hostContext.Configuration["HelpCenterApi:Username"] ?? "TigerAPIUser";
                var password = hostContext.Configuration["HelpCenterApi:Password"] ?? "";
                if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    var authBytes = System.Text.Encoding.ASCII.GetBytes($"{username}:{password}");
                    var authHeader = Convert.ToBase64String(authBytes);
                    client.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
                }
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
                isDevelopment ?
                    new HttpLoggingHandler(httpLogger) { InnerHandler = new HttpClientHandler() } :
                    new HttpClientHandler()
            );

        // Add Hangfire services
        services.AddHangfire(config => config.UseMemoryStorage().UseConsole());
        services.AddHangfireServer();

       

        // Configure Staff-related services
        services.AddSingleton(typeof(IMarkerService<>), typeof(MarkerService<>));
        services.AddSingleton<IStaffApiService, StaffApiService>();
        services.AddSingleton<StaffSyncOrchestrator>();

        // Configure HQUser-related services, even when disabled
        // This ensures we can enable it later without redeployment
        services.AddSingleton<IHQUserMarkerService, HQUserMarkerService>();
        services.AddSingleton<IHQUserApiService, HQUserApiService>();
        services.AddSingleton<HQUserSyncOrchestrator>();

        // Log the enabled state of each pipeline
        var staffSyncEnabled = hostContext.Configuration.GetValue("StaffSync:Enabled", true);
        var hqUserSyncEnabled = hostContext.Configuration.GetValue("HQUserSync:Enabled", false);

        var logger = loggerFactory.CreateLogger("Startup");
        logger.LogInformation("Staff Synchronization Pipeline: {Enabled}", staffSyncEnabled ? "Enabled" : "Disabled");
        logger.LogInformation("HQ User Priority Pipeline: {Enabled}", hqUserSyncEnabled ? "Enabled" : "Disabled");

        // Worker service registration removed; now using Hangfire for all background jobs
    })
    .ConfigureWebHostDefaults(webBuilder =>
    {
        webBuilder.UseUrls("http://localhost:5002");
        webBuilder.Configure(app =>
        {
            var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                app.UseHangfireDashboard("/hangfire");
            }
        });
    });

var host = builder.Build();

using (var scope = host.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    services.AddHangfireJobs();
}

host.Run();
