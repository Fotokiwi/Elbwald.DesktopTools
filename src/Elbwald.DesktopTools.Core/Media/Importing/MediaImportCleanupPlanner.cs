using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Media.Importing;

public sealed class MediaImportCleanupPlanner : IMediaImportCleanupPlanner
{
    private readonly IStorageVolumeProvider _volumeProvider;

    public MediaImportCleanupPlanner(IStorageVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);
        _volumeProvider = volumeProvider;
    }

    public async Task<MediaImportCleanupPlan> CreatePlanAsync(
        MediaImportPlan importPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(importPlan);

        var sourceRoot = NormalizeDirectory(importPlan.SourceRoot);
        var destinationRoot = NormalizeDirectory(importPlan.DestinationRoot);
        var issues = new List<MediaImportPlanIssue>();
        var items = new List<MediaImportCleanupPlanItem>();

        if (!Directory.Exists(sourceRoot))
        {
            return Blocked(sourceRoot, destinationRoot, "Die ursprüngliche Importquelle ist nicht mehr verfügbar.", sourceRoot);
        }

        if (!Directory.Exists(destinationRoot))
        {
            return Blocked(sourceRoot, destinationRoot, "Die Import-Wartehalle ist nicht mehr verfügbar.", destinationRoot);
        }

        if (PathsOverlap(sourceRoot, destinationRoot))
        {
            return Blocked(sourceRoot, destinationRoot, "Quelle und Import-Wartehalle überschneiden sich. Quellbereinigung ist gesperrt.");
        }

        var sourceVolume = _volumeProvider.FindVolumeForPath(sourceRoot);
        var destinationVolume = _volumeProvider.FindVolumeForPath(destinationRoot);
        if (sourceVolume is null || string.IsNullOrWhiteSpace(sourceVolume.VolumeId))
        {
            return Blocked(sourceRoot, destinationRoot, "Der Datenträger der Importquelle konnte nicht eindeutig identifiziert werden.", sourceRoot);
        }
        if (destinationVolume is null || string.IsNullOrWhiteSpace(destinationVolume.VolumeId))
        {
            return Blocked(sourceRoot, destinationRoot, "Der Datenträger der Import-Wartehalle konnte nicht eindeutig identifiziert werden.", destinationRoot);
        }

        var candidates = importPlan.Items
            .Where(item => item.State == MediaImportPlanItemState.ReadyToCopy)
            .OrderBy(item => item.RelativePath, GetPathComparer())
            .ToArray();

        if (candidates.Length == 0)
        {
            issues.Add(new MediaImportPlanIssue("Dieser Import enthält keine neu kopierten Dateien, die für eine Quellbereinigung freigegeben werden können."));
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var source = Path.GetFullPath(candidate.SourcePath);
                var destination = Path.GetFullPath(candidate.DestinationPath);

                if (!IsPathWithinRoot(source, sourceRoot) || !IsPathWithinRoot(destination, destinationRoot))
                {
                    issues.Add(new MediaImportPlanIssue("Ein Dateipfad liegt außerhalb der erwarteten Quelle oder Import-Wartehalle.", candidate.RelativePath));
                    continue;
                }

                if (!File.Exists(source) || !File.Exists(destination))
                {
                    issues.Add(new MediaImportPlanIssue("Quelle oder verifizierte Zielkopie ist nicht mehr vorhanden.", candidate.RelativePath));
                    continue;
                }

                var sourceFileVolume = _volumeProvider.FindVolumeForPath(source);
                var destinationFileVolume = _volumeProvider.FindVolumeForPath(destination);
                if (sourceFileVolume is null || !string.Equals(sourceFileVolume.VolumeId, sourceVolume.VolumeId, StringComparison.Ordinal)
                    || destinationFileVolume is null || !string.Equals(destinationFileVolume.VolumeId, destinationVolume.VolumeId, StringComparison.Ordinal))
                {
                    issues.Add(new MediaImportPlanIssue("Datei liegt auf einem unerwarteten eingebundenen Datenträger. Quellbereinigung wird blockiert.", candidate.RelativePath));
                    continue;
                }

                if (IsReparsePoint(source) || IsReparsePoint(destination))
                {
                    issues.Add(new MediaImportPlanIssue("Symbolische Links/Reparse-Points sind für die Quellbereinigung nicht zulässig.", candidate.RelativePath));
                    continue;
                }

                var sourceInfo = new FileInfo(source);
                var destinationInfo = new FileInfo(destination);
                if (sourceInfo.Length != candidate.Length || destinationInfo.Length != candidate.Length)
                {
                    issues.Add(new MediaImportPlanIssue("Dateigröße hat sich seit der Importvorschau verändert.", candidate.RelativePath));
                    continue;
                }

                var sourceHash = await ComputeSha256HexAsync(source, cancellationToken);
                var destinationHash = await ComputeSha256HexAsync(destination, cancellationToken);
                if (!string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new MediaImportPlanIssue("Quelle und Zielkopie sind nicht mehr bitidentisch.", candidate.RelativePath));
                    continue;
                }

                items.Add(new MediaImportCleanupPlanItem(source, destination, candidate.RelativePath, candidate.Length, sourceHash));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException or NotSupportedException)
            {
                issues.Add(new MediaImportPlanIssue("Datei konnte für die Quellbereinigung nicht sicher verifiziert werden: " + exception.Message, candidate.RelativePath));
            }
        }

        var complete = issues.Count == 0 && items.Count == candidates.Length && items.Count > 0;
        return new MediaImportCleanupPlan(
            sourceRoot,
            destinationRoot,
            sourceVolume.VolumeId,
            destinationVolume.VolumeId,
            DateTimeOffset.UtcNow,
            items,
            issues,
            complete);
    }

    private static MediaImportCleanupPlan Blocked(string sourceRoot, string destinationRoot, string message, string? path = null) =>
        new(sourceRoot, destinationRoot, string.Empty, string.Empty, DateTimeOffset.UtcNow,
            Array.Empty<MediaImportCleanupPlanItem>(),
            new[] { new MediaImportPlanIssue(message, path) },
            false);

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string NormalizeDirectory(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsOverlap(string first, string second) => IsPathWithinRoot(first, second) || IsPathWithinRoot(second, first);

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, comparison)
               || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static StringComparer GetPathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
