using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Media.Importing;

namespace Elbwald.DesktopTools.Core.Media.Importing;

public sealed class MediaImportPlanner : IMediaImportPlanner
{
    private const int HashBufferSize = 1024 * 1024;

    public async Task<MediaImportPlan> CreatePlanAsync(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);

        var source = NormalizeDirectory(sourceRoot);
        var destination = NormalizeDirectory(destinationRoot);

        if (!Directory.Exists(source))
        {
            return Blocked(source, destination, "Der Quellordner existiert nicht.", source);
        }

        if (!Directory.Exists(destination))
        {
            return Blocked(source, destination, "Die konfigurierte Import-Wartehalle existiert nicht.", destination);
        }

        if (PathsOverlap(source, destination))
        {
            return Blocked(
                source,
                destination,
                "Quelle und Import-Wartehalle dürfen sich nicht überschneiden. Wähle eine Quelle außerhalb der Wartehalle.");
        }

        var files = new List<string>();
        var issues = new List<MediaImportPlanIssue>();
        EnumerateFilesSafely(source, files, issues, cancellationToken);

        var complete = issues.Count == 0;
        var items = new List<MediaImportPlanItem>(files.Count);

        foreach (var file in files.OrderBy(path => path, GetPathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath;
            long length;

            try
            {
                relativePath = Path.GetRelativePath(source, file);
                if (!IsSafeRelativePath(relativePath))
                {
                    issues.Add(new MediaImportPlanIssue(
                        "Ein relativer Pfad würde die Import-Wartehalle verlassen. Der Import wird blockiert.",
                        file));
                    complete = false;
                    continue;
                }

                length = new FileInfo(file).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new MediaImportPlanIssue(
                    "Die Quelldatei konnte nicht zuverlässig gelesen werden: " + exception.Message,
                    file));
                complete = false;
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, relativePath));
            if (!IsPathWithinRoot(target, destination))
            {
                issues.Add(new MediaImportPlanIssue(
                    "Der berechnete Zielpfad liegt außerhalb der Import-Wartehalle. Der Import wird blockiert.",
                    file));
                complete = false;
                continue;
            }

            if (Directory.Exists(target))
            {
                items.Add(new MediaImportPlanItem(
                    file, target, relativePath, length,
                    MediaImportPlanItemState.Conflict,
                    "Am Ziel existiert bereits ein Verzeichnis mit diesem Namen."));
                continue;
            }

            if (!File.Exists(target))
            {
                items.Add(new MediaImportPlanItem(
                    file, target, relativePath, length,
                    MediaImportPlanItemState.ReadyToCopy,
                    "Wird sicher kopiert und anschließend verifiziert."));
                continue;
            }

            try
            {
                var targetInfo = new FileInfo(target);
                if (targetInfo.Length != length)
                {
                    items.Add(new MediaImportPlanItem(
                        file, target, relativePath, length,
                        MediaImportPlanItemState.Conflict,
                        "Gleicher Zielname, aber andere Dateigröße. Es wird nichts überschrieben."));
                    continue;
                }

                var sourceHash = await ComputeSha256Async(file, cancellationToken);
                var destinationHash = await ComputeSha256Async(target, cancellationToken);

                if (CryptographicOperations.FixedTimeEquals(sourceHash, destinationHash))
                {
                    items.Add(new MediaImportPlanItem(
                        file, target, relativePath, length,
                        MediaImportPlanItemState.AlreadyImported,
                        "Bitidentische Datei ist bereits in der Import-Wartehalle vorhanden."));
                }
                else
                {
                    items.Add(new MediaImportPlanItem(
                        file, target, relativePath, length,
                        MediaImportPlanItemState.Conflict,
                        "Gleicher Zielname, aber anderer Inhalt. Es wird nichts überschrieben."));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                issues.Add(new MediaImportPlanIssue(
                    "Ein vorhandenes Ziel konnte nicht sicher verglichen werden: " + exception.Message,
                    target));
                complete = false;
            }
        }

        return new MediaImportPlan(
            source,
            destination,
            DateTimeOffset.UtcNow,
            items,
            issues,
            complete);
    }

    private static void EnumerateFilesSafely(
        string root,
        ICollection<string> files,
        ICollection<MediaImportPlanIssue> issues,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(file);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            issues.Add(new MediaImportPlanIssue(
                                "Symbolische Links/Reparse-Points werden beim Import nicht verfolgt.",
                                file));
                            continue;
                        }
                        files.Add(Path.GetFullPath(file));
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        issues.Add(new MediaImportPlanIssue(
                            "Eine Datei konnte nicht zuverlässig geprüft werden: " + exception.Message,
                            file));
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            issues.Add(new MediaImportPlanIssue(
                                "Symbolische Verzeichnisse/Reparse-Points werden beim Import nicht verfolgt.",
                                child));
                            continue;
                        }
                        pending.Push(child);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        issues.Add(new MediaImportPlanIssue(
                            "Ein Unterordner konnte nicht zuverlässig geprüft werden: " + exception.Message,
                            child));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new MediaImportPlanIssue(
                    "Ein Ordner konnte nicht vollständig gelesen werden: " + exception.Message,
                    directory));
            }
        }
    }

    private static MediaImportPlan Blocked(
        string source,
        string destination,
        string message,
        string? path = null)
    {
        return new MediaImportPlan(
            source,
            destination,
            DateTimeOffset.UtcNow,
            Array.Empty<MediaImportPlanItem>(),
            new[] { new MediaImportPlanIssue(message, path) },
            false);
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool PathsOverlap(string first, string second) =>
        IsPathWithinRoot(first, second) || IsPathWithinRoot(second, first);

    private static bool IsPathWithinRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => segment is not ".." and not ".");
    }

    private static async Task<byte[]> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            HashBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}
