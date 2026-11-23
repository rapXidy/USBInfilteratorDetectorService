using Cronos;
using Microsoft.Extensions.Options;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.CoreWorkers;

namespace USBInfilteratorDetectorService;

/// <summary>
/// Cronos-scheduled Windows background service that scopes and runs the USB detector job.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Worker> _logger;
    private readonly CronExpression _cron;

    /// <summary>
    /// Creates the worker host loop using AppSettings:CronInterval.
    /// </summary>
    public Worker(
        ILogger<Worker> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        var schedule = options.Value.CronInterval;
        if (string.IsNullOrWhiteSpace(schedule))
        {
            schedule = "*/1 * * * *";
        }

        _cron = CronExpression.Parse(schedule);
    }

    /// <summary>
    /// Waits for each cron occurrence and executes the detector job until cancellation.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("USB Infilterator Detector Worker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            var utcNow = DateTime.UtcNow;
            var nextUtc = _cron.GetNextOccurrence(utcNow);
            if (nextUtc is null)
            {
                _logger.LogError("Cron expression produced no next occurrence; stopping worker loop");
                break;
            }

            var delay = nextUtc.Value - utcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }

            await DoJobAsync(stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("USB Infilterator Detector Worker stopping");
    }

    /// <summary>
    /// Creates a DI scope and runs <see cref="IUsbDetectorJob"/> for the current tick.
    /// </summary>
    private async Task DoJobAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            _logger.LogInformation("USB detector job scope opened at {Time}", DateTimeOffset.UtcNow);
            var job = scope.ServiceProvider.GetRequiredService<IUsbDetectorJob>();
            await job.Run(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USB detector job failed");
        }
    }
}
