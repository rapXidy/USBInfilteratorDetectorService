using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using USBInfilteratorDetectorService.Configuration;
using USBInfilteratorDetectorService.Models;
using USBInfilteratorDetectorService.Services.Interfaces;

namespace USBInfilteratorDetectorService.Services;

/// <summary>
/// SQLite-backed FIFO offline queue for ingest payloads.
/// </summary>
public sealed class OfflineQueueStore : IOfflineQueueStore
{
    private readonly WorkerOptions _options;
    private readonly ILogger<OfflineQueueStore> _logger;
    private readonly string _dbPath;

    /// <summary>
    /// Creates an offline queue store rooted at OfflineQueuePath.
    /// </summary>
    public OfflineQueueStore(IOptions<WorkerOptions> options, ILogger<OfflineQueueStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _dbPath = Path.Combine(_options.OfflineQueuePath, "offline-queue.db");
    }

    /// <summary>
    /// Ensures the queue directory and SQLite schema exist.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.OfflineQueuePath);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        const string sql = """
            CREATE TABLE IF NOT EXISTS QueueItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PayloadJson TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                NextAttemptUtc TEXT NULL,
                Delivered INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_QueueItems_Pending
                ON QueueItems (Delivered, NextAttemptUtc, Id);
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Offline queue initialised at {Path}", _dbPath);
    }

    /// <summary>
    /// Persists a payload JSON row before any network send.
    /// </summary>
    public async Task EnqueueAsync(string payloadJson, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO QueueItems (PayloadJson, CreatedAtUtc, AttemptCount, NextAttemptUtc, Delivered)
            VALUES ($payload, $created, 0, NULL, 0);
            """;
        command.Parameters.AddWithValue("$payload", payloadJson);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the next undelivered items in FIFO order, respecting back-off.
    /// </summary>
    public async Task<IReadOnlyList<QueueItem>> DequeuePendingAsync(
        int maxBatchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, PayloadJson, CreatedAtUtc, AttemptCount, NextAttemptUtc, Delivered
            FROM QueueItems
            WHERE Delivered = 0
              AND (NextAttemptUtc IS NULL OR NextAttemptUtc <= $now)
            ORDER BY Id ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$limit", maxBatchSize);

        var items = new List<QueueItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            items.Add(new QueueItem
            {
                Id = reader.GetInt64(0),
                PayloadJson = reader.GetString(1),
                CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                AttemptCount = reader.GetInt32(3),
                NextAttemptUtc = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
                Delivered = reader.GetInt32(5) == 1
            });
        }

        return items;
    }

    /// <summary>
    /// Marks a queue item as successfully delivered.
    /// </summary>
    public async Task MarkDeliveredAsync(long id, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE QueueItems SET Delivered = 1, NextAttemptUtc = NULL WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a failed delivery attempt and schedules the next retry with exponential back-off.
    /// </summary>
    public async Task MarkFailedAsync(long id, int attemptCount, CancellationToken cancellationToken)
    {
        var delaySeconds = Math.Min(3600, (int)Math.Pow(2, Math.Min(attemptCount, 10)));
        var nextAttempt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE QueueItems
            SET AttemptCount = $attempts, NextAttemptUtc = $next
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$attempts", attemptCount);
        command.Parameters.AddWithValue("$next", nextAttempt.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Queue item {Id} delivery failed (attempt {Attempt}); next retry at {Next}",
            id,
            attemptCount,
            nextAttempt);
    }

    /// <summary>
    /// Creates a SQLite connection to the offline queue database.
    /// </summary>
    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
    }
}
