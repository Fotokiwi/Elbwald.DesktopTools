using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Media;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class SqliteMediaAnalysisCache
    : IMediaAnalysisCache
{
    private readonly MediaAnalysisCacheOptions _options;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);

    private int _initialized;
    private int _disabled;

    public SqliteMediaAnalysisCache(
        MediaAnalysisCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DatabasePath);

        _options = options with
        {
            DatabasePath = Path.GetFullPath(options.DatabasePath)
        };
    }

    public async Task<ImageMetadataReadResult?> TryGetImageMetadataAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (Volatile.Read(ref _disabled) == 1)
        {
            return null;
        }

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (Volatile.Read(ref _disabled) == 1)
            {
                return null;
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    state,
                    pixel_width,
                    pixel_height,
                    captured_at_ticks,
                    date_time_original_ticks,
                    date_time_digitized_ticks,
                    date_time_modified_ticks,
                    gps_date_time_utc_ticks,
                    camera_make,
                    camera_model,
                    lens_model,
                    orientation,
                    has_exif,
                    has_gps,
                    latitude,
                    longitude,
                    warnings_json,
                    error_message
                FROM image_metadata_cache_v2
                WHERE path = $path
                  AND file_length = $length
                  AND last_write_utc_ticks = $lastWrite
                  AND analysis_version = $version
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$path", NormalizePath(file.FullPath));
            command.Parameters.AddWithValue("$length", file.Length);
            command.Parameters.AddWithValue("$lastWrite", file.LastWriteTimeUtc.UtcDateTime.Ticks);
            command.Parameters.AddWithValue("$version", _options.AnalysisVersion);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var state = (ImageMetadataReadState)reader.GetInt32(0);
            ImageMetadata? metadata = null;

            if (state == ImageMetadataReadState.Success)
            {
                metadata =
                    new ImageMetadata(
                        GetNullableInt32(reader, 1),
                        GetNullableInt32(reader, 2),
                        GetNullableDateTime(reader, 3),
                        GetNullableString(reader, 8),
                        GetNullableString(reader, 9),
                        GetNullableString(reader, 10),
                        GetNullableInt32(reader, 11),
                        GetBoolean(reader, 12),
                        GetBoolean(reader, 13),
                        GetNullableDouble(reader, 14),
                        GetNullableDouble(reader, 15))
                    {
                        DateTimeOriginal =
                            GetNullableDateTime(reader, 4),
                        DateTimeDigitized =
                            GetNullableDateTime(reader, 5),
                        DateTimeModified =
                            GetNullableDateTime(reader, 6),
                        GpsDateTimeUtc =
                            GetNullableUtcDateTime(reader, 7)
                    };
            }

            return new ImageMetadataReadResult(
                state,
                metadata,
                DeserializeWarnings(GetNullableString(reader, 16)),
                GetNullableString(reader, 17),
                isFromCache: true);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SqliteException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            DisableCache();
            return null;
        }
    }

    public async Task StoreImageMetadataAsync(
        MediaFile file,
        ImageMetadataReadResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(result);

        if (Volatile.Read(ref _disabled) == 1
            || result.State != ImageMetadataReadState.Success
            || result.Metadata is null)
        {
            return;
        }

        try
        {
            await EnsureInitializedAsync(cancellationToken);

            if (Volatile.Read(ref _disabled) == 1)
            {
                return;
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO image_metadata_cache_v2
                (
                    path,
                    file_length,
                    last_write_utc_ticks,
                    analysis_version,
                    state,
                    pixel_width,
                    pixel_height,
                    captured_at_ticks,
                    date_time_original_ticks,
                    date_time_digitized_ticks,
                    date_time_modified_ticks,
                    gps_date_time_utc_ticks,
                    camera_make,
                    camera_model,
                    lens_model,
                    orientation,
                    has_exif,
                    has_gps,
                    latitude,
                    longitude,
                    warnings_json,
                    error_message,
                    cached_at_utc_ticks
                )
                VALUES
                (
                    $path,
                    $length,
                    $lastWrite,
                    $version,
                    $state,
                    $pixelWidth,
                    $pixelHeight,
                    $capturedAt,
                    $dateTimeOriginal,
                    $dateTimeDigitized,
                    $dateTimeModified,
                    $gpsDateTimeUtc,
                    $cameraMake,
                    $cameraModel,
                    $lensModel,
                    $orientation,
                    $hasExif,
                    $hasGps,
                    $latitude,
                    $longitude,
                    $warnings,
                    $errorMessage,
                    $cachedAt
                )
                ON CONFLICT(path) DO UPDATE SET
                    file_length = excluded.file_length,
                    last_write_utc_ticks = excluded.last_write_utc_ticks,
                    analysis_version = excluded.analysis_version,
                    state = excluded.state,
                    pixel_width = excluded.pixel_width,
                    pixel_height = excluded.pixel_height,
                    captured_at_ticks = excluded.captured_at_ticks,
                    date_time_original_ticks = excluded.date_time_original_ticks,
                    date_time_digitized_ticks = excluded.date_time_digitized_ticks,
                    date_time_modified_ticks = excluded.date_time_modified_ticks,
                    gps_date_time_utc_ticks = excluded.gps_date_time_utc_ticks,
                    camera_make = excluded.camera_make,
                    camera_model = excluded.camera_model,
                    lens_model = excluded.lens_model,
                    orientation = excluded.orientation,
                    has_exif = excluded.has_exif,
                    has_gps = excluded.has_gps,
                    latitude = excluded.latitude,
                    longitude = excluded.longitude,
                    warnings_json = excluded.warnings_json,
                    error_message = excluded.error_message,
                    cached_at_utc_ticks = excluded.cached_at_utc_ticks;
                """;

            var metadata = result.Metadata;

            AddParameter(command, "$path", NormalizePath(file.FullPath));
            AddParameter(command, "$length", file.Length);
            AddParameter(command, "$lastWrite", file.LastWriteTimeUtc.UtcDateTime.Ticks);
            AddParameter(command, "$version", _options.AnalysisVersion);
            AddParameter(command, "$state", (int)result.State);
            AddParameter(command, "$pixelWidth", metadata.PixelWidth);
            AddParameter(command, "$pixelHeight", metadata.PixelHeight);
            AddParameter(command, "$capturedAt", metadata.CapturedAt?.Ticks);
            AddParameter(command, "$dateTimeOriginal", metadata.DateTimeOriginal?.Ticks);
            AddParameter(command, "$dateTimeDigitized", metadata.DateTimeDigitized?.Ticks);
            AddParameter(command, "$dateTimeModified", metadata.DateTimeModified?.Ticks);
            AddParameter(
                command,
                "$gpsDateTimeUtc",
                metadata.GpsDateTimeUtc is { } gpsDateTimeUtc
                    ? DateTime.SpecifyKind(
                        gpsDateTimeUtc,
                        DateTimeKind.Utc).Ticks
                    : null);
            AddParameter(command, "$cameraMake", metadata.CameraMake);
            AddParameter(command, "$cameraModel", metadata.CameraModel);
            AddParameter(command, "$lensModel", metadata.LensModel);
            AddParameter(command, "$orientation", metadata.Orientation);
            AddParameter(command, "$hasExif", metadata.HasExif ? 1 : 0);
            AddParameter(command, "$hasGps", metadata.HasGps ? 1 : 0);
            AddParameter(command, "$latitude", metadata.Latitude);
            AddParameter(command, "$longitude", metadata.Longitude);
            AddParameter(command, "$warnings", JsonSerializer.Serialize(result.Warnings));
            AddParameter(command, "$errorMessage", result.ErrorMessage);
            AddParameter(command, "$cachedAt", DateTimeOffset.UtcNow.UtcDateTime.Ticks);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SqliteException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            DisableCache();
        }
    }

    public async Task<MediaCacheStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disabled) == 1)
        {
            return new MediaCacheStatistics(
                _options.DatabasePath,
                EntryCount: 0,
                SizeBytes:
                    GetDatabaseSizeBytes(),
                IsAvailable: false,
                Message:
                    "Der SQLite-Cache ist für diese Sitzung deaktiviert.");
        }

        try
        {
            await EnsureInitializedAsync(
                cancellationToken);

            if (Volatile.Read(ref _disabled) == 1)
            {
                return new MediaCacheStatistics(
                    _options.DatabasePath,
                    EntryCount: 0,
                    SizeBytes:
                        GetDatabaseSizeBytes(),
                    IsAvailable: false,
                    Message:
                        "Der SQLite-Cache ist für diese Sitzung deaktiviert.");
            }

            await using var connection =
                CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using var command =
                connection.CreateCommand();

            command.CommandText =
                "SELECT COUNT(*) FROM image_metadata_cache_v2;";

            var scalar =
                await command.ExecuteScalarAsync(
                    cancellationToken);

            var entryCount =
                scalar is null
                || scalar is DBNull
                    ? 0
                    : Convert.ToInt64(
                        scalar,
                        System.Globalization.CultureInfo.InvariantCulture);

            return new MediaCacheStatistics(
                _options.DatabasePath,
                entryCount,
                GetDatabaseSizeBytes());
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SqliteException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            DisableCache();

            return new MediaCacheStatistics(
                _options.DatabasePath,
                EntryCount: 0,
                SizeBytes:
                    GetDatabaseSizeBytes(),
                IsAvailable: false,
                Message:
                    exception.Message);
        }
    }

    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        await _initializeGate.WaitAsync(
            cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            DeleteCacheFileIfPresent(
                _options.DatabasePath);

            DeleteCacheFileIfPresent(
                _options.DatabasePath + "-wal");

            DeleteCacheFileIfPresent(
                _options.DatabasePath + "-shm");

            Volatile.Write(
                ref _initialized,
                0);

            Volatile.Write(
                ref _disabled,
                0);
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private long GetDatabaseSizeBytes()
    {
        return GetFileLength(
                   _options.DatabasePath)
               + GetFileLength(
                   _options.DatabasePath + "-wal")
               + GetFileLength(
                   _options.DatabasePath + "-shm");
    }

    private static long GetFileLength(
        string path)
    {
        try
        {
            return File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return 0;
        }
    }

    private static void DeleteCacheFileIfPresent(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 1
            || Volatile.Read(ref _disabled) == 1)
        {
            return;
        }

        await _initializeGate.WaitAsync(cancellationToken);

        try
        {
            if (Volatile.Read(ref _initialized) == 1
                || Volatile.Read(ref _disabled) == 1)
            {
                return;
            }

            var directory =
                Path.GetDirectoryName(_options.DatabasePath)
                ?? throw new InvalidOperationException(
                    "Der Analyse-Cache hat kein gültiges Verzeichnis.");

            Directory.CreateDirectory(directory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;

                CREATE TABLE IF NOT EXISTS image_metadata_cache_v2
                (
                    path TEXT NOT NULL PRIMARY KEY,
                    file_length INTEGER NOT NULL,
                    last_write_utc_ticks INTEGER NOT NULL,
                    analysis_version INTEGER NOT NULL,
                    state INTEGER NOT NULL,
                    pixel_width INTEGER NULL,
                    pixel_height INTEGER NULL,
                    captured_at_ticks INTEGER NULL,
                    date_time_original_ticks INTEGER NULL,
                    date_time_digitized_ticks INTEGER NULL,
                    date_time_modified_ticks INTEGER NULL,
                    gps_date_time_utc_ticks INTEGER NULL,
                    camera_make TEXT NULL,
                    camera_model TEXT NULL,
                    lens_model TEXT NULL,
                    orientation INTEGER NULL,
                    has_exif INTEGER NOT NULL,
                    has_gps INTEGER NOT NULL,
                    latitude REAL NULL,
                    longitude REAL NULL,
                    warnings_json TEXT NULL,
                    error_message TEXT NULL,
                    cached_at_utc_ticks INTEGER NOT NULL
                );
                """;

            await command.ExecuteNonQueryAsync(cancellationToken);

            Volatile.Write(ref _initialized, 1);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SqliteException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            DisableCache();
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };

        return new SqliteConnection(builder.ToString());
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path);

    private static void AddParameter(
        SqliteCommand command,
        string name,
        object? value)
    {
        command.Parameters.AddWithValue(
            name,
            value ?? DBNull.Value);
    }

    private static int? GetNullableInt32(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);

    private static double? GetNullableDouble(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetDouble(ordinal);

    private static string? GetNullableString(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);

    private static DateTime? GetNullableDateTime(
        SqliteDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return new DateTime(
            reader.GetInt64(ordinal),
            DateTimeKind.Unspecified);
    }

    private static DateTime? GetNullableUtcDateTime(
        SqliteDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return new DateTime(
            reader.GetInt64(ordinal),
            DateTimeKind.Utc);
    }

    private static bool GetBoolean(
        SqliteDataReader reader,
        int ordinal) =>
        !reader.IsDBNull(ordinal)
        && reader.GetInt32(ordinal) != 0;

    private static IReadOnlyList<string> DeserializeWarnings(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                   ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private void DisableCache() =>
        Volatile.Write(ref _disabled, 1);
}
