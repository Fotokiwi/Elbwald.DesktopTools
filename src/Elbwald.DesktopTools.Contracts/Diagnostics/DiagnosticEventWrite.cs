namespace Elbwald.DesktopTools.Contracts.Diagnostics;

public sealed record DiagnosticEventWrite(
    DateTimeOffset OccurredAtUtc,
    DiagnosticEventSeverity Severity,
    DiagnosticEventCategory Category,
    string Source,
    string Message,
    string? Details = null,
    string? FilePath = null,
    string? DestinationPath = null,
    string? MountPoint = null,
    string? FileSystemType = null,
    string? VolumeDevicePath = null,
    string? PhysicalDevicePath = null,
    string? DeviceModel = null,
    string? DeviceSerialNumber = null,
    bool? IsRotational = null,
    long? CapacityBytes = null,
    string? OperationKind = null,
    string? CorrelationId = null,
    string? ErrorCode = null);
