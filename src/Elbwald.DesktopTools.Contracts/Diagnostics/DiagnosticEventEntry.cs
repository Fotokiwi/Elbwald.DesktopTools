namespace Elbwald.DesktopTools.Contracts.Diagnostics;

public sealed record DiagnosticEventEntry(
    long Id,
    DateTimeOffset OccurredAtUtc,
    DiagnosticEventSeverity Severity,
    DiagnosticEventCategory Category,
    string Source,
    string Message,
    string? Details,
    string? FilePath,
    string? DestinationPath,
    string? MountPoint,
    string? FileSystemType,
    string? VolumeDevicePath,
    string? PhysicalDevicePath,
    string? DeviceModel,
    string? DeviceSerialNumber,
    bool? IsRotational,
    long? CapacityBytes,
    string? OperationKind,
    string? CorrelationId,
    string? ErrorCode);
