using System.Runtime.InteropServices;
using System.Text;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class WindowsStorageVolumeProvider
    : IStorageVolumeProvider
{
    public StorageVolumeInfo? FindVolumeForPath(
        string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var fullPath = Path.GetFullPath(absolutePath);
        var volumePathBuffer = new StringBuilder(1024);

        if (!GetVolumePathNameW(
                fullPath,
                volumePathBuffer,
                (uint)volumePathBuffer.Capacity))
        {
            return null;
        }

        return CreateVolumeInfo(volumePathBuffer.ToString());
    }

    public IReadOnlyList<StorageVolumeInfo> GetAvailableVolumes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<StorageVolumeInfo>();
        }

        var result = new List<StorageVolumeInfo>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var volume = CreateVolumeInfo(drive.RootDirectory.FullName);
                if (volume is not null)
                {
                    result.Add(volume);
                }
            }
            catch
            {
                // Nicht bereite/removable Laufwerke werden übersprungen.
            }
        }

        return result;
    }

    private static StorageVolumeInfo? CreateVolumeInfo(string mountPath)
    {
        var normalizedMount = Path.GetPathRoot(mountPath);
        if (string.IsNullOrWhiteSpace(normalizedMount))
        {
            return null;
        }

        var volumeNameBuffer = new StringBuilder(1024);

        if (!GetVolumeNameForVolumeMountPointW(
                normalizedMount,
                volumeNameBuffer,
                (uint)volumeNameBuffer.Capacity))
        {
            return null;
        }

        var volumeName = volumeNameBuffer.ToString();
        if (string.IsNullOrWhiteSpace(volumeName))
        {
            return null;
        }

        string? fileSystem = null;
        string? displayName = null;
        long? capacity = null;

        try
        {
            var drive = new DriveInfo(normalizedMount);
            if (drive.IsReady)
            {
                fileSystem = drive.DriveFormat;
                displayName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.Name
                    : drive.VolumeLabel;
                capacity = drive.TotalSize;
            }
        }
        catch
        {
            // Optionale Anzeige-Metadaten dürfen die stabile Kennung nicht beeinflussen.
        }

        return new StorageVolumeInfo(
            "windows-volume-guid:" + volumeName.TrimEnd('\\'),
            normalizedMount,
            fileSystem,
            displayName,
            capacity);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName,
        StringBuilder lpszVolumePathName,
        uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string lpszVolumeMountPoint,
        StringBuilder lpszVolumeName,
        uint cchBufferLength);
}
