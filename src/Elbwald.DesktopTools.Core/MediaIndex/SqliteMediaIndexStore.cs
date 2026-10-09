using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.MediaIndex;

internal sealed class SqliteMediaIndexStore
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteMediaIndexStore(MediaIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        _databasePath = Path.GetFullPath(options.DatabasePath);
    }

    public async Task<MediaIndexSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var itemCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM MediaItems;", cancellationToken);
            var presentCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM MediaLocations WHERE IsPresent = 1;", cancellationToken);
            var historyCount = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM MediaLocations WHERE IsPresent = 0;", cancellationToken);

            await using var lastCommand = connection.CreateCommand();
            lastCommand.CommandText = "SELECT MAX(LastSeenUtc) FROM MediaItems;";
            var lastValue = await lastCommand.ExecuteScalarAsync(cancellationToken);
            DateTimeOffset? lastIndexed = null;
            if (lastValue is string text && DateTimeOffset.TryParse(text, out var parsed))
            {
                lastIndexed = parsed;
            }

            return new MediaIndexSummary(itemCount, presentCount, historyCount, lastIndexed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaIndexItem?> GetItemAsync(string mediaItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Sha256, Length, MediaType, FirstSeenUtc, LastSeenUtc
                FROM MediaItems
                WHERE Id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadItem(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaIndexItem?> FindByHashAsync(string sha256, long length, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, Sha256, Length, MediaType, FirstSeenUtc, LastSeenUtc
                FROM MediaItems
                WHERE Sha256 = $sha256 AND Length = $length
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$sha256", sha256);
            command.Parameters.AddWithValue("$length", length);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaIndexLocation>> GetLocationsAsync(string mediaItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, MediaItemId, EndpointId, EndpointKind, VolumeId, RelativePath,
                       Length, LastWriteTimeUtc, IsPresent, FirstSeenUtc, LastSeenUtc
                FROM MediaLocations
                WHERE MediaItemId = $id
                ORDER BY IsPresent DESC, LastSeenUtc DESC;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);

            var result = new List<MediaIndexLocation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result.Add(ReadLocation(reader));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaIndexSearchResult>> SearchAsync(
        MediaIndexSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, 500);
        var search = string.IsNullOrWhiteSpace(query.SearchText)
            ? null
            : query.SearchText.Trim();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            var presencePredicate = query.PresentOnly
                ? "AND EXISTS (SELECT 1 FROM MediaLocations lp WHERE lp.MediaItemId = i.Id AND lp.IsPresent = 1)"
                : query.HistoricalOnly
                    ? "AND EXISTS (SELECT 1 FROM MediaLocations lh WHERE lh.MediaItemId = i.Id AND lh.IsPresent = 0)"
                    : string.Empty;

            var searchPredicate = search is null
                ? string.Empty
                : """
                  AND (
                      i.Id LIKE $search ESCAPE '~'
                      OR i.Sha256 LIKE $search ESCAPE '~'
                      OR EXISTS (
                          SELECT 1 FROM MediaLocations ls
                          WHERE ls.MediaItemId = i.Id
                            AND (ls.RelativePath LIKE $search ESCAPE '~'
                                 OR ls.EndpointId LIKE $search ESCAPE '~')
                      )
                  )
                  """;

            command.CommandText = $"""
                SELECT i.Id, i.Sha256, i.Length, i.MediaType, i.FirstSeenUtc, i.LastSeenUtc,
                       (
                           SELECT l.EndpointId
                           FROM MediaLocations l
                           WHERE l.MediaItemId = i.Id
                           ORDER BY l.IsPresent DESC, l.LastSeenUtc DESC, l.Id DESC
                           LIMIT 1
                       ) AS RepresentativeEndpointId,
                       (
                           SELECT l.RelativePath
                           FROM MediaLocations l
                           WHERE l.MediaItemId = i.Id
                           ORDER BY l.IsPresent DESC, l.LastSeenUtc DESC, l.Id DESC
                           LIMIT 1
                       ) AS RepresentativeRelativePath,
                       COALESCE((
                           SELECT l.IsPresent
                           FROM MediaLocations l
                           WHERE l.MediaItemId = i.Id
                           ORDER BY l.IsPresent DESC, l.LastSeenUtc DESC, l.Id DESC
                           LIMIT 1
                       ), 0) AS RepresentativeIsPresent,
                       (SELECT COUNT(*) FROM MediaLocations l WHERE l.MediaItemId = i.Id AND l.IsPresent = 1) AS PresentLocationCount,
                       (SELECT COUNT(*) FROM MediaLocations l WHERE l.MediaItemId = i.Id AND l.IsPresent = 0) AS HistoricalLocationCount
                FROM MediaItems i
                WHERE 1 = 1
                {presencePredicate}
                {searchPredicate}
                ORDER BY i.LastSeenUtc DESC, i.Id
                LIMIT $limit;
                """;

            command.Parameters.AddWithValue("$limit", limit);
            if (search is not null)
            {
                command.Parameters.AddWithValue("$search", "%" + EscapeLike(search) + "%");
            }

            var result = new List<MediaIndexSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new MediaIndexItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    (MediaFileType)reader.GetInt32(3),
                    DateTimeOffset.Parse(reader.GetString(4)),
                    DateTimeOffset.Parse(reader.GetString(5)));

                result.Add(new MediaIndexSearchResult(
                    item,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt64(8) != 0,
                    Convert.ToInt32(reader.GetInt64(9)),
                    Convert.ToInt32(reader.GetInt64(10))));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IndexedFileState?> TryGetCurrentFileStateAsync(
        string endpointId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT l.Id, l.MediaItemId, l.Length, l.LastWriteTimeUtc, i.Sha256
                FROM MediaLocations l
                INNER JOIN MediaItems i ON i.Id = l.MediaItemId
                WHERE l.EndpointId = $endpointId
                  AND l.RelativePath = $relativePath
                  AND l.IsPresent = 1
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$endpointId", endpointId);
            command.Parameters.AddWithValue("$relativePath", relativePath);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new IndexedFileState(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetInt64(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetString(4));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UpsertIndexResult> UpsertFileAsync(
        string endpointId,
        StorageEndpointKind endpointKind,
        string volumeId,
        string relativePath,
        MediaFileType mediaType,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        string sha256,
        string scanId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            var (itemId, newItem) = await GetOrCreateItemAsync(
                connection,
                transaction,
                sha256,
                length,
                mediaType,
                nowUtc,
                cancellationToken);

            var existing = await GetCurrentLocationAsync(
                connection,
                transaction,
                endpointId,
                relativePath,
                cancellationToken);

            var newLocation = false;
            if (existing is not null && string.Equals(existing.MediaItemId, itemId, StringComparison.Ordinal))
            {
                await TouchLocationAsync(
                    connection,
                    transaction,
                    existing.Id,
                    volumeId,
                    endpointKind,
                    length,
                    lastWriteTimeUtc,
                    scanId,
                    nowUtc,
                    cancellationToken);
            }
            else
            {
                if (existing is not null)
                {
                    await MarkLocationMissingAsync(
                        connection,
                        transaction,
                        existing.Id,
                        nowUtc,
                        cancellationToken);
                }

                await InsertLocationAsync(
                    connection,
                    transaction,
                    itemId,
                    endpointId,
                    endpointKind,
                    volumeId,
                    relativePath,
                    length,
                    lastWriteTimeUtc,
                    scanId,
                    nowUtc,
                    cancellationToken);
                newLocation = true;
            }

            transaction.Commit();
            return new UpsertIndexResult(itemId, newItem, newLocation);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TouchExistingLocationAsync(
        long locationId,
        StorageEndpointKind endpointKind,
        string volumeId,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        string scanId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE MediaLocations
                SET EndpointKind = $endpointKind,
                    VolumeId = $volumeId,
                    Length = $length,
                    LastWriteTimeUtc = $lastWrite,
                    LastSeenScanId = $scanId,
                    LastSeenUtc = $now,
                    IsPresent = 1
                WHERE Id = $id;

                UPDATE MediaItems
                SET LastSeenUtc = $now
                WHERE Id = (SELECT MediaItemId FROM MediaLocations WHERE Id = $id);
                """;
            command.Parameters.AddWithValue("$endpointKind", (int)endpointKind);
            command.Parameters.AddWithValue("$volumeId", volumeId);
            command.Parameters.AddWithValue("$length", length);
            command.Parameters.AddWithValue("$lastWrite", lastWriteTimeUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$scanId", scanId);
            command.Parameters.AddWithValue("$now", nowUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$id", locationId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MissingIndexLocation>> MarkUnseenLocationsMissingAsync(
        string endpointId,
        string scanId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            using var transaction = connection.BeginTransaction();

            var missing = new List<MissingIndexLocation>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = """
                    SELECT Id, MediaItemId, EndpointId, RelativePath
                    FROM MediaLocations
                    WHERE EndpointId = $endpointId
                      AND IsPresent = 1
                      AND (LastSeenScanId IS NULL OR LastSeenScanId <> $scanId)
                    ORDER BY Id;
                    """;
                select.Parameters.AddWithValue("$endpointId", endpointId);
                select.Parameters.AddWithValue("$scanId", scanId);

                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    missing.Add(new MissingIndexLocation(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3)));
                }
            }

            if (missing.Count > 0)
            {
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE MediaLocations
                    SET IsPresent = 0
                    WHERE EndpointId = $endpointId
                      AND IsPresent = 1
                      AND (LastSeenScanId IS NULL OR LastSeenScanId <> $scanId);
                    """;
                update.Parameters.AddWithValue("$endpointId", endpointId);
                update.Parameters.AddWithValue("$scanId", scanId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();
            return missing;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS MediaItems (
                Id TEXT PRIMARY KEY NOT NULL,
                Sha256 TEXT NOT NULL,
                Length INTEGER NOT NULL,
                MediaType INTEGER NOT NULL,
                FirstSeenUtc TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_MediaItems_Content
                ON MediaItems (Sha256, Length);

            CREATE TABLE IF NOT EXISTS MediaLocations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MediaItemId TEXT NOT NULL,
                EndpointId TEXT NOT NULL,
                EndpointKind INTEGER NOT NULL,
                VolumeId TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                Length INTEGER NOT NULL,
                LastWriteTimeUtc TEXT NOT NULL,
                IsPresent INTEGER NOT NULL,
                FirstSeenUtc TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL,
                LastSeenScanId TEXT NULL,
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_MediaLocations_CurrentPath
                ON MediaLocations (EndpointId, RelativePath)
                WHERE IsPresent = 1;

            CREATE INDEX IF NOT EXISTS IX_MediaLocations_Item
                ON MediaLocations (MediaItemId, IsPresent);

            CREATE INDEX IF NOT EXISTS IX_MediaLocations_Endpoint
                ON MediaLocations (EndpointId, IsPresent);

            PRAGMA user_version = 1;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        _initialized = true;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task<(string Id, bool IsNew)> GetOrCreateItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sha256,
        long length,
        MediaFileType mediaType,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT Id FROM MediaItems WHERE Sha256 = $sha256 AND Length = $length LIMIT 1;";
        lookup.Parameters.AddWithValue("$sha256", sha256);
        lookup.Parameters.AddWithValue("$length", length);
        var existing = await lookup.ExecuteScalarAsync(cancellationToken);
        if (existing is string id)
        {
            await using var touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE MediaItems SET MediaType = $mediaType, LastSeenUtc = $now WHERE Id = $id;";
            touch.Parameters.AddWithValue("$mediaType", (int)mediaType);
            touch.Parameters.AddWithValue("$now", nowUtc.UtcDateTime.ToString("O"));
            touch.Parameters.AddWithValue("$id", id);
            await touch.ExecuteNonQueryAsync(cancellationToken);
            return (id, false);
        }

        var newId = Guid.NewGuid().ToString("N");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO MediaItems (Id, Sha256, Length, MediaType, FirstSeenUtc, LastSeenUtc)
            VALUES ($id, $sha256, $length, $mediaType, $now, $now);
            """;
        insert.Parameters.AddWithValue("$id", newId);
        insert.Parameters.AddWithValue("$sha256", sha256);
        insert.Parameters.AddWithValue("$length", length);
        insert.Parameters.AddWithValue("$mediaType", (int)mediaType);
        insert.Parameters.AddWithValue("$now", nowUtc.UtcDateTime.ToString("O"));
        await insert.ExecuteNonQueryAsync(cancellationToken);
        return (newId, true);
    }

    private static async Task<CurrentLocation?> GetCurrentLocationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string endpointId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, MediaItemId
            FROM MediaLocations
            WHERE EndpointId = $endpointId AND RelativePath = $relativePath AND IsPresent = 1
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$endpointId", endpointId);
        command.Parameters.AddWithValue("$relativePath", relativePath);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CurrentLocation(reader.GetInt64(0), reader.GetString(1))
            : null;
    }

    private static async Task TouchLocationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id,
        string volumeId,
        StorageEndpointKind endpointKind,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        string scanId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE MediaLocations
            SET EndpointKind = $endpointKind,
                VolumeId = $volumeId,
                Length = $length,
                LastWriteTimeUtc = $lastWrite,
                LastSeenScanId = $scanId,
                LastSeenUtc = $now,
                IsPresent = 1
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$endpointKind", (int)endpointKind);
        command.Parameters.AddWithValue("$volumeId", volumeId);
        command.Parameters.AddWithValue("$length", length);
        command.Parameters.AddWithValue("$lastWrite", lastWriteTimeUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$scanId", scanId);
        command.Parameters.AddWithValue("$now", nowUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLocationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string mediaItemId,
        string endpointId,
        StorageEndpointKind endpointKind,
        string volumeId,
        string relativePath,
        long length,
        DateTimeOffset lastWriteTimeUtc,
        string scanId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO MediaLocations (
                MediaItemId, EndpointId, EndpointKind, VolumeId, RelativePath,
                Length, LastWriteTimeUtc, IsPresent, FirstSeenUtc, LastSeenUtc, LastSeenScanId)
            VALUES (
                $mediaItemId, $endpointId, $endpointKind, $volumeId, $relativePath,
                $length, $lastWrite, 1, $now, $now, $scanId);
            """;
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId);
        command.Parameters.AddWithValue("$endpointId", endpointId);
        command.Parameters.AddWithValue("$endpointKind", (int)endpointKind);
        command.Parameters.AddWithValue("$volumeId", volumeId);
        command.Parameters.AddWithValue("$relativePath", relativePath);
        command.Parameters.AddWithValue("$length", length);
        command.Parameters.AddWithValue("$lastWrite", lastWriteTimeUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$now", nowUtc.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$scanId", scanId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkLocationMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long id,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE MediaLocations SET IsPresent = 0 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static string EscapeLike(string value) =>
        value.Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal);

    private static MediaIndexItem ReadItem(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt64(2),
        (MediaFileType)reader.GetInt32(3),
        DateTimeOffset.Parse(reader.GetString(4)),
        DateTimeOffset.Parse(reader.GetString(5)));

    private static MediaIndexLocation ReadLocation(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        (StorageEndpointKind)reader.GetInt32(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetInt64(6),
        DateTimeOffset.Parse(reader.GetString(7)),
        reader.GetInt64(8) != 0,
        DateTimeOffset.Parse(reader.GetString(9)),
        DateTimeOffset.Parse(reader.GetString(10)));

    private sealed record CurrentLocation(long Id, string MediaItemId);
}

internal sealed record IndexedFileState(
    long LocationId,
    string MediaItemId,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

internal sealed record UpsertIndexResult(
    string MediaItemId,
    bool IsNewItem,
    bool IsNewLocation);

internal sealed record MissingIndexLocation(
    long LocationId,
    string MediaItemId,
    string EndpointId,
    string RelativePath);
