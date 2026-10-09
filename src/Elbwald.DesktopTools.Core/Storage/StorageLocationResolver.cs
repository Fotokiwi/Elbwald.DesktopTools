using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class StorageLocationResolver
    : IStorageLocationResolver
{
    private readonly IStorageVolumeProvider _volumeProvider;

    public StorageLocationResolver(
        IStorageVolumeProvider volumeProvider)
    {
        ArgumentNullException.ThrowIfNull(volumeProvider);
        _volumeProvider = volumeProvider;
    }

    public StorageLocation Capture(
        string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var fullPath =
            Path.GetFullPath(absolutePath);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Der Speicherort existiert nicht: {fullPath}");
        }

        var volume =
            _volumeProvider.FindVolumeForPath(fullPath)
            ?? throw new InvalidOperationException(
                "Für diesen Ordner konnte keine stabile Laufwerkskennung ermittelt werden. "
                + "Der Speicherort wurde aus Sicherheitsgründen nicht gespeichert.");

        if (string.IsNullOrWhiteSpace(volume.VolumeId))
        {
            throw new InvalidOperationException(
                "Das Laufwerk besitzt keine verwendbare stabile Kennung.");
        }

        var relativePath =
            Path.GetRelativePath(
                volume.MountPath,
                fullPath);

        if (relativePath == ".")
        {
            relativePath = string.Empty;
        }

        if (IsEscapingRelativePath(relativePath))
        {
            throw new InvalidOperationException(
                "Der ausgewählte Ordner liegt außerhalb des ermittelten Laufwerks.");
        }

        return new StorageLocation(
            volume.VolumeId,
            NormalizeStoredRelativePath(relativePath),
            volume.MountPath,
            fullPath);
    }

    public StorageLocationResolution Resolve(
        StorageLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (string.IsNullOrWhiteSpace(location.VolumeId)
            || !TryNormalizeRelativePath(
                location.RelativePath,
                out var relativePath))
        {
            return new StorageLocationResolution(
                StorageLocationStatus.InvalidConfiguration,
                null,
                null,
                "Die gespeicherte Laufwerkskonfiguration ist ungültig.");
        }

        var matches =
            _volumeProvider
                .GetAvailableVolumes()
                .Where(
                    volume => string.Equals(
                        volume.VolumeId,
                        location.VolumeId,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (matches.Length == 0)
        {
            return new StorageLocationResolution(
                StorageLocationStatus.VolumeUnavailable,
                null,
                null,
                "Das konfigurierte Laufwerk ist derzeit nicht verfügbar.");
        }

        if (matches.Length > 1)
        {
            return new StorageLocationResolution(
                StorageLocationStatus.AmbiguousVolume,
                null,
                null,
                "Die Laufwerkskennung wurde mehrfach gefunden. Der Speicherort wird aus Sicherheitsgründen nicht verwendet.");
        }

        var volume =
            matches[0];

        var resolvedPath =
            string.IsNullOrWhiteSpace(relativePath)
                ? Path.GetFullPath(volume.MountPath)
                : Path.GetFullPath(
                    Path.Combine(
                        volume.MountPath,
                        relativePath));

        if (!IsInsideVolume(
                resolvedPath,
                volume.MountPath))
        {
            return new StorageLocationResolution(
                StorageLocationStatus.InvalidConfiguration,
                null,
                volume,
                "Der konfigurierte relative Pfad verlässt das erkannte Laufwerk.");
        }

        if (!Directory.Exists(resolvedPath))
        {
            return new StorageLocationResolution(
                StorageLocationStatus.PathMissing,
                resolvedPath,
                volume,
                "Das richtige Laufwerk wurde erkannt, der konfigurierte Unterordner fehlt jedoch.");
        }

        return new StorageLocationResolution(
            StorageLocationStatus.Available,
            resolvedPath,
            volume,
            "Speicherort verfügbar und über stabile Laufwerkskennung verifiziert.");
    }

    private static string NormalizeStoredRelativePath(
        string relativePath)
    {
        return relativePath
            .Replace(
                Path.DirectorySeparatorChar,
                '/')
            .Replace(
                Path.AltDirectorySeparatorChar,
                '/');
    }

    private static bool TryNormalizeRelativePath(
        string? storedPath,
        out string relativePath)
    {
        relativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return true;
        }

        if (Path.IsPathRooted(storedPath))
        {
            return false;
        }

        var parts =
            storedPath
                .Split(
                    ['/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(
                part => part is "." or ".."))
        {
            return false;
        }

        relativePath =
            parts.Length == 0
                ? string.Empty
                : Path.Combine(parts);

        return true;
    }

    private static bool IsEscapingRelativePath(
        string relativePath)
    {
        return relativePath == ".."
               || relativePath.StartsWith(
                   ".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal)
               || relativePath.StartsWith(
                   ".." + Path.AltDirectorySeparatorChar,
                   StringComparison.Ordinal);
    }

    private static bool IsInsideVolume(
        string fullPath,
        string mountPath)
    {
        var normalizedMount =
            Path.GetFullPath(mountPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        var normalizedPath =
            Path.GetFullPath(fullPath);

        var comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (string.Equals(
                normalizedPath,
                normalizedMount,
                comparison))
        {
            return true;
        }

        return normalizedPath.StartsWith(
            normalizedMount
            + Path.DirectorySeparatorChar,
            comparison);
    }
}
