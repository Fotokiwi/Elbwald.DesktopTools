using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class LinuxStorageVolumeProvider
    : IStorageVolumeProvider
{
    public StorageVolumeInfo? FindVolumeForPath(
        string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var fullPath = Path.GetFullPath(absolutePath);

        return GetAvailableVolumes()
            .Where(volume => IsPathInsideMount(fullPath, volume.MountPath))
            .OrderByDescending(volume => volume.MountPath.Length)
            .FirstOrDefault();
    }

    public IReadOnlyList<StorageVolumeInfo> GetAvailableVolumes()
    {
        if (!OperatingSystem.IsLinux()
            || !File.Exists("/proc/self/mountinfo"))
        {
            return Array.Empty<StorageVolumeInfo>();
        }

        var byUuid = BuildUuidMap();
        var result = new List<StorageVolumeInfo>();

        foreach (var line in File.ReadLines("/proc/self/mountinfo"))
        {
            var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                continue;
            }

            var left = line[..separatorIndex]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var right = line[(separatorIndex + 3)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (left.Length < 5 || right.Length < 2)
            {
                continue;
            }

            var mountPoint = DecodeMountInfoPath(left[4]);
            var fileSystem = right[0];
            var source = DecodeMountInfoPath(right[1]);

            if (!source.StartsWith("/dev/", StringComparison.Ordinal))
            {
                continue;
            }

            var realSource = TryResolveRealPath(source);
            if (string.IsNullOrWhiteSpace(realSource)
                || !byUuid.TryGetValue(realSource, out var uuid)
                || string.IsNullOrWhiteSpace(uuid))
            {
                continue;
            }

            long? capacity = null;
            try
            {
                var drive = new DriveInfo(mountPoint);
                if (drive.IsReady)
                {
                    capacity = drive.TotalSize;
                }
            }
            catch
            {
                // Anzeige-Metadaten sind optional. Die UUID bleibt maßgeblich.
            }

            result.Add(
                new StorageVolumeInfo(
                    "linux-fs-uuid:" + uuid,
                    mountPoint,
                    fileSystem,
                    source,
                    capacity));
        }

        return result
            .GroupBy(
                volume => new
                {
                    Id = volume.VolumeId.ToUpperInvariant(),
                    Mount = volume.MountPath
                })
            .Select(group => group.First())
            .ToArray();
    }

    private static Dictionary<string, string> BuildUuidMap()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        const string uuidDirectory = "/dev/disk/by-uuid";

        try
        {
            if (!Directory.Exists(uuidDirectory))
            {
                return result;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(uuidDirectory))
            {
                var realPath = TryResolveRealPath(entry);
                if (string.IsNullOrWhiteSpace(realPath))
                {
                    continue;
                }

                var uuid = Path.GetFileName(entry);
                if (string.IsNullOrWhiteSpace(uuid))
                {
                    continue;
                }

                result[realPath] = uuid;
            }
        }
        catch
        {
            // Ohne verlässliche UUID wird das Volume absichtlich nicht angeboten.
        }

        return result;
    }

    private static string? TryResolveRealPath(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return Path.GetFullPath(target?.FullName ?? info.FullName);
        }
        catch
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }
    }

    private static bool IsPathInsideMount(
        string fullPath,
        string mountPoint)
    {
        if (string.Equals(fullPath, mountPoint, StringComparison.Ordinal))
        {
            return true;
        }

        if (mountPoint == "/")
        {
            return fullPath.StartsWith("/", StringComparison.Ordinal);
        }

        var prefix = mountPoint.EndsWith(Path.DirectorySeparatorChar)
            ? mountPoint
            : mountPoint + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string DecodeMountInfoPath(string value)
    {
        return value
            .Replace("\\040", " ", StringComparison.Ordinal)
            .Replace("\\011", "\t", StringComparison.Ordinal)
            .Replace("\\012", "\n", StringComparison.Ordinal)
            .Replace("\\134", "\\", StringComparison.Ordinal);
    }
}
