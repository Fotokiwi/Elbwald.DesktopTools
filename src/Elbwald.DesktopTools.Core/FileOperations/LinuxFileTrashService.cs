using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class LinuxFileTrashService : IFileTrashService
{
    public async Task<FileTrashResult> MoveToTrashAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsLinux())
        {
            return new FileTrashResult(
                false,
                fullPath,
                null,
                "Der Linux-Papierkorb ist auf diesem Betriebssystem nicht verfügbar. Die Datei wurde nicht verändert.");
        }

        if (!File.Exists(fullPath))
        {
            return new FileTrashResult(
                false,
                fullPath,
                null,
                "Die Datei ist vor dem Verschieben in den Papierkorb nicht mehr vorhanden.");
        }

        try
        {
            var target = ResolveTrashTarget(fullPath);
            Directory.CreateDirectory(target.FilesDirectory);
            Directory.CreateDirectory(target.InfoDirectory);
            EnsureNotSymbolicLink(target.TrashRoot);
            EnsureNotSymbolicLink(target.FilesDirectory);
            EnsureNotSymbolicLink(target.InfoDirectory);

            TryHardenPermissions(target.TrashRoot);
            TryHardenPermissions(target.FilesDirectory);
            TryHardenPermissions(target.InfoDirectory);

            var fileName = ReserveUniqueName(
                target.FilesDirectory,
                target.InfoDirectory,
                Path.GetFileName(fullPath));
            var trashPath = Path.Combine(target.FilesDirectory, fileName);
            var infoPath = Path.Combine(target.InfoDirectory, fileName + ".trashinfo");

            var info = BuildTrashInfo(target.InfoOriginalPath);
            await File.WriteAllTextAsync(
                infoPath,
                info,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CancellationToken.None);

            try
            {
                // Ab hier gibt es bewusst keinen Abbruch mehr: Info-Datei und
                // Datei-Move müssen als zusammengehörige Papierkorb-Aktion enden.
                File.Move(fullPath, trashPath, overwrite: false);
            }
            catch
            {
                TryDelete(infoPath);
                throw;
            }

            return new FileTrashResult(
                true,
                fullPath,
                trashPath,
                "Datei wurde nach FreeDesktop-Spezifikation in den Linux-Papierkorb verschoben.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            return new FileTrashResult(
                false,
                fullPath,
                null,
                "Linux-Papierkorb nicht verfügbar: " + exception.GetBaseException().Message + " Die Datei wurde nicht endgültig gelöscht.");
        }
    }

    private static TrashTarget ResolveTrashTarget(string fullPath)
    {
        var sourceMount = FindMountPoint(fullPath)
            ?? throw new InvalidOperationException("Der Einhängepunkt der Quelldatei konnte nicht bestimmt werden.");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException("Das Benutzerverzeichnis konnte nicht bestimmt werden.");
        }

        var homeMount = FindMountPoint(home);
        if (homeMount is not null && PathsEqual(sourceMount, homeMount))
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                dataHome = Path.Combine(home, ".local", "share");
            }

            var fullDataHome = Path.GetFullPath(dataHome);
            var dataHomeMount = FindMountPoint(fullDataHome);
            if (dataHomeMount is not null && PathsEqual(sourceMount, dataHomeMount))
            {
                var trashRoot = Path.Combine(fullDataHome, "Trash");
                return new TrashTarget(
                    trashRoot,
                    Path.Combine(trashRoot, "files"),
                    Path.Combine(trashRoot, "info"),
                    fullPath);
            }
        }

        var uid = GetEffectiveUserId();
        var volumeTrashRoot = Path.Combine(sourceMount, $".Trash-{uid.ToString(CultureInfo.InvariantCulture)}");
        var relative = Path.GetRelativePath(sourceMount, fullPath);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Der Quellpfad liegt nicht sicher innerhalb des ermittelten Einhängepunkts.");
        }

        return new TrashTarget(
            volumeTrashRoot,
            Path.Combine(volumeTrashRoot, "files"),
            Path.Combine(volumeTrashRoot, "info"),
            relative);
    }

    private static string? FindMountPoint(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
        {
            fullPath = Path.GetDirectoryName(fullPath) ?? fullPath;
        }

        if (!File.Exists("/proc/self/mountinfo"))
        {
            return Path.GetPathRoot(fullPath);
        }

        string? best = null;
        foreach (var line in File.ReadLines("/proc/self/mountinfo"))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 6)
            {
                continue;
            }

            var mountPoint = DecodeMountInfoPath(fields[4]);
            if (!IsPathWithin(fullPath, mountPoint))
            {
                continue;
            }

            if (best is null || mountPoint.Length > best.Length)
            {
                best = mountPoint;
            }
        }

        return best;
    }

    private static string DecodeMountInfoPath(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\'
                && index + 3 < value.Length
                && IsOctal(value[index + 1])
                && IsOctal(value[index + 2])
                && IsOctal(value[index + 3]))
            {
                var decoded = ((value[index + 1] - '0') << 6)
                    + ((value[index + 2] - '0') << 3)
                    + (value[index + 3] - '0');
                builder.Append((char)decoded);
                index += 3;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private static bool IsOctal(char value) => value is >= '0' and <= '7';

    private static bool IsPathWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);

        if (string.Equals(fullPath, fullRoot, StringComparison.Ordinal))
        {
            return true;
        }

        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.Ordinal);

    private static string ReserveUniqueName(
        string filesDirectory,
        string infoDirectory,
        string requestedName)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? "elbwald-file"
            : requestedName;

        for (var suffix = 0; suffix < 100_000; suffix++)
        {
            var candidate = suffix == 0
                ? baseName
                : baseName + "." + suffix.ToString(CultureInfo.InvariantCulture);
            var filePath = Path.Combine(filesDirectory, candidate);
            var infoPath = Path.Combine(infoDirectory, candidate + ".trashinfo");

            if (!File.Exists(filePath)
                && !Directory.Exists(filePath)
                && !File.Exists(infoPath))
            {
                return candidate;
            }
        }

        throw new IOException("Für den Papierkorb konnte kein freier Dateiname reserviert werden.");
    }

    private static string BuildTrashInfo(string originalPath)
    {
        var escapedPath = Uri.EscapeDataString(originalPath)
            .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);
        var deletionDate = DateTime.Now.ToString(
            "yyyy-MM-dd'T'HH:mm:ss",
            CultureInfo.InvariantCulture);

        return "[Trash Info]\n"
            + "Path=" + escapedPath + "\n"
            + "DeletionDate=" + deletionDate + "\n";
    }

    private static void EnsureNotSymbolicLink(string path)
    {
        var info = new DirectoryInfo(path);
        if (!string.IsNullOrWhiteSpace(info.LinkTarget))
        {
            throw new IOException($"Der Papierkorbpfad '{path}' ist ein symbolischer Link und wird aus Sicherheitsgründen nicht verwendet.");
        }
    }

    private static void TryHardenPermissions(string path)
    {
        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
        {
            // Die Plattform-/Mount-Berechtigungen sind maßgeblich; ein nicht
            // unterstützter chmod darf den Papierkorb nicht künstlich blockieren.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: Die Quelldatei wurde in diesem Fehlerpfad nicht bewegt.
        }
    }

    private static uint GetEffectiveUserId() => geteuid();

    [DllImport("libc", SetLastError = false)]
    private static extern uint geteuid();

    private sealed record TrashTarget(
        string TrashRoot,
        string FilesDirectory,
        string InfoDirectory,
        string InfoOriginalPath);
}
