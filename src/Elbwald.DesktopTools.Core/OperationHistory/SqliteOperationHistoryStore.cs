using Elbwald.DesktopTools.Contracts.OperationHistory;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.OperationHistory;

public sealed class SqliteOperationHistoryStore : IOperationHistoryStore
{
    private readonly OperationHistoryOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteOperationHistoryStore(OperationHistoryOptions options) => _options = options;

    public async Task AppendBatchAsync(IEnumerable<OperationHistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        var batch = entries.ToArray();
        if (batch.Length == 0) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var entry in batch)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = """
                    INSERT INTO OperationHistory
                    (BatchId, OperationType, SourcePath, DestinationPath, Sha256, Length, MediaItemId, CompletedUtc, ReversalOfBatchId)
                    VALUES ($batch, $type, $source, $destination, $sha, $length, $media, $completed, $reversal);
                    """;
                command.Parameters.AddWithValue("$batch", entry.BatchId);
                command.Parameters.AddWithValue("$type", entry.OperationType);
                command.Parameters.AddWithValue("$source", entry.SourcePath);
                command.Parameters.AddWithValue("$destination", entry.DestinationPath);
                command.Parameters.AddWithValue("$sha", entry.Sha256);
                command.Parameters.AddWithValue("$length", entry.Length);
                command.Parameters.AddWithValue("$media", (object?)entry.MediaItemId ?? DBNull.Value);
                command.Parameters.AddWithValue("$completed", entry.CompletedUtc.ToString("O"));
                command.Parameters.AddWithValue("$reversal", (object?)entry.ReversalOfBatchId ?? DBNull.Value);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationHistoryEntry>> GetRecentAsync(int maxCount = 200, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT BatchId, OperationType, SourcePath, DestinationPath, Sha256, Length, MediaItemId, CompletedUtc, ReversalOfBatchId
                FROM OperationHistory ORDER BY Id DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(maxCount, 1, 5000));
            return await ReadEntriesAsync(command, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OperationHistoryEntry>> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT BatchId, OperationType, SourcePath, DestinationPath, Sha256, Length, MediaItemId, CompletedUtc, ReversalOfBatchId
                FROM OperationHistory WHERE BatchId = $batch ORDER BY Id ASC;
                """;
            command.Parameters.AddWithValue("$batch", batchId);
            return await ReadEntriesAsync(command, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasReversalAsync(string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM OperationHistory WHERE ReversalOfBatchId = $batch LIMIT 1);";
            command.Parameters.AddWithValue("$batch", batchId);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(value) != 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<OperationHistoryEntry>> ReadEntriesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<OperationHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OperationHistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return result;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath) ?? throw new InvalidOperationException("Ungültiger History-Datenbankpfad."));
        await using var connection = new SqliteConnection($"Data Source={_options.DatabasePath}");
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS OperationHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BatchId TEXT NOT NULL,
                    OperationType TEXT NOT NULL,
                    SourcePath TEXT NOT NULL,
                    DestinationPath TEXT NOT NULL,
                    Sha256 TEXT NOT NULL,
                    Length INTEGER NOT NULL,
                    MediaItemId TEXT NULL,
                    CompletedUtc TEXT NOT NULL,
                    ReversalOfBatchId TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var hasReversalColumn = false;
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(OperationHistory);";
            await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "ReversalOfBatchId", StringComparison.OrdinalIgnoreCase))
                {
                    hasReversalColumn = true;
                    break;
                }
            }
        }

        if (!hasReversalColumn)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE OperationHistory ADD COLUMN ReversalOfBatchId TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText = """
                CREATE INDEX IF NOT EXISTS IX_OperationHistory_BatchId ON OperationHistory(BatchId);
                CREATE INDEX IF NOT EXISTS IX_OperationHistory_MediaItemId ON OperationHistory(MediaItemId);
                CREATE INDEX IF NOT EXISTS IX_OperationHistory_ReversalOfBatchId ON OperationHistory(ReversalOfBatchId);
                """;
            await indexes.ExecuteNonQueryAsync(cancellationToken);
        }

        _initialized = true;
    }
}
