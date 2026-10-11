using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.MediaIndex.Collections;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using System.Text.Json;
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

    public async Task<MediaIndexItem> EnsureItemAsync(
        string sha256,
        long length,
        MediaFileType mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var (id, _) = await GetOrCreateItemAsync(
                connection,
                (SqliteTransaction)transaction,
                sha256,
                length,
                mediaType,
                now,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new MediaIndexItem(id, sha256, length, mediaType, now, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateIdentityAfterControlledRewriteAsync(
        string mediaItemId,
        string expectedOldSha256,
        long expectedOldLength,
        string newSha256,
        long newLength,
        DateTime newLastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOldSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(newSha256);
        if (expectedOldLength < 0 || newLength < 0) throw new ArgumentOutOfRangeException(nameof(newLength));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sqliteTransaction = (SqliteTransaction)transaction;

            await using (var verify = connection.CreateCommand())
            {
                verify.Transaction = sqliteTransaction;
                verify.CommandText = "SELECT Sha256, Length FROM MediaItems WHERE Id = $id LIMIT 1;";
                verify.Parameters.AddWithValue("$id", mediaItemId);
                await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)
                    || !string.Equals(reader.GetString(0), expectedOldSha256, StringComparison.OrdinalIgnoreCase)
                    || reader.GetInt64(1) != expectedOldLength)
                {
                    throw new InvalidOperationException("Media-ID hat sich seit dem Export-Preflight verändert.");
                }
            }

            await using (var collision = connection.CreateCommand())
            {
                collision.Transaction = sqliteTransaction;
                collision.CommandText = "SELECT Id FROM MediaItems WHERE Sha256 = $sha AND Length = $length AND Id <> $id LIMIT 1;";
                collision.Parameters.AddWithValue("$sha", newSha256);
                collision.Parameters.AddWithValue("$length", newLength);
                collision.Parameters.AddWithValue("$id", mediaItemId);
                if (await collision.ExecuteScalarAsync(cancellationToken) is string otherId)
                    throw new InvalidOperationException($"Der neue Dateiinhalt ist bereits einer anderen Media-ID zugeordnet ({otherId}).");
            }

            long? currentLocationId = null;
            await using (var locations = connection.CreateCommand())
            {
                locations.Transaction = sqliteTransaction;
                locations.CommandText = "SELECT Id FROM MediaLocations WHERE MediaItemId = $id AND IsPresent = 1 ORDER BY Id;";
                locations.Parameters.AddWithValue("$id", mediaItemId);
                await using var reader = await locations.ExecuteReaderAsync(cancellationToken);
                var ids = new List<long>();
                while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetInt64(0));
                if (ids.Count > 1)
                    throw new InvalidOperationException("Direktes Metadaten-Schreiben ist bei mehreren aktuellen Kopien derselben Media-ID in 0060 gesperrt.");
                if (ids.Count == 1) currentLocationId = ids[0];
            }

            var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
            await using (var updateItem = connection.CreateCommand())
            {
                updateItem.Transaction = sqliteTransaction;
                updateItem.CommandText = "UPDATE MediaItems SET Sha256 = $sha, Length = $length, LastSeenUtc = $now WHERE Id = $id;";
                updateItem.Parameters.AddWithValue("$sha", newSha256);
                updateItem.Parameters.AddWithValue("$length", newLength);
                updateItem.Parameters.AddWithValue("$now", now);
                updateItem.Parameters.AddWithValue("$id", mediaItemId);
                if (await updateItem.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Media-ID konnte nach dem kontrollierten Rewrite nicht aktualisiert werden.");
            }

            if (currentLocationId.HasValue)
            {
                await using var updateLocation = connection.CreateCommand();
                updateLocation.Transaction = sqliteTransaction;
                updateLocation.CommandText = "UPDATE MediaLocations SET Length = $length, LastWriteTimeUtc = $write, LastSeenUtc = $now WHERE Id = $locationId;";
                updateLocation.Parameters.AddWithValue("$length", newLength);
                updateLocation.Parameters.AddWithValue("$write", newLastWriteTimeUtc.ToString("O"));
                updateLocation.Parameters.AddWithValue("$now", now);
                updateLocation.Parameters.AddWithValue("$locationId", currentLocationId.Value);
                await updateLocation.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaMetadataSnapshot> GetMetadataAsync(
        string mediaItemId,
        CancellationToken cancellationToken)
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
                SELECT MediaItemId,
                       CaptureDateOverridden, CaptureDate,
                       TitleOverridden, Title,
                       DescriptionOverridden, Description,
                       GpsMode, Latitude, Longitude, UpdatedUtc
                FROM MediaMetadata
                WHERE MediaItemId = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return new MediaMetadataSnapshot(
                    mediaItemId,
                    false,
                    false, null,
                    false, null,
                    false, null,
                    MetadataGpsMode.Inherit,
                    null, null, null);
            }

            return ReadMetadata(reader);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveMetadataBatchAsync(
        IReadOnlyList<MediaMetadataUpdate> updates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (updates.Count == 0)
        {
            return;
        }

        var duplicateId = updates.GroupBy(x => x.MediaItemId, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new InvalidOperationException($"MediaItem '{duplicateId}' ist im Metadaten-Batch mehrfach enthalten.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sqliteTransaction = (SqliteTransaction)transaction;
            var now = DateTimeOffset.UtcNow;

            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ArgumentException.ThrowIfNullOrWhiteSpace(update.MediaItemId);

                await using var existence = connection.CreateCommand();
                existence.Transaction = sqliteTransaction;
                existence.CommandText = "SELECT COUNT(*) FROM MediaItems WHERE Id = $id;";
                existence.Parameters.AddWithValue("$id", update.MediaItemId);
                var exists = Convert.ToInt64(await existence.ExecuteScalarAsync(cancellationToken)) == 1;
                if (!exists)
                {
                    throw new InvalidOperationException($"MediaItem '{update.MediaItemId}' existiert nicht im Media Index.");
                }

                var previous = await GetMetadataForHistoryAsync(connection, sqliteTransaction, update.MediaItemId, cancellationToken);

                await using var command = connection.CreateCommand();
                command.Transaction = sqliteTransaction;
                command.CommandText = """
                    INSERT INTO MediaMetadata (
                        MediaItemId,
                        CaptureDateOverridden, CaptureDate,
                        TitleOverridden, Title,
                        DescriptionOverridden, Description,
                        GpsMode, Latitude, Longitude,
                        UpdatedUtc, Revision)
                    VALUES (
                        $id,
                        $captureOverridden, $captureDate,
                        $titleOverridden, $title,
                        $descriptionOverridden, $description,
                        $gpsMode, $latitude, $longitude,
                        $updatedUtc, 1)
                    ON CONFLICT(MediaItemId) DO UPDATE SET
                        CaptureDateOverridden = excluded.CaptureDateOverridden,
                        CaptureDate = excluded.CaptureDate,
                        TitleOverridden = excluded.TitleOverridden,
                        Title = excluded.Title,
                        DescriptionOverridden = excluded.DescriptionOverridden,
                        Description = excluded.Description,
                        GpsMode = excluded.GpsMode,
                        Latitude = excluded.Latitude,
                        Longitude = excluded.Longitude,
                        UpdatedUtc = excluded.UpdatedUtc,
                        Revision = MediaMetadata.Revision + 1;
                    """;
                command.Parameters.AddWithValue("$id", update.MediaItemId);
                command.Parameters.AddWithValue("$captureOverridden", update.CaptureDateOverridden ? 1 : 0);
                command.Parameters.AddWithValue("$captureDate", (object?)update.CaptureDate?.ToString("O") ?? DBNull.Value);
                command.Parameters.AddWithValue("$titleOverridden", update.TitleOverridden ? 1 : 0);
                command.Parameters.AddWithValue("$title", (object?)update.Title ?? DBNull.Value);
                command.Parameters.AddWithValue("$descriptionOverridden", update.DescriptionOverridden ? 1 : 0);
                command.Parameters.AddWithValue("$description", (object?)update.Description ?? DBNull.Value);
                command.Parameters.AddWithValue("$gpsMode", (int)update.GpsMode);
                command.Parameters.AddWithValue("$latitude", (object?)update.Latitude ?? DBNull.Value);
                command.Parameters.AddWithValue("$longitude", (object?)update.Longitude ?? DBNull.Value);
                command.Parameters.AddWithValue("$updatedUtc", now.UtcDateTime.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);

                var currentJson = JsonSerializer.Serialize(update);
                await using var history = connection.CreateCommand();
                history.Transaction = sqliteTransaction;
                history.CommandText = """
                    INSERT INTO MediaMetadataHistory (ChangeId, MediaItemId, ChangedUtc, PreviousJson, CurrentJson)
                    VALUES ($changeId, $id, $changedUtc, $previousJson, $currentJson);
                    """;
                history.Parameters.AddWithValue("$changeId", Guid.NewGuid().ToString("N"));
                history.Parameters.AddWithValue("$id", update.MediaItemId);
                history.Parameters.AddWithValue("$changedUtc", now.UtcDateTime.ToString("O"));
                history.Parameters.AddWithValue("$previousJson", (object?)previous ?? DBNull.Value);
                history.Parameters.AddWithValue("$currentJson", currentJson);
                await history.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> HasFactsAsync(string mediaItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM MediaFacts WHERE MediaItemId = $id;";
            command.Parameters.AddWithValue("$id", mediaItemId);
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpsertFactsAsync(
        string mediaItemId,
        ImageMetadata? metadata,
        DateTimeOffset sourceLastWriteTimeUtc,
        CancellationToken cancellationToken)
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
                INSERT INTO MediaFacts (
                    MediaItemId, CaptureDate, CameraMake, CameraModel,
                    Latitude, Longitude, PixelWidth, PixelHeight,
                    SourceLastWriteTimeUtc, UpdatedUtc)
                VALUES (
                    $id, $captureDate, $cameraMake, $cameraModel,
                    $latitude, $longitude, $pixelWidth, $pixelHeight,
                    $sourceLastWrite, $updated)
                ON CONFLICT(MediaItemId) DO UPDATE SET
                    CaptureDate = excluded.CaptureDate,
                    CameraMake = excluded.CameraMake,
                    CameraModel = excluded.CameraModel,
                    Latitude = excluded.Latitude,
                    Longitude = excluded.Longitude,
                    PixelWidth = excluded.PixelWidth,
                    PixelHeight = excluded.PixelHeight,
                    SourceLastWriteTimeUtc = excluded.SourceLastWriteTimeUtc,
                    UpdatedUtc = excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            command.Parameters.AddWithValue("$captureDate", (object?)GetBestCaptureDate(metadata)?.ToString("O") ?? DBNull.Value);
            command.Parameters.AddWithValue("$cameraMake", (object?)metadata?.CameraMake ?? DBNull.Value);
            command.Parameters.AddWithValue("$cameraModel", (object?)metadata?.CameraModel ?? DBNull.Value);
            command.Parameters.AddWithValue("$latitude", (object?)metadata?.Latitude ?? DBNull.Value);
            command.Parameters.AddWithValue("$longitude", (object?)metadata?.Longitude ?? DBNull.Value);
            command.Parameters.AddWithValue("$pixelWidth", (object?)metadata?.PixelWidth ?? DBNull.Value);
            command.Parameters.AddWithValue("$pixelHeight", (object?)metadata?.PixelHeight ?? DBNull.Value);
            command.Parameters.AddWithValue("$sourceLastWrite", sourceLastWriteTimeUtc.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaIndexFacts?> GetFactsAsync(string mediaItemId, CancellationToken cancellationToken)
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
                SELECT MediaItemId, CaptureDate, CameraMake, CameraModel, Latitude, Longitude,
                       PixelWidth, PixelHeight, SourceLastWriteTimeUtc, UpdatedUtc
                FROM MediaFacts WHERE MediaItemId = $id LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;

            DateTime? captureDate = null;
            if (!reader.IsDBNull(1) && DateTime.TryParse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                captureDate = parsed;

            return new MediaIndexFacts(
                reader.GetString(0),
                captureDate,
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                DateTimeOffset.Parse(reader.GetString(8)),
                DateTimeOffset.Parse(reader.GetString(9)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaKnowledgeItem>> GetKnowledgeItemsAsync(
        MediaKnowledgeKind kind,
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var safeLimit = Math.Clamp(limit, 1, 100);
        var search = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = search is null
                ? """
                  SELECT Id, Kind, Name, StartDate, EndDate, Latitude, Longitude, Notes,
                         IsPinned, UsageCount, LastUsedUtc, CreatedUtc, UpdatedUtc
                  FROM MetadataKnowledgeItems
                  WHERE Kind = $kind
                  ORDER BY IsPinned DESC, LastUsedUtc IS NULL, LastUsedUtc DESC, UsageCount DESC, Name COLLATE NOCASE
                  LIMIT $limit;
                  """
                : """
                  SELECT Id, Kind, Name, StartDate, EndDate, Latitude, Longitude, Notes,
                         IsPinned, UsageCount, LastUsedUtc, CreatedUtc, UpdatedUtc
                  FROM MetadataKnowledgeItems
                  WHERE Kind = $kind AND Name LIKE $search ESCAPE '~'
                  ORDER BY IsPinned DESC, UsageCount DESC, LastUsedUtc DESC, Name COLLATE NOCASE
                  LIMIT $limit;
                  """;
            command.Parameters.AddWithValue("$kind", (int)kind);
            command.Parameters.AddWithValue("$limit", safeLimit);
            if (search is not null) command.Parameters.AddWithValue("$search", "%" + EscapeLike(search) + "%");

            var result = new List<MediaKnowledgeItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(ReadKnowledgeItem(reader));
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaKnowledgeItem> EnsureKnowledgeItemAsync(
        MediaKnowledgeDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Name darf nicht leer sein.", nameof(draft));
        if (draft.EndDate.HasValue && draft.StartDate.HasValue && draft.EndDate.Value < draft.StartDate.Value)
            throw new ArgumentException("Enddatum darf nicht vor dem Startdatum liegen.", nameof(draft));
        if ((draft.Latitude.HasValue && (draft.Latitude.Value < -90 || draft.Latitude.Value > 90))
            || (draft.Longitude.HasValue && (draft.Longitude.Value < -180 || draft.Longitude.Value > 180)))
            throw new ArgumentException("Koordinaten liegen außerhalb des gültigen Bereichs.", nameof(draft));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            var normalized = NormalizeKnowledgeName(name);
            var now = DateTimeOffset.UtcNow;

            await using (var existing = connection.CreateCommand())
            {
                existing.CommandText = """
                    SELECT Id, Kind, Name, StartDate, EndDate, Latitude, Longitude, Notes,
                           IsPinned, UsageCount, LastUsedUtc, CreatedUtc, UpdatedUtc
                    FROM MetadataKnowledgeItems
                    WHERE Kind = $kind AND NormalizedName = $normalized
                    LIMIT 1;
                    """;
                existing.Parameters.AddWithValue("$kind", (int)draft.Kind);
                existing.Parameters.AddWithValue("$normalized", normalized);
                await using var reader = await existing.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken)) return ReadKnowledgeItem(reader);
            }

            var id = Guid.NewGuid().ToString("N");
            await using (var insert = connection.CreateCommand())
            {
                insert.CommandText = """
                    INSERT INTO MetadataKnowledgeItems (
                        Id, Kind, Name, NormalizedName, StartDate, EndDate,
                        Latitude, Longitude, Notes, IsPinned, UsageCount,
                        LastUsedUtc, CreatedUtc, UpdatedUtc)
                    VALUES (
                        $id, $kind, $name, $normalized, $startDate, $endDate,
                        $latitude, $longitude, $notes, $isPinned, 0,
                        NULL, $created, $updated);
                    """;
                insert.Parameters.AddWithValue("$id", id);
                insert.Parameters.AddWithValue("$kind", (int)draft.Kind);
                insert.Parameters.AddWithValue("$name", name);
                insert.Parameters.AddWithValue("$normalized", normalized);
                insert.Parameters.AddWithValue("$startDate", (object?)draft.StartDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
                insert.Parameters.AddWithValue("$endDate", (object?)draft.EndDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
                insert.Parameters.AddWithValue("$latitude", (object?)draft.Latitude ?? DBNull.Value);
                insert.Parameters.AddWithValue("$longitude", (object?)draft.Longitude ?? DBNull.Value);
                insert.Parameters.AddWithValue("$notes", (object?)draft.Notes ?? DBNull.Value);
                insert.Parameters.AddWithValue("$isPinned", draft.IsPinned ? 1 : 0);
                insert.Parameters.AddWithValue("$created", now.UtcDateTime.ToString("O"));
                insert.Parameters.AddWithValue("$updated", now.UtcDateTime.ToString("O"));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            return new MediaKnowledgeItem(id, draft.Kind, name, draft.StartDate, draft.EndDate,
                draft.Latitude, draft.Longitude, draft.Notes, draft.IsPinned, 0, null, now, now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaKnowledgeSnapshot> GetKnowledgeAsync(string mediaItemId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var rating = 0;
            await using (var ratingCommand = connection.CreateCommand())
            {
                ratingCommand.CommandText = "SELECT Rating FROM MediaRatings WHERE MediaItemId = $id LIMIT 1;";
                ratingCommand.Parameters.AddWithValue("$id", mediaItemId);
                var value = await ratingCommand.ExecuteScalarAsync(cancellationToken);
                if (value is not null && value != DBNull.Value) rating = Convert.ToInt32(value);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT k.Id, k.Kind, k.Name, k.StartDate, k.EndDate, k.Latitude, k.Longitude, k.Notes,
                       k.IsPinned, k.UsageCount, k.LastUsedUtc, k.CreatedUtc, k.UpdatedUtc
                FROM MediaKnowledgeAssignments a
                INNER JOIN MetadataKnowledgeItems k ON k.Id = a.KnowledgeItemId
                WHERE a.MediaItemId = $id
                ORDER BY k.Kind, k.Name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$id", mediaItemId);
            var items = new List<MediaKnowledgeItem>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) items.Add(ReadKnowledgeItem(reader));
            return new MediaKnowledgeSnapshot(mediaItemId, rating, items);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> AssignKnowledgeItemBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        string knowledgeItemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeItemId);
        var ids = mediaItemIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var tx = (SqliteTransaction)transaction;
            var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
            var added = 0;

            foreach (var mediaId in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = """
                    INSERT OR IGNORE INTO MediaKnowledgeAssignments (MediaItemId, KnowledgeItemId, AssignedUtc)
                    SELECT $mediaId, $knowledgeId, $now
                    WHERE EXISTS (SELECT 1 FROM MediaItems WHERE Id = $mediaId)
                      AND EXISTS (SELECT 1 FROM MetadataKnowledgeItems WHERE Id = $knowledgeId);
                    """;
                command.Parameters.AddWithValue("$mediaId", mediaId);
                command.Parameters.AddWithValue("$knowledgeId", knowledgeItemId);
                command.Parameters.AddWithValue("$now", now);
                added += await command.ExecuteNonQueryAsync(cancellationToken);
            }

            if (added > 0)
            {
                await using var touch = connection.CreateCommand();
                touch.Transaction = tx;
                touch.CommandText = """
                    UPDATE MetadataKnowledgeItems
                    SET UsageCount = UsageCount + $added,
                        LastUsedUtc = $now,
                        UpdatedUtc = $now
                    WHERE Id = $id;
                    """;
                touch.Parameters.AddWithValue("$added", added);
                touch.Parameters.AddWithValue("$now", now);
                touch.Parameters.AddWithValue("$id", knowledgeItemId);
                await touch.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return added;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RemoveKnowledgeItemBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        string knowledgeItemId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(knowledgeItemId);
        var ids = mediaItemIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return 0;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var tx = (SqliteTransaction)transaction;
            var removed = 0;
            foreach (var mediaId in ids)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                command.CommandText = "DELETE FROM MediaKnowledgeAssignments WHERE MediaItemId = $mediaId AND KnowledgeItemId = $knowledgeId;";
                command.Parameters.AddWithValue("$mediaId", mediaId);
                command.Parameters.AddWithValue("$knowledgeId", knowledgeItemId);
                removed += await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetRatingBatchAsync(
        IReadOnlyList<string> mediaItemIds,
        int rating,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        if (rating is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(rating), "Bewertung muss zwischen 0 und 3 liegen.");
        var ids = mediaItemIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var tx = (SqliteTransaction)transaction;
            var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("O");
            foreach (var mediaId in ids)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = tx;
                if (rating == 0)
                {
                    command.CommandText = "DELETE FROM MediaRatings WHERE MediaItemId = $id;";
                }
                else
                {
                    command.CommandText = """
                        INSERT INTO MediaRatings (MediaItemId, Rating, UpdatedUtc)
                        SELECT $id, $rating, $now
                        WHERE EXISTS (SELECT 1 FROM MediaItems WHERE Id = $id)
                        ON CONFLICT(MediaItemId) DO UPDATE SET Rating = excluded.Rating, UpdatedUtc = excluded.UpdatedUtc;
                        """;
                    command.Parameters.AddWithValue("$rating", rating);
                    command.Parameters.AddWithValue("$now", now);
                }
                command.Parameters.AddWithValue("$id", mediaId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
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

    public async Task<IReadOnlyList<MediaIndexFolderFacet>> GetFolderFacetsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EndpointId, RelativePath FROM MediaLocations WHERE IsPresent = 1 ORDER BY EndpointId, RelativePath;";

            var counts = new Dictionary<(string EndpointId, string Folder), HashSet<string>>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var endpointId = reader.GetString(0);
                var relativePath = reader.GetString(1);
                var rootKey = (endpointId, string.Empty);
                if (!counts.TryGetValue(rootKey, out var rootMedia))
                {
                    rootMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    counts[rootKey] = rootMedia;
                }
                rootMedia.Add(relativePath);

                var normalized = relativePath.Replace('\\', '/');
                var slash = normalized.LastIndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }

                var folder = normalized[..slash].Trim('/');
                var parts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = string.Empty;
                foreach (var part in parts)
                {
                    current = current.Length == 0 ? part : current + "/" + part;
                    var key = (endpointId, current);
                    if (!counts.TryGetValue(key, out var media))
                    {
                        media = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        counts[key] = media;
                    }
                    media.Add(relativePath);
                }
            }

            return counts
                .Select(pair => new MediaIndexFolderFacet(pair.Key.EndpointId, pair.Key.Folder, pair.Value.Count))
                .OrderBy(item => item.EndpointId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativeFolder, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaIndexFolderNode>> GetFolderChildrenAsync(
        string endpointId,
        string? parentRelativeFolder,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        var parent = string.IsNullOrWhiteSpace(parentRelativeFolder)
            ? string.Empty
            : parentRelativeFolder.Replace('\\', '/').Trim('/');
        var prefix = parent.Length == 0 ? string.Empty : parent + "/";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = parent.Length == 0
                ? "SELECT RelativePath FROM MediaLocations WHERE IsPresent = 1 AND EndpointId = $endpoint ORDER BY RelativePath;"
                : "SELECT RelativePath FROM MediaLocations WHERE IsPresent = 1 AND EndpointId = $endpoint AND RelativePath LIKE $prefix ESCAPE '\\' ORDER BY RelativePath;";
            command.Parameters.AddWithValue("$endpoint", endpointId);
            if (parent.Length != 0)
            {
                command.Parameters.AddWithValue("$prefix", EscapeLikePattern(prefix) + "%");
            }

            var folders = new Dictionary<string, FolderAccumulator>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var relativePath = reader.GetString(0).Replace('\\', '/').TrimStart('/');
                if (prefix.Length != 0 && !relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var remainder = prefix.Length == 0 ? relativePath : relativePath[prefix.Length..];
                var slash = remainder.IndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }

                var childName = remainder[..slash];
                var childRelativeFolder = prefix + childName;
                if (!folders.TryGetValue(childRelativeFolder, out var accumulator))
                {
                    accumulator = new FolderAccumulator(childName);
                    folders[childRelativeFolder] = accumulator;
                }

                accumulator.Paths.Add(relativePath);
                var tail = remainder[(slash + 1)..];
                if (tail.Contains('/'))
                {
                    accumulator.HasChildren = true;
                }
            }

            return folders
                .Select(pair => new MediaIndexFolderNode(
                    endpointId,
                    pair.Key,
                    pair.Value.Name,
                    pair.Value.Paths.Count,
                    pair.Value.HasChildren))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
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
        var offset = Math.Max(0, query.Offset);
        var search = string.IsNullOrWhiteSpace(query.SearchText)
            ? null
            : query.SearchText.Trim();
        var knowledgeIds = (query.KnowledgeItemIds ?? Array.Empty<string>())
            .Concat(string.IsNullOrWhiteSpace(query.KnowledgeItemId) ? Array.Empty<string>() : new[] { query.KnowledgeItemId! })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

            var typePredicate = query.MediaType.HasValue
                ? "AND i.MediaType = $mediaType"
                : string.Empty;

            var locationPredicate = string.IsNullOrWhiteSpace(query.EndpointId)
                ? string.Empty
                : string.IsNullOrWhiteSpace(query.RelativeFolder)
                    ? "AND EXISTS (SELECT 1 FROM MediaLocations lf WHERE lf.MediaItemId = i.Id AND lf.IsPresent = 1 AND lf.EndpointId = $endpointId)"
                    : "AND EXISTS (SELECT 1 FROM MediaLocations lf WHERE lf.MediaItemId = i.Id AND lf.IsPresent = 1 AND lf.EndpointId = $endpointId AND (REPLACE(lf.RelativePath, '\\', '/') LIKE $folderPrefix ESCAPE '~'))";

            var datePredicate = query.CaptureDateFrom.HasValue || query.CaptureDateTo.HasValue
                ? """
                  AND COALESCE(
                        (SELECT m.CaptureDate FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.CaptureDateOverridden = 1),
                        (SELECT f.CaptureDate FROM MediaFacts f WHERE f.MediaItemId = i.Id)
                      ) IS NOT NULL
                  """
                  + (query.CaptureDateFrom.HasValue
                      ? " AND COALESCE((SELECT m.CaptureDate FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.CaptureDateOverridden = 1), (SELECT f.CaptureDate FROM MediaFacts f WHERE f.MediaItemId = i.Id)) >= $captureFrom"
                      : string.Empty)
                  + (query.CaptureDateTo.HasValue
                      ? " AND COALESCE((SELECT m.CaptureDate FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.CaptureDateOverridden = 1), (SELECT f.CaptureDate FROM MediaFacts f WHERE f.MediaItemId = i.Id)) <= $captureTo"
                      : string.Empty)
                : string.Empty;

            var ratingPredicate = query.MinimumRating.HasValue
                ? "AND COALESCE((SELECT r.Rating FROM MediaRatings r WHERE r.MediaItemId = i.Id), 0) >= $minimumRating"
                : string.Empty;

            var knowledgePredicate = knowledgeIds.Length == 0
                ? string.Empty
                : string.Join("\n", knowledgeIds.Select((_, index) =>
                    $"AND EXISTS (SELECT 1 FROM MediaKnowledgeAssignments ka{index} WHERE ka{index}.MediaItemId = i.Id AND ka{index}.KnowledgeItemId = $knowledgeItemId{index})"));

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
                      OR EXISTS (
                          SELECT 1 FROM MediaMetadata ms
                          WHERE ms.MediaItemId = i.Id
                            AND ((ms.TitleOverridden = 1 AND ms.Title LIKE $search ESCAPE '~')
                                 OR (ms.DescriptionOverridden = 1 AND ms.Description LIKE $search ESCAPE '~'))
                      )
                      OR EXISTS (
                          SELECT 1
                          FROM MediaKnowledgeAssignments kas
                          INNER JOIN MetadataKnowledgeItems ks ON ks.Id = kas.KnowledgeItemId
                          WHERE kas.MediaItemId = i.Id AND ks.Name LIKE $search ESCAPE '~'
                      )
                  )
                  """;

            var orderBy = query.SortOrder switch
            {
                MediaIndexSortOrder.FirstSeenDescending => "i.FirstSeenUtc DESC, i.Id",
                MediaIndexSortOrder.FileNameAscending => "RepresentativeRelativePath COLLATE NOCASE ASC, i.Id",
                MediaIndexSortOrder.FileNameDescending => "RepresentativeRelativePath COLLATE NOCASE DESC, i.Id",
                MediaIndexSortOrder.SizeDescending => "i.Length DESC, i.Id",
                MediaIndexSortOrder.SizeAscending => "i.Length ASC, i.Id",
                MediaIndexSortOrder.CaptureDateDescending => "EffectiveCaptureDate DESC, i.Id",
                MediaIndexSortOrder.CaptureDateAscending => "EffectiveCaptureDate ASC, i.Id",
                _ => "i.LastSeenUtc DESC, i.Id"
            };

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
                       (SELECT COUNT(*) FROM MediaLocations l WHERE l.MediaItemId = i.Id AND l.IsPresent = 0) AS HistoricalLocationCount,
                       (SELECT m.Title FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.TitleOverridden = 1) AS MetadataTitle,
                       COALESCE(
                           (SELECT m.CaptureDate FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.CaptureDateOverridden = 1),
                           (SELECT f.CaptureDate FROM MediaFacts f WHERE f.MediaItemId = i.Id)
                       ) AS EffectiveCaptureDate,
                       (SELECT m.Description FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.DescriptionOverridden = 1) AS MetadataDescription,
                       COALESCE((SELECT r.Rating FROM MediaRatings r WHERE r.MediaItemId = i.Id), 0) AS Rating
                FROM MediaItems i
                WHERE 1 = 1
                {presencePredicate}
                {typePredicate}
                {locationPredicate}
                {datePredicate}
                {ratingPredicate}
                {knowledgePredicate}
                {searchPredicate}
                ORDER BY {orderBy}
                LIMIT $limit OFFSET $offset;
                """;

            command.Parameters.AddWithValue("$limit", limit);
            command.Parameters.AddWithValue("$offset", offset);
            if (query.MediaType.HasValue)
            {
                command.Parameters.AddWithValue("$mediaType", (int)query.MediaType.Value);
            }

            if (!string.IsNullOrWhiteSpace(query.EndpointId))
            {
                command.Parameters.AddWithValue("$endpointId", query.EndpointId.Trim());
                if (!string.IsNullOrWhiteSpace(query.RelativeFolder))
                {
                    var normalizedFolder = query.RelativeFolder.Replace('\\', '/').Trim('/');
                    command.Parameters.AddWithValue("$folderPrefix", EscapeLike(normalizedFolder) + "/%");
                }
            }

            if (query.CaptureDateFrom.HasValue)
                command.Parameters.AddWithValue("$captureFrom", query.CaptureDateFrom.Value.ToString("O"));
            if (query.CaptureDateTo.HasValue)
                command.Parameters.AddWithValue("$captureTo", query.CaptureDateTo.Value.ToString("O"));
            if (query.MinimumRating.HasValue)
                command.Parameters.AddWithValue("$minimumRating", Math.Clamp(query.MinimumRating.Value, 0, 3));
            for (var index = 0; index < knowledgeIds.Length; index++)
                command.Parameters.AddWithValue($"$knowledgeItemId{index}", knowledgeIds[index]);

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

                DateTime? captureDate = null;
                if (!reader.IsDBNull(12)
                    && DateTime.TryParse(
                        reader.GetString(12),
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var parsedCaptureDate))
                {
                    captureDate = parsedCaptureDate;
                }

                result.Add(new MediaIndexSearchResult(
                    item,
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetInt64(8) != 0,
                    Convert.ToInt32(reader.GetInt64(9)),
                    Convert.ToInt32(reader.GetInt64(10)),
                    reader.IsDBNull(11) ? null : reader.GetString(11),
                    captureDate,
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? 0 : reader.GetInt32(14)));
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


    public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineYearsAsync(CancellationToken cancellationToken) =>
        GetTimelineFacetsAsync(null, null, cancellationToken);

    public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineMonthsAsync(
        int year,
        CancellationToken cancellationToken) =>
        GetTimelineFacetsAsync(year, null, cancellationToken);

    public Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineDaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        return GetTimelineFacetsAsync(year, month, cancellationToken);
    }

    private async Task<IReadOnlyList<MediaTimelineFacet>> GetTimelineFacetsAsync(
        int? year,
        int? month,
        CancellationToken cancellationToken)
    {
        if (year.HasValue && year.Value is < 1 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();

            var groupExpression = !year.HasValue
                ? "substr(CaptureDate, 1, 4)"
                : !month.HasValue
                    ? "substr(CaptureDate, 6, 2)"
                    : "substr(CaptureDate, 9, 2)";
            var where = "CaptureDate IS NOT NULL AND length(CaptureDate) >= 10";
            if (year.HasValue) where += " AND substr(CaptureDate, 1, 4) = $year";
            if (month.HasValue) where += " AND substr(CaptureDate, 6, 2) = $month";

            command.CommandText = $"""
                WITH Effective AS (
                    SELECT i.Id,
                           COALESCE(
                               (SELECT m.CaptureDate FROM MediaMetadata m WHERE m.MediaItemId = i.Id AND m.CaptureDateOverridden = 1),
                               (SELECT f.CaptureDate FROM MediaFacts f WHERE f.MediaItemId = i.Id)
                           ) AS CaptureDate
                    FROM MediaItems i
                    WHERE EXISTS (
                        SELECT 1 FROM MediaLocations l
                        WHERE l.MediaItemId = i.Id AND l.IsPresent = 1
                    )
                )
                SELECT {groupExpression} AS Bucket, COUNT(*) AS MediaCount
                FROM Effective
                WHERE {where}
                GROUP BY {groupExpression}
                ORDER BY Bucket DESC;
                """;

            if (year.HasValue) command.Parameters.AddWithValue("$year", year.Value.ToString("0000"));
            if (month.HasValue) command.Parameters.AddWithValue("$month", month.Value.ToString("00"));

            var result = new List<MediaTimelineFacet>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0) || !int.TryParse(reader.GetString(0), out var bucket)) continue;
                result.Add(!year.HasValue
                    ? new MediaTimelineFacet(bucket, null, null, Convert.ToInt32(reader.GetInt64(1)))
                    : !month.HasValue
                        ? new MediaTimelineFacet(year.Value, bucket, null, Convert.ToInt32(reader.GetInt64(1)))
                        : new MediaTimelineFacet(year.Value, month.Value, bucket, Convert.ToInt32(reader.GetInt64(1))));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MediaSmartCollection>> GetSmartCollectionsAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            var rows = new List<(string Id, string Name, string? SearchText, MediaFileType? MediaType, string? EndpointId, string? RelativeFolder, DateTime? From, DateTime? To, int? Rating, DateTimeOffset Created, DateTimeOffset Updated)>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT Id, Name, SearchText, MediaType, EndpointId, RelativeFolder, CaptureDateFrom, CaptureDateTo,
                           MinimumRating, CreatedUtc, UpdatedUtc
                    FROM SmartCollections
                    ORDER BY Name COLLATE NOCASE;
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    rows.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : (MediaFileType)reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetString(5),
                        ReadNullableDateTime(reader, 6),
                        ReadNullableDateTime(reader, 7),
                        reader.IsDBNull(8) ? null : reader.GetInt32(8),
                        DateTimeOffset.Parse(reader.GetString(9)),
                        DateTimeOffset.Parse(reader.GetString(10))));
                }
            }

            var knowledge = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT CollectionId, KnowledgeItemId
                    FROM SmartCollectionKnowledgeItems
                    ORDER BY CollectionId, KnowledgeItemId;
                    """;
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var collectionId = reader.GetString(0);
                    if (!knowledge.TryGetValue(collectionId, out var values))
                    {
                        values = [];
                        knowledge[collectionId] = values;
                    }
                    values.Add(reader.GetString(1));
                }
            }

            return rows.Select(row => new MediaSmartCollection(
                    row.Id,
                    row.Name,
                    row.SearchText,
                    row.MediaType,
                    row.EndpointId,
                    row.RelativeFolder,
                    row.From,
                    row.To,
                    row.Rating,
                    knowledge.TryGetValue(row.Id, out var ids) ? ids.ToArray() : Array.Empty<string>(),
                    row.Created,
                    row.Updated))
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MediaSmartCollection> SaveSmartCollectionAsync(
        MediaSmartCollectionDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var name = (draft.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Name darf nicht leer sein.", nameof(draft));
        if (draft.CaptureDateFrom.HasValue && draft.CaptureDateTo.HasValue && draft.CaptureDateTo.Value < draft.CaptureDateFrom.Value)
            throw new ArgumentException("Enddatum darf nicht vor dem Startdatum liegen.", nameof(draft));
        if (draft.MinimumRating is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(draft), "Mindestbewertung muss zwischen 1 und 3 liegen.");

        var knowledgeIds = (draft.KnowledgeItemIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var id = string.IsNullOrWhiteSpace(draft.Id) ? Guid.NewGuid().ToString("N") : draft.Id.Trim();
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            foreach (var knowledgeId in knowledgeIds)
            {
                await using var exists = connection.CreateCommand();
                exists.Transaction = transaction;
                exists.CommandText = "SELECT COUNT(*) FROM MetadataKnowledgeItems WHERE Id = $id;";
                exists.Parameters.AddWithValue("$id", knowledgeId);
                if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken)) == 0)
                    throw new InvalidOperationException($"Metadatenkriterium ist nicht mehr vorhanden: {knowledgeId}");
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO SmartCollections (
                        Id, Name, SearchText, MediaType, EndpointId, RelativeFolder, CaptureDateFrom, CaptureDateTo,
                        MinimumRating, CreatedUtc, UpdatedUtc)
                    VALUES (
                        $id, $name, $searchText, $mediaType, $endpointId, $relativeFolder, $captureFrom, $captureTo,
                        $minimumRating, $created, $updated)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = excluded.Name,
                        SearchText = excluded.SearchText,
                        MediaType = excluded.MediaType,
                        EndpointId = excluded.EndpointId,
                        RelativeFolder = excluded.RelativeFolder,
                        CaptureDateFrom = excluded.CaptureDateFrom,
                        CaptureDateTo = excluded.CaptureDateTo,
                        MinimumRating = excluded.MinimumRating,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$id", id);
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$searchText", (object?)NormalizeNullableText(draft.SearchText) ?? DBNull.Value);
                command.Parameters.AddWithValue("$mediaType", draft.MediaType.HasValue ? (int)draft.MediaType.Value : DBNull.Value);
                command.Parameters.AddWithValue("$endpointId", (object?)NormalizeNullableText(draft.EndpointId) ?? DBNull.Value);
                command.Parameters.AddWithValue("$relativeFolder", (object?)NormalizeNullableText(draft.RelativeFolder) ?? DBNull.Value);
                command.Parameters.AddWithValue("$captureFrom", draft.CaptureDateFrom.HasValue ? draft.CaptureDateFrom.Value.ToString("O") : DBNull.Value);
                command.Parameters.AddWithValue("$captureTo", draft.CaptureDateTo.HasValue ? draft.CaptureDateTo.Value.ToString("O") : DBNull.Value);
                command.Parameters.AddWithValue("$minimumRating", draft.MinimumRating.HasValue ? draft.MinimumRating.Value : DBNull.Value);
                command.Parameters.AddWithValue("$created", now.UtcDateTime.ToString("O"));
                command.Parameters.AddWithValue("$updated", now.UtcDateTime.ToString("O"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM SmartCollectionKnowledgeItems WHERE CollectionId = $id;";
                delete.Parameters.AddWithValue("$id", id);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var knowledgeId in knowledgeIds)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO SmartCollectionKnowledgeItems (CollectionId, KnowledgeItemId)
                    VALUES ($collectionId, $knowledgeId);
                    """;
                insert.Parameters.AddWithValue("$collectionId", id);
                insert.Parameters.AddWithValue("$knowledgeId", knowledgeId);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var created = now;
            await using (var createdCommand = connection.CreateCommand())
            {
                createdCommand.CommandText = "SELECT CreatedUtc FROM SmartCollections WHERE Id = $id LIMIT 1;";
                createdCommand.Parameters.AddWithValue("$id", id);
                var value = await createdCommand.ExecuteScalarAsync(cancellationToken);
                if (value is string text && DateTimeOffset.TryParse(text, out var parsed)) created = parsed;
            }

            return new MediaSmartCollection(
                id,
                name,
                NormalizeNullableText(draft.SearchText),
                draft.MediaType,
                NormalizeNullableText(draft.EndpointId),
                NormalizeNullableText(draft.RelativeFolder),
                draft.CaptureDateFrom,
                draft.CaptureDateTo,
                draft.MinimumRating,
                knowledgeIds,
                created,
                now);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteSmartCollectionAsync(string collectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (var links = connection.CreateCommand())
            {
                links.Transaction = transaction;
                links.CommandText = "DELETE FROM SmartCollectionKnowledgeItems WHERE CollectionId = $id;";
                links.Parameters.AddWithValue("$id", collectionId.Trim());
                await links.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM SmartCollections WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", collectionId.Trim());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static DateTime? ReadNullableDateTime(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index)) return null;
        return DateTime.TryParse(
            reader.GetString(index),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? NormalizeNullableText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

            CREATE TABLE IF NOT EXISTS MediaMetadata (
                MediaItemId TEXT PRIMARY KEY NOT NULL,
                CaptureDateOverridden INTEGER NOT NULL DEFAULT 0,
                CaptureDate TEXT NULL,
                TitleOverridden INTEGER NOT NULL DEFAULT 0,
                Title TEXT NULL,
                DescriptionOverridden INTEGER NOT NULL DEFAULT 0,
                Description TEXT NULL,
                GpsMode INTEGER NOT NULL DEFAULT 0,
                Latitude REAL NULL,
                Longitude REAL NULL,
                UpdatedUtc TEXT NOT NULL,
                Revision INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS MediaMetadataHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChangeId TEXT NOT NULL,
                MediaItemId TEXT NOT NULL,
                ChangedUtc TEXT NOT NULL,
                PreviousJson TEXT NULL,
                CurrentJson TEXT NOT NULL,
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_MediaMetadataHistory_Item
                ON MediaMetadataHistory (MediaItemId, ChangedUtc DESC);

            CREATE TABLE IF NOT EXISTS MediaFacts (
                MediaItemId TEXT PRIMARY KEY NOT NULL,
                CaptureDate TEXT NULL,
                CameraMake TEXT NULL,
                CameraModel TEXT NULL,
                Latitude REAL NULL,
                Longitude REAL NULL,
                PixelWidth INTEGER NULL,
                PixelHeight INTEGER NULL,
                SourceLastWriteTimeUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_MediaFacts_CaptureDate
                ON MediaFacts (CaptureDate);

            CREATE INDEX IF NOT EXISTS IX_MediaFacts_Camera
                ON MediaFacts (CameraMake, CameraModel);

            CREATE TABLE IF NOT EXISTS MetadataKnowledgeItems (
                Id TEXT PRIMARY KEY NOT NULL,
                Kind INTEGER NOT NULL,
                Name TEXT NOT NULL,
                NormalizedName TEXT NOT NULL,
                StartDate TEXT NULL,
                EndDate TEXT NULL,
                Latitude REAL NULL,
                Longitude REAL NULL,
                Notes TEXT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 0,
                UsageCount INTEGER NOT NULL DEFAULT 0,
                LastUsedUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_MetadataKnowledgeItems_KindName
                ON MetadataKnowledgeItems (Kind, NormalizedName);

            CREATE INDEX IF NOT EXISTS IX_MetadataKnowledgeItems_Usage
                ON MetadataKnowledgeItems (Kind, IsPinned DESC, LastUsedUtc DESC, UsageCount DESC);

            CREATE TABLE IF NOT EXISTS MediaKnowledgeAssignments (
                MediaItemId TEXT NOT NULL,
                KnowledgeItemId TEXT NOT NULL,
                AssignedUtc TEXT NOT NULL,
                PRIMARY KEY (MediaItemId, KnowledgeItemId),
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE,
                FOREIGN KEY (KnowledgeItemId) REFERENCES MetadataKnowledgeItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_MediaKnowledgeAssignments_Knowledge
                ON MediaKnowledgeAssignments (KnowledgeItemId, MediaItemId);

            CREATE TABLE IF NOT EXISTS MediaRatings (
                MediaItemId TEXT PRIMARY KEY NOT NULL,
                Rating INTEGER NOT NULL CHECK (Rating BETWEEN 1 AND 3),
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (MediaItemId) REFERENCES MediaItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_MediaRatings_Rating
                ON MediaRatings (Rating);

            CREATE TABLE IF NOT EXISTS SmartCollections (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL,
                SearchText TEXT NULL,
                MediaType INTEGER NULL,
                EndpointId TEXT NULL,
                RelativeFolder TEXT NULL,
                CaptureDateFrom TEXT NULL,
                CaptureDateTo TEXT NULL,
                MinimumRating INTEGER NULL CHECK (MinimumRating IS NULL OR MinimumRating BETWEEN 1 AND 3),
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SmartCollectionKnowledgeItems (
                CollectionId TEXT NOT NULL,
                KnowledgeItemId TEXT NOT NULL,
                PRIMARY KEY (CollectionId, KnowledgeItemId),
                FOREIGN KEY (CollectionId) REFERENCES SmartCollections(Id) ON DELETE CASCADE,
                FOREIGN KEY (KnowledgeItemId) REFERENCES MetadataKnowledgeItems(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_SmartCollectionKnowledgeItems_Knowledge
                ON SmartCollectionKnowledgeItems (KnowledgeItemId, CollectionId);

            PRAGMA user_version = 4;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        _initialized = true;
    }

    private static DateTime? GetBestCaptureDate(ImageMetadata? metadata) =>
        metadata?.DateTimeOriginal
        ?? metadata?.CapturedAt
        ?? metadata?.DateTimeDigitized
        ?? metadata?.DateTimeModified;

    private static string NormalizeKnowledgeName(string value) =>
        value.Trim().ToUpperInvariant();

    private static MediaKnowledgeItem ReadKnowledgeItem(SqliteDataReader reader)
    {
        static DateOnly? ReadDate(SqliteDataReader r, int index) =>
            r.IsDBNull(index) ? null : DateOnly.TryParse(r.GetString(index), out var parsed) ? parsed : null;
        static DateTimeOffset? ReadDateTimeOffset(SqliteDataReader r, int index) =>
            r.IsDBNull(index) ? null : DateTimeOffset.TryParse(r.GetString(index), out var parsed) ? parsed : null;

        return new MediaKnowledgeItem(
            reader.GetString(0),
            (MediaKnowledgeKind)reader.GetInt32(1),
            reader.GetString(2),
            ReadDate(reader, 3),
            ReadDate(reader, 4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8) != 0,
            reader.GetInt32(9),
            ReadDateTimeOffset(reader, 10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static MediaMetadataSnapshot ReadMetadata(SqliteDataReader reader)
    {
        var captureText = reader.IsDBNull(2) ? null : reader.GetString(2);
        DateTime? captureDate = DateTime.TryParse(captureText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedCapture)
            ? parsedCapture
            : null;
        var updatedText = reader.IsDBNull(10) ? null : reader.GetString(10);
        DateTimeOffset? updatedUtc = DateTimeOffset.TryParse(updatedText, out var parsedUpdated)
            ? parsedUpdated
            : null;

        return new MediaMetadataSnapshot(
            reader.GetString(0),
            true,
            reader.GetInt32(1) != 0,
            captureDate,
            reader.GetInt32(3) != 0,
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5) != 0,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            (MetadataGpsMode)reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetDouble(9),
            updatedUtc);
    }

    private static async Task<string?> GetMetadataForHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string mediaItemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT MediaItemId,
                   CaptureDateOverridden, CaptureDate,
                   TitleOverridden, Title,
                   DescriptionOverridden, Description,
                   GpsMode, Latitude, Longitude, UpdatedUtc
            FROM MediaMetadata
            WHERE MediaItemId = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", mediaItemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? JsonSerializer.Serialize(ReadMetadata(reader))
            : null;
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);

    private sealed class FolderAccumulator(string name)
    {
        public string Name { get; } = name;
        public HashSet<string> Paths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasChildren { get; set; }
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
