using Serilog;
using USBInfilteratorDetectorService;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.CoreWorkers;
using USBInfilteratorDetectorService.Services;
using USBInfilteratorDetectorService.Services.Interfaces;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("AppSettings"));

var logPath = builder.Configuration["AppSettings:LogPath"];
if (!string.IsNullOrWhiteSpace(logPath))
{
    Directory.CreateDirectory(logPath);
}

var loggerConfig = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext();

var seqUrl = builder.Configuration["AppSettings:SeqServerUrl"];
if (!string.IsNullOrWhiteSpace(seqUrl))
{
    loggerConfig = loggerConfig.WriteTo.Seq(seqUrl);
}

Log.Logger = loggerConfig.CreateLogger();

builder.Services.AddSerilog();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "USB Infilterator Detector";
});

builder.Services.AddSingleton<IHostContextService, HostContextService>();
builder.Services.AddSingleton<ISessionBuilder, SessionBuilder>();
builder.Services.AddSingleton<IOfflineQueueStore, OfflineQueueStore>();

builder.Services.AddScoped<IUsbPresenceService, UsbPresenceService>();
builder.Services.AddScoped<IAuditPolicyService, AuditPolicyService>();
builder.Services.AddScoped<IEventLogReader, EventLogReader>();
builder.Services.AddScoped<IRiskScoringEngine, RiskScoringEngine>();
builder.Services.AddScoped<IUsbDetectorJob, UsbDetectorJob>();

builder.Services.AddHttpClient<IIngestApiClient, IngestApiClient>();

builder.Services.AddHostedService<Worker>();

try
{
    Log.Information("Starting USB Infilterator Detector Service");
    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "USB Infilterator Detector Service terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
