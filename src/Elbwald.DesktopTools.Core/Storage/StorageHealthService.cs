using Elbwald.DesktopTools.Contracts.Diagnostics;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.Storage;

public sealed class StorageHealthService
    : IStorageHealthService
{
    private readonly IDiagnosticEventStore _diagnosticEventStore;

    public StorageHealthService(
        IDiagnosticEventStore diagnosticEventStore)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEventStore);
        _diagnosticEventStore = diagnosticEventStore;
    }

    public async Task<StorageHealthSnapshot> InspectFailureAsync(
        string filePath,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(exception);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath =
            Path.GetFullPath(filePath);

        var sourceRead =
            SourceReadIOException.Find(
                exception);

        var rootException =
            sourceRead?.InnerException
            ?? exception.GetBaseException();

        var classification =
            Classify(
                rootException);

        var device =
            OperatingSystem.IsLinux()
                ? TryReadLinuxDeviceInfo(
                    fullPath)
                : TryReadPortableVolumeInfo(
                    fullPath);

        var summary =
            classification.Kind
                == StorageFailureKind.SuspectedDeviceIoFailure
                ? "Das Betriebssystem meldet einen möglichen Datenträger-I/O-Fehler. "
                  + "Die betroffene Quelldatei bzw. Dateigruppe sollte nicht verändert werden."
                : classification.Kind
                    == StorageFailureKind.FileAccessFailure
                    ? "Die Quelldatei konnte nicht gelesen werden. Die betroffene Dateigruppe wird nicht verändert."
                    : "Die Quelldatei konnte nicht zuverlässig gelesen werden. Die betroffene Dateigruppe wird nicht verändert.";

        var snapshot =
            new StorageHealthSnapshot(
                fullPath,
                classification.Kind,
                classification.Severity,
                summary,
                rootException.Message,
                device.MountPoint,
                device.FileSystemType,
                device.VolumeDevicePath,
                device.PhysicalDevicePath,
                device.Model,
                device.SerialNumber,
                device.IsRotational,
                device.CapacityBytes,
                SmartDataWasQueried: false,
                SmartNote:
                    "SMART-/Selbsttests werden bei einem I/O-Fehler nicht automatisch gestartet. "
                    + "Die App liest zunächst nur vorhandene Betriebssystem- und Geräteinformationen.");

        await TryWriteDiagnosticEventAsync(
            snapshot,
            rootException,
            cancellationToken);

        return snapshot;
    }

    private async Task TryWriteDiagnosticEventAsync(
        StorageHealthSnapshot snapshot,
        Exception rootException,
        CancellationToken cancellationToken)
    {
        var severity =
            snapshot.Severity switch
            {
                StorageHealthSeverity.Information =>
                    DiagnosticEventSeverity.Information,

                StorageHealthSeverity.Warning =>
                    DiagnosticEventSeverity.Warning,

                StorageHealthSeverity.Critical =>
                    DiagnosticEventSeverity.Critical,

                _ =>
                    DiagnosticEventSeverity.Warning
            };

        var errorCode =
            rootException is IOException
                ? $"0x{rootException.HResult:X8}"
                : rootException.GetType().Name;

        try
        {
            await _diagnosticEventStore.TryWriteAsync(
                new DiagnosticEventWrite(
                    DateTimeOffset.UtcNow,
                    severity,
                    DiagnosticEventCategory.Storage,
                    "StorageHealth",
                    snapshot.Summary,
                    rootException.Message,
                    FilePath: snapshot.FilePath,
                    MountPoint: snapshot.MountPoint,
                    FileSystemType: snapshot.FileSystemType,
                    VolumeDevicePath: snapshot.VolumeDevicePath,
                    PhysicalDevicePath: snapshot.PhysicalDevicePath,
                    DeviceModel: snapshot.Model,
                    DeviceSerialNumber: snapshot.SerialNumber,
                    IsRotational: snapshot.IsRotational,
                    CapacityBytes: snapshot.CapacityBytes,
                    OperationKind: "SourceRead",
                    ErrorCode: errorCode),
                cancellationToken);
        }
        catch
        {
            // Ein Diagnosefehler darf die eigentliche Storage-Einschätzung
            // und niemals eine Dateioperation beeinflussen.
        }
    }

    private static FailureClassification Classify(
        Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return new FailureClassification(
                StorageFailureKind.FileAccessFailure,
                StorageHealthSeverity.Warning);
        }

        if (exception is IOException ioException)
        {
            var nativeCode =
                ioException.HResult
                & 0xFFFF;

            if ((OperatingSystem.IsLinux()
                 && nativeCode == 5)
                || (OperatingSystem.IsWindows()
                    && nativeCode is 23 or 1117 or 1392)
                || ContainsDeviceIoText(
                    ioException.Message))
            {
                return new FailureClassification(
                    StorageFailureKind.SuspectedDeviceIoFailure,
                    StorageHealthSeverity.Critical);
            }

            return new FailureClassification(
                StorageFailureKind.SourceReadFailure,
                StorageHealthSeverity.Warning);
        }

        return new FailureClassification(
            StorageFailureKind.Unknown,
            StorageHealthSeverity.Warning);
    }

    private static bool ContainsDeviceIoText(
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains(
                   "input/output error",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "eingabe-/ausgabefehler",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "i/o device error",
                   StringComparison.OrdinalIgnoreCase)
               || message.Contains(
                   "crc error",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static StorageDeviceInfo TryReadPortableVolumeInfo(
        string fullPath)
    {
        try
        {
            var root =
                Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return StorageDeviceInfo.Empty;
            }

            var drive =
                new DriveInfo(root);

            return new StorageDeviceInfo(
                root,
                drive.IsReady
                    ? drive.DriveFormat
                    : null,
                drive.Name,
                PhysicalDevicePath: null,
                Model: null,
                SerialNumber: null,
                IsRotational: null,
                CapacityBytes:
                    drive.IsReady
                        ? drive.TotalSize
                        : null);
        }
        catch
        {
            return StorageDeviceInfo.Empty;
        }
    }

    private static StorageDeviceInfo TryReadLinuxDeviceInfo(
        string fullPath)
    {
        try
        {
            var mount =
                FindLinuxMount(
                    fullPath);

            if (mount is null)
            {
                return TryReadPortableVolumeInfo(
                    fullPath);
            }

            var volumeDevice =
                mount.Source.StartsWith(
                    "/dev/",
                    StringComparison.Ordinal)
                    ? mount.Source
                    : null;

            var volumeName =
                volumeDevice is null
                    ? null
                    : Path.GetFileName(
                        volumeDevice);

            var physicalName =
                TryResolveLinuxPhysicalBlockName(
                    volumeName);

            if (string.IsNullOrWhiteSpace(
                    physicalName))
            {
                return new StorageDeviceInfo(
                    mount.MountPoint,
                    mount.FileSystemType,
                    volumeDevice,
                    PhysicalDevicePath: null,
                    Model: null,
                    SerialNumber: null,
                    IsRotational: null,
                    CapacityBytes: null);
            }

            var physicalDevice =
                "/dev/"
                + physicalName;

            var sysBlock =
                Path.Combine(
                    "/sys/class/block",
                    physicalName);

            return new StorageDeviceInfo(
                mount.MountPoint,
                mount.FileSystemType,
                volumeDevice,
                physicalDevice,
                ReadTrimmedText(
                    Path.Combine(
                        sysBlock,
                        "device",
                        "model")),
                ReadTrimmedText(
                    Path.Combine(
                        sysBlock,
                        "device",
                        "serial")),
                ParseRotational(
                    ReadTrimmedText(
                        Path.Combine(
                            sysBlock,
                            "queue",
                            "rotational"))),
                ParseCapacityBytes(
                    ReadTrimmedText(
                        Path.Combine(
                            sysBlock,
                            "size"))));
        }
        catch
        {
            return TryReadPortableVolumeInfo(
                fullPath);
        }
    }

    private static LinuxMountInfo? FindLinuxMount(
        string fullPath)
    {
        if (!File.Exists(
                "/proc/self/mountinfo"))
        {
            return null;
        }

        LinuxMountInfo? best =
            null;

        foreach (var line in File.ReadLines(
                     "/proc/self/mountinfo"))
        {
            var separatorIndex =
                line.IndexOf(
                    " - ",
                    StringComparison.Ordinal);

            if (separatorIndex < 0)
            {
                continue;
            }

            var left =
                line[..separatorIndex]
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);

            var right =
                line[(separatorIndex + 3)..]
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries);

            if (left.Length < 5
                || right.Length < 2)
            {
                continue;
            }

            var mountPoint =
                DecodeMountInfoPath(
                    left[4]);

            if (!IsPathInsideMount(
                    fullPath,
                    mountPoint))
            {
                continue;
            }

            var candidate =
                new LinuxMountInfo(
                    mountPoint,
                    right[0],
                    DecodeMountInfoPath(
                        right[1]));

            if (best is null
                || candidate.MountPoint.Length
                > best.MountPoint.Length)
            {
                best =
                    candidate;
            }
        }

        return best;
    }

    private static bool IsPathInsideMount(
        string fullPath,
        string mountPoint)
    {
        var comparison =
            StringComparison.Ordinal;

        if (string.Equals(
                fullPath,
                mountPoint,
                comparison))
        {
            return true;
        }

        if (mountPoint == "/")
        {
            return fullPath.StartsWith(
                "/",
                comparison);
        }

        var prefix =
            mountPoint.EndsWith(
                Path.DirectorySeparatorChar)
                ? mountPoint
                : mountPoint
                  + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            prefix,
            comparison);
    }

    private static string DecodeMountInfoPath(
        string value)
    {
        return value
            .Replace(
                "\\040",
                " ",
                StringComparison.Ordinal)
            .Replace(
                "\\011",
                "\t",
                StringComparison.Ordinal)
            .Replace(
                "\\012",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                "\\134",
                "\\",
                StringComparison.Ordinal);
    }

    private static string? TryResolveLinuxPhysicalBlockName(
        string? volumeName)
    {
        if (string.IsNullOrWhiteSpace(
                volumeName))
        {
            return null;
        }

        var sysVolume =
            Path.Combine(
                "/sys/class/block",
                volumeName);

        if (!Directory.Exists(
                sysVolume))
        {
            return null;
        }

        var partitionMarker =
            Path.Combine(
                sysVolume,
                "partition");

        if (!File.Exists(
                partitionMarker))
        {
            return volumeName;
        }

        var target =
            new DirectoryInfo(
                sysVolume)
                .ResolveLinkTarget(
                    returnFinalTarget: true);

        return target is null
            ? volumeName
            : Directory.GetParent(
                    target.FullName)
                ?.Name
              ?? volumeName;
    }

    private static string? ReadTrimmedText(
        string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var value =
                File.ReadAllText(path)
                    .Trim();

            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }
        catch
        {
            return null;
        }
    }

    private static bool? ParseRotational(
        string? value)
    {
        return value switch
        {
            "0" => false,
            "1" => true,
            _ => null
        };
    }

    private static long? ParseCapacityBytes(
        string? sectorCount)
    {
        if (!long.TryParse(
                sectorCount,
                out var sectors)
            || sectors < 0)
        {
            return null;
        }

        try
        {
            return checked(
                sectors * 512L);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private sealed record FailureClassification(
        StorageFailureKind Kind,
        StorageHealthSeverity Severity);

    private sealed record LinuxMountInfo(
        string MountPoint,
        string FileSystemType,
        string Source);

    private sealed record StorageDeviceInfo(
        string? MountPoint,
        string? FileSystemType,
        string? VolumeDevicePath,
        string? PhysicalDevicePath,
        string? Model,
        string? SerialNumber,
        bool? IsRotational,
        long? CapacityBytes)
    {
        public static StorageDeviceInfo Empty { get; } =
            new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
    }
}
