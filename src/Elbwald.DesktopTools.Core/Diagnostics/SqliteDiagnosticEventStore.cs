using Elbwald.DesktopTools.Contracts.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.Diagnostics;

public sealed class SqliteDiagnosticEventStore
    : IDiagnosticEventStore
{
    private readonly DiagnosticEventStoreOptions _options;
    private readonly SemaphoreSlim _initializationGate =
        new(1, 1);

    private volatile bool _initialized;

    public SqliteDiagnosticEventStore(
        DiagnosticEventStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);

        _options = options;
    }

    public async ValueTask<bool> TryWriteAsync(
        DiagnosticEventWrite diagnosticEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await EnsureInitializedAsync(
                cancellationToken);

            await using var connection =
                new SqliteConnection(
                    CreateConnectionString());

            await connection.OpenAsync(
                cancellationToken);

            await ConfigureConnectionAsync(
                connection,
                cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO diagnostic_events
                (
                    occurred_utc,
                    severity,
                    category,
                    source,
                    message,
                    details,
                    file_path,
                    destination_path,
                    mount_point,
                    file_system_type,
                    volume_device_path,
                    physical_device_path,
                    device_model,
                    device_serial_number,
                    is_rotational,
                    capacity_bytes,
                    operation_kind,
                    correlation_id,
                    error_code
                )
                VALUES
                (
                    $occurred_utc,
                    $severity,
                    $category,
                    $source,
                    $message,
                    $details,
                    $file_path,
                    $destination_path,
                    $mount_point,
                    $file_system_type,
                    $volume_device_path,
                    $physical_device_path,
                    $device_model,
                    $device_serial_number,
                    $is_rotational,
                    $capacity_bytes,
                    $operation_kind,
                    $correlation_id,
                    $error_code
                );
                """;

            AddParameter(
                command,
                "$occurred_utc",
                diagnosticEvent.OccurredAtUtc
                    .ToUniversalTime()
                    .ToString("O"));

            AddParameter(
                command,
                "$severity",
                (int)diagnosticEvent.Severity);

            AddParameter(
                command,
                "$category",
                (int)diagnosticEvent.Category);

            AddParameter(
                command,
                "$source",
                diagnosticEvent.Source);

            AddParameter(
                command,
                "$message",
                diagnosticEvent.Message);

            AddParameter(
                command,
                "$details",
                diagnosticEvent.Details);

            AddParameter(
                command,
                "$file_path",
                diagnosticEvent.FilePath);

            AddParameter(
                command,
                "$destination_path",
                diagnosticEvent.DestinationPath);

            AddParameter(
                command,
                "$mount_point",
                diagnosticEvent.MountPoint);

            AddParameter(
                command,
                "$file_system_type",
                diagnosticEvent.FileSystemType);

            AddParameter(
                command,
                "$volume_device_path",
                diagnosticEvent.VolumeDevicePath);

            AddParameter(
                command,
                "$physical_device_path",
                diagnosticEvent.PhysicalDevicePath);

            AddParameter(
                command,
                "$device_model",
                diagnosticEvent.DeviceModel);

            AddParameter(
                command,
                "$device_serial_number",
                diagnosticEvent.DeviceSerialNumber);

            AddParameter(
                command,
                "$is_rotational",
                diagnosticEvent.IsRotational is null
                    ? null
                    : diagnosticEvent.IsRotational.Value
                        ? 1
                        : 0);

            AddParameter(
                command,
                "$capacity_bytes",
                diagnosticEvent.CapacityBytes);

            AddParameter(
                command,
                "$operation_kind",
                diagnosticEvent.OperationKind);

            AddParameter(
                command,
                "$correlation_id",
                diagnosticEvent.CorrelationId);

            AddParameter(
                command,
                "$error_code",
                diagnosticEvent.ErrorCode);

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch
        {
            // Diagnoseprotokoll ist ausdrücklich best effort und niemals Teil
            // der sicherheitskritischen Dateiwahrheit.
            return false;
        }
    }

    public async Task<IReadOnlyList<DiagnosticEventEntry>> QueryAsync(
        DiagnosticEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        await EnsureInitializedAsync(
            cancellationToken);

        var limit =
            Math.Clamp(
                query.Limit,
                1,
                1000);

        await using var connection =
            new SqliteConnection(
                CreateConnectionString());

        await connection.OpenAsync(
            cancellationToken);

        await ConfigureConnectionAsync(
            connection,
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        var predicates =
            new List<string>();

        if (query.MinimumSeverity is not null)
        {
            predicates.Add(
                "severity >= $minimum_severity");

            AddParameter(
                command,
                "$minimum_severity",
                (int)query.MinimumSeverity.Value);
        }

        if (query.Category is not null)
        {
            predicates.Add(
                "category = $category");

            AddParameter(
                command,
                "$category",
                (int)query.Category.Value);
        }

        if (!string.IsNullOrWhiteSpace(
                query.SearchText))
        {
            predicates.Add(
                """
                (
                    message LIKE $search ESCAPE '\'
                    OR details LIKE $search ESCAPE '\'
                    OR file_path LIKE $search ESCAPE '\'
                    OR destination_path LIKE $search ESCAPE '\'
                    OR mount_point LIKE $search ESCAPE '\'
                    OR file_system_type LIKE $search ESCAPE '\'
                    OR physical_device_path LIKE $search ESCAPE '\'
                    OR device_model LIKE $search ESCAPE '\'
                    OR device_serial_number LIKE $search ESCAPE '\'
                )
                """);

            AddParameter(
                command,
                "$search",
                "%"
                + EscapeLike(
                    query.SearchText.Trim())
                + "%");
        }

        var where =
            predicates.Count == 0
                ? string.Empty
                : "WHERE "
                  + string.Join(
                      " AND ",
                      predicates);

        command.CommandText =
            $$"""
            SELECT
                id,
                occurred_utc,
                severity,
                category,
                source,
                message,
                details,
                file_path,
                destination_path,
                mount_point,
                file_system_type,
                volume_device_path,
                physical_device_path,
                device_model,
                device_serial_number,
                is_rotational,
                capacity_bytes,
                operation_kind,
                correlation_id,
                error_code
            FROM diagnostic_events
            {{where}}
            ORDER BY id DESC
            LIMIT $limit;
            """;

        AddParameter(
            command,
            "$limit",
            limit);

        var entries =
            new List<DiagnosticEventEntry>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(
                   cancellationToken))
        {
            entries.Add(
                ReadEntry(
                    reader));
        }

        return entries;
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(
            cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            var directory =
                Path.GetDirectoryName(
                    _options.DatabasePath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            await using var connection =
                new SqliteConnection(
                    CreateConnectionString());

            await connection.OpenAsync(
                cancellationToken);

            await ConfigureConnectionAsync(
                connection,
                cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;

                CREATE TABLE IF NOT EXISTS diagnostic_events
                (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    occurred_utc TEXT NOT NULL,
                    severity INTEGER NOT NULL,
                    category INTEGER NOT NULL,
                    source TEXT NOT NULL,
                    message TEXT NOT NULL,
                    details TEXT NULL,
                    file_path TEXT NULL,
                    destination_path TEXT NULL,
                    mount_point TEXT NULL,
                    file_system_type TEXT NULL,
                    volume_device_path TEXT NULL,
                    physical_device_path TEXT NULL,
                    device_model TEXT NULL,
                    device_serial_number TEXT NULL,
                    is_rotational INTEGER NULL,
                    capacity_bytes INTEGER NULL,
                    operation_kind TEXT NULL,
                    correlation_id TEXT NULL,
                    error_code TEXT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_diagnostic_events_occurred
                    ON diagnostic_events(occurred_utc DESC);

                CREATE INDEX IF NOT EXISTS ix_diagnostic_events_severity_category
                    ON diagnostic_events(severity, category);
                """;

            await command.ExecuteNonQueryAsync(
                cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=250;
            """;

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private string CreateConnectionString()
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _options.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            };

        return builder.ToString();
    }

    private static DiagnosticEventEntry ReadEntry(
        SqliteDataReader reader)
    {
        var occurred =
            DateTimeOffset.Parse(
                reader.GetString(1),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind);

        return new DiagnosticEventEntry(
            reader.GetInt64(0),
            occurred,
            (DiagnosticEventSeverity)reader.GetInt32(2),
            (DiagnosticEventCategory)reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            GetNullableString(reader, 6),
            GetNullableString(reader, 7),
            GetNullableString(reader, 8),
            GetNullableString(reader, 9),
            GetNullableString(reader, 10),
            GetNullableString(reader, 11),
            GetNullableString(reader, 12),
            GetNullableString(reader, 13),
            GetNullableString(reader, 14),
            GetNullableBoolean(reader, 15),
            GetNullableInt64(reader, 16),
            GetNullableString(reader, 17),
            GetNullableString(reader, 18),
            GetNullableString(reader, 19));
    }

    private static string? GetNullableString(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static bool? GetNullableBoolean(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal) != 0;
    }

    private static long? GetNullableInt64(
        SqliteDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt64(ordinal);
    }

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        command.Parameters.AddWithValue(
            name,
            value
            ?? DBNull.Value);
    }

    private static string EscapeLike(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "%",
                "\\%",
                StringComparison.Ordinal)
            .Replace(
                "_",
                "\\_",
                StringComparison.Ordinal);
    }
}
