using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media.Editing;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Image.Editor.Basic;

/// <summary>
/// Module-owned persistence for non-destructive edit stacks. The database contains
/// only recipes keyed by stable Media-ID; original image bytes are never written.
/// </summary>
public sealed class SqliteImageEditDocumentStore : IImageEditDocumentStore
{
    private const int DatabaseSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteImageEditDocumentStore(ImageEditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);

        var fullPath = Path.GetFullPath(options.DatabasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<ImageEditDocument?> GetAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT SchemaVersion, Revision, DocumentJson, UpdatedUtc
                FROM ImageEditDocuments
                WHERE MediaItemId = $mediaItemId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$mediaItemId", mediaItemId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            var schemaVersion = reader.GetInt32(0);
            var revision = reader.GetInt64(1);
            var documentJson = reader.GetString(2);
            var updatedUtc = DateTimeOffset.Parse(
                reader.GetString(3),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);

            var document = JsonSerializer.Deserialize<ImageEditDocument>(documentJson, JsonOptions)
                ?? throw new InvalidDataException(
                    $"Edit-Dokument für Media-ID '{mediaItemId}' konnte nicht gelesen werden.");

            if (!string.Equals(document.MediaItemId, mediaItemId, StringComparison.Ordinal)
                || document.SchemaVersion != schemaVersion
                || document.Revision != revision)
            {
                throw new InvalidDataException(
                    $"Edit-Dokument für Media-ID '{mediaItemId}' ist inkonsistent.");
            }

            document = document with { UpdatedUtc = updatedUtc };
            document.Validate();
            return document;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ImageEditDocument> SaveAsync(
        ImageEditDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            long? persistedRevision = null;
            await using (var read = connection.CreateCommand())
            {
                read.Transaction = (SqliteTransaction)transaction;
                read.CommandText = """
                    SELECT Revision
                    FROM ImageEditDocuments
                    WHERE MediaItemId = $mediaItemId
                    LIMIT 1;
                    """;
                read.Parameters.AddWithValue("$mediaItemId", document.MediaItemId);

                var scalar = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (scalar is not null && scalar is not DBNull)
                {
                    persistedRevision = Convert.ToInt64(
                        scalar,
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (persistedRevision is null)
            {
                if (document.Revision != 0)
                {
                    throw new ImageEditConcurrencyException(
                        $"Edit-Dokument '{document.MediaItemId}' existiert nicht mehr in der erwarteten Revision.");
                }
            }
            else if (persistedRevision.Value != document.Revision)
            {
                throw new ImageEditConcurrencyException(
                    $"Edit-Dokument '{document.MediaItemId}' wurde zwischenzeitlich geändert. "
                    + $"Erwartet: Revision {document.Revision}, vorhanden: {persistedRevision.Value}.");
            }

            var saved = document with
            {
                Revision = checked(document.Revision + 1),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            saved.Validate();

            var json = JsonSerializer.Serialize(saved, JsonOptions);

            await using (var write = connection.CreateCommand())
            {
                write.Transaction = (SqliteTransaction)transaction;
                write.CommandText = """
                    INSERT INTO ImageEditDocuments (
                        MediaItemId,
                        SchemaVersion,
                        Revision,
                        DocumentJson,
                        UpdatedUtc)
                    VALUES (
                        $mediaItemId,
                        $schemaVersion,
                        $revision,
                        $documentJson,
                        $updatedUtc)
                    ON CONFLICT(MediaItemId) DO UPDATE SET
                        SchemaVersion = excluded.SchemaVersion,
                        Revision = excluded.Revision,
                        DocumentJson = excluded.DocumentJson,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
                write.Parameters.AddWithValue("$mediaItemId", saved.MediaItemId);
                write.Parameters.AddWithValue("$schemaVersion", saved.SchemaVersion);
                write.Parameters.AddWithValue("$revision", saved.Revision);
                write.Parameters.AddWithValue("$documentJson", json);
                write.Parameters.AddWithValue("$updatedUtc", saved.UpdatedUtc.ToString("O"));

                await write.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return saved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(
        string mediaItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM ImageEditDocuments WHERE MediaItemId = $mediaItemId;";
            command.Parameters.AddWithValue("$mediaItemId", mediaItemId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        int currentVersion;
        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "PRAGMA user_version;";
            var scalar = await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            currentVersion = Convert.ToInt32(
                scalar,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        if (currentVersion > DatabaseSchemaVersion)
        {
            throw new InvalidDataException(
                $"Die Bildbearbeitungs-Datenbank verwendet Schema {currentVersion}; "
                + $"dieses Modul unterstützt höchstens Schema {DatabaseSchemaVersion}.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS ImageEditDocuments (
                MediaItemId TEXT NOT NULL PRIMARY KEY,
                SchemaVersion INTEGER NOT NULL,
                Revision INTEGER NOT NULL,
                DocumentJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ImageEditDocuments_UpdatedUtc
                ON ImageEditDocuments (UpdatedUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (currentVersion < DatabaseSchemaVersion)
        {
            await using var versionUpdate = connection.CreateCommand();
            versionUpdate.CommandText = $"PRAGMA user_version = {DatabaseSchemaVersion};";
            await versionUpdate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _initialized = true;
    }
}
