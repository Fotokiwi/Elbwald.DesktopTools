using Elbwald.DesktopTools.Contracts.Projects;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.Projects;

internal sealed class SqliteProjectStore
{
    private readonly string _connectionString;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public SqliteProjectStore(ProjectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);
        _databasePath = options.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task<IReadOnlyList<ProjectRecord>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes, CoverMediaItemId
            FROM Projects
            ORDER BY COALESCE(StartDate, '9999-12-31'), Name COLLATE NOCASE;
            """;

        var result = new List<ProjectRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadProject(reader));
        }

        return result;
    }

    public async Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes, CoverMediaItemId
            FROM Projects
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProject(reader) : null;
    }

    public async Task<ProjectRecord> InsertProjectAsync(ProjectCreateRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Name, request.Kind, request.StartDate, request.EndDate);
        await EnsureInitializedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var project = new ProjectRecord(
            Guid.CreateVersion7().ToString("N"),
            request.Name.Trim(),
            request.Kind.Trim(),
            now,
            now,
            request.StartDate,
            request.EndDate,
            NormalizeOptional(request.Location),
            NormalizeOptional(request.Notes));

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Projects(Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes, CoverMediaItemId)
            VALUES($id, $name, $kind, $created, $updated, $start, $end, $location, $notes, $cover);
            """;
        AddProjectParameters(command, project);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return project;
    }

    public async Task<ProjectRecord> UpdateProjectAsync(string projectId, ProjectUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        Validate(request.Name, request.Kind, request.StartDate, request.EndDate);
        await EnsureInitializedAsync(cancellationToken);
        var existing = await GetProjectAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Das Projekt existiert nicht mehr.");
        var updated = existing with
        {
            Name = request.Name.Trim(),
            Kind = request.Kind.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Location = NormalizeOptional(request.Location),
            Notes = NormalizeOptional(request.Notes)
        };

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Projects
            SET Name=$name, Kind=$kind, UpdatedAtUtc=$updated, StartDate=$start,
                EndDate=$end, Location=$location, Notes=$notes
            WHERE Id=$id;
            """;
        AddProjectParameters(command, updated);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return updated;
    }

    public async Task<IReadOnlyList<ProjectMediaReference>> GetMediaReferencesAsync(string projectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProjectId, MediaItemId, AddedAtUtc, IsHighlight
            FROM ProjectMedia
            WHERE ProjectId=$projectId
            ORDER BY IsHighlight DESC, AddedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        var result = new List<ProjectMediaReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectMediaReference(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(
                    reader.GetString(2),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetInt64(3) != 0));
        }
        return result;
    }

    public async Task<bool> AddMediaAsync(string projectId, string mediaItemId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ProjectMedia(ProjectId, MediaItemId, AddedAtUtc, IsHighlight)
            VALUES($projectId, $mediaItemId, $added, 0);
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId);
        command.Parameters.AddWithValue("$added", DateTimeOffset.UtcNow.ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> RemoveMediaAsync(string projectId, string mediaItemId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var remove = connection.CreateCommand();
        remove.Transaction = (SqliteTransaction)transaction;
        remove.CommandText = """
            DELETE FROM ProjectMedia
            WHERE ProjectId=$projectId AND MediaItemId=$mediaItemId;
            """;
        remove.Parameters.AddWithValue("$projectId", projectId);
        remove.Parameters.AddWithValue("$mediaItemId", mediaItemId);
        var removed = await remove.ExecuteNonQueryAsync(cancellationToken) > 0;

        if (removed)
        {
            await using var clearJournalLinks = connection.CreateCommand();
            clearJournalLinks.Transaction = (SqliteTransaction)transaction;
            clearJournalLinks.CommandText = """
                DELETE FROM ProjectJournalMedia
                WHERE MediaItemId=$mediaItemId
                  AND EntryId IN (
                      SELECT Id
                      FROM ProjectJournalEntries
                      WHERE ProjectId=$projectId
                  );
                """;
            clearJournalLinks.Parameters.AddWithValue("$projectId", projectId);
            clearJournalLinks.Parameters.AddWithValue("$mediaItemId", mediaItemId);
            await clearJournalLinks.ExecuteNonQueryAsync(cancellationToken);

            await using var clearCover = connection.CreateCommand();
            clearCover.Transaction = (SqliteTransaction)transaction;
            clearCover.CommandText = """
                UPDATE Projects
                SET CoverMediaItemId=NULL, UpdatedAtUtc=$updated
                WHERE Id=$projectId AND CoverMediaItemId=$mediaItemId;
                """;
            clearCover.Parameters.AddWithValue("$projectId", projectId);
            clearCover.Parameters.AddWithValue("$mediaItemId", mediaItemId);
            clearCover.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await clearCover.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return removed;
    }

    public async Task SetCoverAsync(
        string projectId,
        string? mediaItemId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(mediaItemId))
        {
            await using var verify = connection.CreateCommand();
            verify.CommandText = """
                SELECT COUNT(1)
                FROM ProjectMedia
                WHERE ProjectId=$projectId AND MediaItemId=$mediaItemId;
                """;
            verify.Parameters.AddWithValue("$projectId", projectId);
            verify.Parameters.AddWithValue("$mediaItemId", mediaItemId);
            var count = Convert.ToInt32(
                await verify.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);

            if (count == 0)
            {
                throw new InvalidOperationException(
                    "Das ausgewählte Medium gehört nicht mehr zu diesem Projekt.");
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Projects
            SET CoverMediaItemId=$cover, UpdatedAtUtc=$updated
            WHERE Id=$projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue(
            "$cover",
            (object?)NormalizeOptional(mediaItemId) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Das Projekt existiert nicht mehr.");
        }
    }

    public async Task SetHighlightAsync(
        string projectId,
        string mediaItemId,
        bool isHighlight,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            UPDATE ProjectMedia
            SET IsHighlight=$highlight
            WHERE ProjectId=$projectId AND MediaItemId=$mediaItemId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId);
        command.Parameters.AddWithValue("$highlight", isHighlight ? 1 : 0);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                "Das ausgewählte Medium gehört nicht mehr zu diesem Projekt.");
        }

        await using var touchProject = connection.CreateCommand();
        touchProject.Transaction = (SqliteTransaction)transaction;
        touchProject.CommandText = """
            UPDATE Projects
            SET UpdatedAtUtc=$updated
            WHERE Id=$projectId;
            """;
        touchProject.Parameters.AddWithValue("$projectId", projectId);
        touchProject.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await touchProject.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectJournalEntry>> GetJournalEntriesAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, EntryDate, Title, Body, Location, CreatedAtUtc, UpdatedAtUtc
            FROM ProjectJournalEntries
            WHERE ProjectId=$projectId
            ORDER BY EntryDate DESC, UpdatedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);

        var result = new List<ProjectJournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(ReadJournalEntry(reader));
        }

        return result;
    }

    public async Task<ProjectJournalEntry> CreateJournalEntryAsync(
        string projectId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateJournalEntry(request);
        await EnsureInitializedAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var entry = new ProjectJournalEntry(
            Guid.CreateVersion7().ToString("N"),
            projectId,
            request.EntryDate,
            request.Title.Trim(),
            NormalizeOptional(request.Body),
            NormalizeOptional(request.Location),
            now,
            now);

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProjectJournalEntries(
                Id, ProjectId, EntryDate, Title, Body, Location, CreatedAtUtc, UpdatedAtUtc)
            VALUES(
                $id, $projectId, $entryDate, $title, $body, $location, $created, $updated);
            """;
        AddJournalEntryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TouchProjectAsync(connection, projectId, cancellationToken);
        return entry;
    }

    public async Task<ProjectJournalEntry> UpdateJournalEntryAsync(
        string projectId,
        string entryId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(request);
        ValidateJournalEntry(request);
        await EnsureInitializedAsync(cancellationToken);

        var existing = await GetJournalEntryAsync(projectId, entryId, cancellationToken)
            ?? throw new InvalidOperationException("Der Tagebucheintrag existiert nicht mehr.");

        var updated = existing with
        {
            EntryDate = request.EntryDate,
            Title = request.Title.Trim(),
            Body = NormalizeOptional(request.Body),
            Location = NormalizeOptional(request.Location),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ProjectJournalEntries
            SET EntryDate=$entryDate,
                Title=$title,
                Body=$body,
                Location=$location,
                UpdatedAtUtc=$updated
            WHERE Id=$id AND ProjectId=$projectId;
            """;
        AddJournalEntryParameters(command, updated);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Der Tagebucheintrag existiert nicht mehr.");
        }

        await TouchProjectAsync(connection, projectId, cancellationToken);
        return updated;
    }

    public async Task DeleteJournalEntryAsync(
        string projectId,
        string entryId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM ProjectJournalEntries
            WHERE Id=$id AND ProjectId=$projectId;
            """;
        command.Parameters.AddWithValue("$id", entryId);
        command.Parameters.AddWithValue("$projectId", projectId);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Der Tagebucheintrag existiert nicht mehr.");
        }

        await TouchProjectAsync(connection, projectId, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectJournalMediaReference>> GetJournalMediaReferencesAsync(
        string entryId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EntryId, MediaItemId, AddedAtUtc
            FROM ProjectJournalMedia
            WHERE EntryId=$entryId
            ORDER BY AddedAtUtc, MediaItemId;
            """;
        command.Parameters.AddWithValue("$entryId", entryId);

        var result = new List<ProjectJournalMediaReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectJournalMediaReference(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(
                    reader.GetString(2),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind)));
        }

        return result;
    }

    public async Task SetJournalMediaAsync(
        string projectId,
        string entryId,
        IReadOnlyCollection<string> mediaItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(mediaItemIds);
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var verifyEntry = connection.CreateCommand();
        verifyEntry.Transaction = (SqliteTransaction)transaction;
        verifyEntry.CommandText = """
            SELECT COUNT(1)
            FROM ProjectJournalEntries
            WHERE Id=$entryId AND ProjectId=$projectId;
            """;
        verifyEntry.Parameters.AddWithValue("$entryId", entryId);
        verifyEntry.Parameters.AddWithValue("$projectId", projectId);

        var entryCount = Convert.ToInt32(
            await verifyEntry.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);

        if (entryCount == 0)
        {
            throw new InvalidOperationException("Der Tagebucheintrag existiert nicht mehr.");
        }

        await using var clear = connection.CreateCommand();
        clear.Transaction = (SqliteTransaction)transaction;
        clear.CommandText = "DELETE FROM ProjectJournalMedia WHERE EntryId=$entryId;";
        clear.Parameters.AddWithValue("$entryId", entryId);
        await clear.ExecuteNonQueryAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var mediaItemId in mediaItemIds
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO ProjectJournalMedia(EntryId, MediaItemId, AddedAtUtc)
                VALUES($entryId, $mediaItemId, $added);
                """;
            insert.Parameters.AddWithValue("$entryId", entryId);
            insert.Parameters.AddWithValue("$mediaItemId", mediaItemId);
            insert.Parameters.AddWithValue("$added", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var touchProject = connection.CreateCommand();
        touchProject.Transaction = (SqliteTransaction)transaction;
        touchProject.CommandText = """
            UPDATE Projects
            SET UpdatedAtUtc=$updated
            WHERE Id=$projectId;
            """;
        touchProject.Parameters.AddWithValue("$projectId", projectId);
        touchProject.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await touchProject.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<ProjectJournalEntry?> GetJournalEntryAsync(
        string projectId,
        string entryId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProjectId, EntryDate, Title, Body, Location, CreatedAtUtc, UpdatedAtUtc
            FROM ProjectJournalEntries
            WHERE Id=$entryId AND ProjectId=$projectId;
            """;
        command.Parameters.AddWithValue("$entryId", entryId);
        command.Parameters.AddWithValue("$projectId", projectId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadJournalEntry(reader)
            : null;
    }

    private static async Task TouchProjectAsync(
        SqliteConnection connection,
        string projectId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Projects
            SET UpdatedAtUtc=$updated
            WHERE Id=$projectId;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS Projects(
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Kind TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    StartDate TEXT NULL,
                    EndDate TEXT NULL,
                    Location TEXT NULL,
                    Notes TEXT NULL,
                    CoverMediaItemId TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS ProjectMedia(
                    ProjectId TEXT NOT NULL,
                    MediaItemId TEXT NOT NULL,
                    AddedAtUtc TEXT NOT NULL,
                    IsHighlight INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(ProjectId, MediaItemId),
                    FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ProjectMedia_MediaItemId ON ProjectMedia(MediaItemId);

                CREATE TABLE IF NOT EXISTS ProjectJournalEntries(
                    Id TEXT PRIMARY KEY,
                    ProjectId TEXT NOT NULL,
                    EntryDate TEXT NOT NULL,
                    Title TEXT NOT NULL,
                    Body TEXT NULL,
                    Location TEXT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ProjectJournalEntries_ProjectDate
                    ON ProjectJournalEntries(ProjectId, EntryDate DESC);

                CREATE TABLE IF NOT EXISTS ProjectJournalMedia(
                    EntryId TEXT NOT NULL,
                    MediaItemId TEXT NOT NULL,
                    AddedAtUtc TEXT NOT NULL,
                    PRIMARY KEY(EntryId, MediaItemId),
                    FOREIGN KEY(EntryId) REFERENCES ProjectJournalEntries(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ProjectJournalMedia_MediaItemId
                    ON ProjectJournalMedia(MediaItemId);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // Existing alpha databases predate cover/highlight metadata.
            // Additive columns keep project/media references intact and need no data rewrite.
            await EnsureColumnAsync(
                connection,
                "Projects",
                "CoverMediaItemId",
                "TEXT NULL",
                cancellationToken);

            await EnsureColumnAsync(
                connection,
                "ProjectMedia",
                "IsHighlight",
                "INTEGER NOT NULL DEFAULT 0",
                cancellationToken);

            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info({tableName});";

        var exists = false;
        await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(
                        reader.GetString(1),
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static ProjectRecord ReadProject(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        DateTimeOffset.Parse(
            reader.GetString(3),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(
            reader.GetString(4),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : DateOnly.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private static void AddProjectParameters(SqliteCommand command, ProjectRecord project)
    {
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$kind", project.Kind);
        command.Parameters.AddWithValue("$created", project.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated", project.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$start", (object?)project.StartDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$end", (object?)project.EndDate?.ToString("yyyy-MM-dd") ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)project.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)project.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$cover", (object?)project.CoverMediaItemId ?? DBNull.Value);
    }

    private static ProjectJournalEntry ReadJournalEntry(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        DateOnly.Parse(reader.GetString(2)),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        DateTimeOffset.Parse(
            reader.GetString(6),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(
            reader.GetString(7),
            null,
            System.Globalization.DateTimeStyles.RoundtripKind));

    private static void AddJournalEntryParameters(
        SqliteCommand command,
        ProjectJournalEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$projectId", entry.ProjectId);
        command.Parameters.AddWithValue("$entryDate", entry.EntryDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$body", (object?)entry.Body ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)entry.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", entry.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updated", entry.UpdatedAtUtc.ToString("O"));
    }

    private static void ValidateJournalEntry(ProjectJournalEntryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ArgumentException("Ein Tagebucheintrag benötigt einen Titel.");
        }
    }

    private static void Validate(string name, string kind, DateOnly? startDate, DateOnly? endDate)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ein Projekt benötigt einen Namen.");
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Ein Projekt benötigt einen Typ.");
        if (startDate is not null && endDate is not null && endDate < startDate)
            throw new ArgumentException("Das Enddatum darf nicht vor dem Startdatum liegen.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
