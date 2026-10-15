using Elbwald.DesktopTools.Contracts.Maintenance;
using Elbwald.DesktopTools.Contracts.Paths;
using Microsoft.Data.Sqlite;

namespace Elbwald.DesktopTools.Core.Maintenance;

public sealed class ApplicationDataResetService : IApplicationDataResetService
{
    private static readonly string[] VolatileConfigFileNames =
    [
        "media-analysis-history.json",
        "media-import-history.json"
    ];

    private readonly IAppPaths _appPaths;

    public ApplicationDataResetService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _appPaths = appPaths;
    }

    public Task<ApplicationDataResetResult> ResetVolatileDataAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var deletedFiles = 0;
        long deletedBytes = 0;
        var clearedTargets = new List<string>();

        // SQLite may still have pooled handles even though all Elbwald stores open
        // short-lived connections. Clear the pools before touching database files so
        // the reset behaves the same on Linux and Windows.
        SqliteConnection.ClearAllPools();

        ClearDirectoryContents(
            _appPaths.CacheDirectory,
            "Cache",
            clearedTargets,
            ref deletedFiles,
            ref deletedBytes,
            cancellationToken);

        ClearDirectoryContents(
            _appPaths.DatabaseDirectory,
            "Datenbanken",
            clearedTargets,
            ref deletedFiles,
            ref deletedBytes,
            cancellationToken);

        ClearDiagnosticDatabase(
            clearedTargets,
            ref deletedFiles,
            ref deletedBytes,
            cancellationToken);

        ClearVolatileHistoryFiles(
            clearedTargets,
            ref deletedFiles,
            ref deletedBytes,
            cancellationToken);

        Directory.CreateDirectory(_appPaths.CacheDirectory);
        Directory.CreateDirectory(_appPaths.DatabaseDirectory);

        return Task.FromResult(
            new ApplicationDataResetResult(
                deletedFiles,
                deletedBytes,
                clearedTargets));
    }

    private void ClearDirectoryContents(
        string directory,
        string displayName,
        ICollection<string> clearedTargets,
        ref int deletedFiles,
        ref long deletedBytes,
        CancellationToken cancellationToken)
    {
        var safeDirectory = EnsureChildPath(directory);

        if (!Directory.Exists(safeDirectory))
        {
            clearedTargets.Add(displayName);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     safeDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            deletedFiles++;
            deletedBytes += TryGetFileLength(file);
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(safeDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        clearedTargets.Add(displayName);
    }

    private void ClearDiagnosticDatabase(
        ICollection<string> clearedTargets,
        ref int deletedFiles,
        ref long deletedBytes,
        CancellationToken cancellationToken)
    {
        var diagnosticsDirectory = EnsureChildPath(_appPaths.DiagnosticsDirectory);
        var databasePath = Path.Combine(diagnosticsDirectory, "events.db");

        foreach (var path in new[]
                 {
                     databasePath,
                     databasePath + "-wal",
                     databasePath + "-shm"
                 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFileIfPresent(path, ref deletedFiles, ref deletedBytes);
        }

        clearedTargets.Add("Diagnose-Datenbank");
    }

    private void ClearVolatileHistoryFiles(
        ICollection<string> clearedTargets,
        ref int deletedFiles,
        ref long deletedBytes,
        CancellationToken cancellationToken)
    {
        var configDirectory = EnsureChildPath(_appPaths.ConfigDirectory);

        foreach (var fileName in VolatileConfigFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = Path.Combine(configDirectory, fileName);
            DeleteFileIfPresent(path, ref deletedFiles, ref deletedBytes);

            foreach (var temporaryPath in Directory.Exists(configDirectory)
                         ? Directory.EnumerateFiles(
                             configDirectory,
                             fileName + ".tmp.*",
                             SearchOption.TopDirectoryOnly)
                         : Array.Empty<string>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteFileIfPresent(temporaryPath, ref deletedFiles, ref deletedBytes);
            }
        }

        clearedTargets.Add("Analyse-/Import-Komfortverlauf");
    }

    private void DeleteFileIfPresent(
        string path,
        ref int deletedFiles,
        ref long deletedBytes)
    {
        var safePath = EnsureChildPath(path);

        if (!File.Exists(safePath))
        {
            return;
        }

        deletedFiles++;
        deletedBytes += TryGetFileLength(safePath);
        File.Delete(safePath);
    }

    private string EnsureChildPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var root = Path.GetFullPath(_appPaths.ApplicationDataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = root + Path.DirectorySeparatorChar;

        if (string.Equals(candidate, root, comparison)
            || !candidate.StartsWith(prefix, comparison))
        {
            throw new InvalidOperationException(
                $"Reset-Ziel liegt außerhalb des Elbwald-Anwendungsdatenverzeichnisses: {candidate}");
        }

        return candidate;
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
