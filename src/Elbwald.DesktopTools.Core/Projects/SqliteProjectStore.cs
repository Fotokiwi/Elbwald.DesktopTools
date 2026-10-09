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
            SELECT Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes
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
            SELECT Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes
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
            INSERT INTO Projects(Id, Name, Kind, CreatedAtUtc, UpdatedAtUtc, StartDate, EndDate, Location, Notes)
            VALUES($id, $name, $kind, $created, $updated, $start, $end, $location, $notes);
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
            SELECT ProjectId, MediaItemId, AddedAtUtc
            FROM ProjectMedia
            WHERE ProjectId=$projectId
            ORDER BY AddedAtUtc DESC;
            """;
        command.Parameters.AddWithValue("$projectId", projectId);
        var result = new List<ProjectMediaReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProjectMediaReference(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return result;
    }

    public async Task<bool> AddMediaAsync(string projectId, string mediaItemId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO ProjectMedia(ProjectId, MediaItemId, AddedAtUtc)
            VALUES($projectId, $mediaItemId, $added);
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
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProjectMedia WHERE ProjectId=$projectId AND MediaItemId=$mediaItemId;";
        command.Parameters.AddWithValue("$projectId", projectId);
        command.Parameters.AddWithValue("$mediaItemId", mediaItemId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
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
                    Notes TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS ProjectMedia(
                    ProjectId TEXT NOT NULL,
                    MediaItemId TEXT NOT NULL,
                    AddedAtUtc TEXT NOT NULL,
                    PRIMARY KEY(ProjectId, MediaItemId),
                    FOREIGN KEY(ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_ProjectMedia_MediaItemId ON ProjectMedia(MediaItemId);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
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
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
        DateTimeOffset.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
        reader.IsDBNull(5) ? null : DateOnly.Parse(reader.GetString(5)),
        reader.IsDBNull(6) ? null : DateOnly.Parse(reader.GetString(6)),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8));

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
    }

    private static void Validate(string name, string kind, DateOnly? startDate, DateOnly? endDate)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Ein Projekt benötigt einen Namen.");
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Ein Projekt benötigt einen Typ.");
        if (startDate is not null && endDate is not null && endDate < startDate)
            throw new ArgumentException("Das Enddatum darf nicht vor dem Startdatum liegen.");
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
