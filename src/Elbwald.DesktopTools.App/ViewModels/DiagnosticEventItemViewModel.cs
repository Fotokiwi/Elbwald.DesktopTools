using Elbwald.DesktopTools.Contracts.Diagnostics;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class DiagnosticEventItemViewModel
{
    public DiagnosticEventItemViewModel(
        DiagnosticEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Id = entry.Id;
        OccurredAt = entry.OccurredAtUtc.ToLocalTime();
        Severity = FormatSeverity(entry.Severity);
        Category = FormatCategory(entry.Category);
        Source = entry.Source;
        Message = entry.Message;
        Details = entry.Details;
        FilePath = entry.FilePath;
        DestinationPath = entry.DestinationPath;
        Device = BuildDevice(entry);
        StorageContext = BuildStorageContext(entry);
        Operation = entry.OperationKind;
        ErrorCode = entry.ErrorCode;
    }

    public long Id { get; }

    public DateTimeOffset OccurredAt { get; }

    public string Timestamp =>
        OccurredAt.ToString("dd.MM.yyyy HH:mm:ss");

    public string Severity { get; }

    public string Category { get; }

    public string Source { get; }

    public string Message { get; }

    public string? Details { get; }

    public string? FilePath { get; }

    public string? DestinationPath { get; }

    public string? Device { get; }

    public string? StorageContext { get; }

    public string? Operation { get; }

    public string? ErrorCode { get; }

    public string? DeviceLine =>
        HasDevice
            ? "Datenträger: " + Device
            : null;

    public string? StorageContextLine =>
        HasStorageContext
            ? "Volume: " + StorageContext
            : null;

    public string? FilePathLine =>
        HasFilePath
            ? "Datei: " + FilePath
            : null;

    public string? DestinationPathLine =>
        HasDestinationPath
            ? "Ziel: " + DestinationPath
            : null;

    public string? OperationLine =>
        HasOperation
            ? "Aktion: " + Operation
            : null;

    public string? ErrorCodeLine =>
        HasErrorCode
            ? "Code: " + ErrorCode
            : null;

    public bool HasDetails =>
        !string.IsNullOrWhiteSpace(Details);

    public bool HasStorageContext =>
        !string.IsNullOrWhiteSpace(StorageContext);

    public bool HasFilePath =>
        !string.IsNullOrWhiteSpace(FilePath);

    public bool HasDestinationPath =>
        !string.IsNullOrWhiteSpace(DestinationPath);

    public bool HasDevice =>
        !string.IsNullOrWhiteSpace(Device);

    public bool HasOperation =>
        !string.IsNullOrWhiteSpace(Operation);

    public bool HasErrorCode =>
        !string.IsNullOrWhiteSpace(ErrorCode);

    private static string FormatSeverity(
        DiagnosticEventSeverity severity)
    {
        return severity switch
        {
            DiagnosticEventSeverity.Information => "Info",
            DiagnosticEventSeverity.Warning => "Warnung",
            DiagnosticEventSeverity.Error => "Fehler",
            DiagnosticEventSeverity.Critical => "Kritisch",
            _ => severity.ToString()
        };
    }

    private static string FormatCategory(
        DiagnosticEventCategory category)
    {
        return category switch
        {
            DiagnosticEventCategory.Application => "Anwendung",
            DiagnosticEventCategory.Storage => "Datenträger",
            DiagnosticEventCategory.FileOperation => "Dateioperation",
            DiagnosticEventCategory.Recovery => "Recovery",
            _ => category.ToString()
        };
    }

    private static string? BuildStorageContext(
        DiagnosticEventEntry entry)
    {
        var parts =
            new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.MountPoint))
        {
            parts.Add(entry.MountPoint);
        }

        if (!string.IsNullOrWhiteSpace(entry.FileSystemType))
        {
            parts.Add(entry.FileSystemType);
        }

        if (entry.IsRotational is not null)
        {
            parts.Add(
                entry.IsRotational.Value
                    ? "HDD"
                    : "SSD/Flash");
        }

        if (entry.CapacityBytes is not null)
        {
            parts.Add(
                FormatBytes(
                    entry.CapacityBytes.Value));
        }

        return parts.Count == 0
            ? null
            : string.Join(" · ", parts);
    }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }

    private static string? BuildDevice(
        DiagnosticEventEntry entry)
    {
        var parts =
            new[]
            {
                entry.DeviceModel,
                entry.PhysicalDevicePath,
                entry.VolumeDevicePath,
                entry.DeviceSerialNumber
            }
            .Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return parts.Length == 0
            ? null
            : string.Join(" · ", parts);
    }
}
