namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageHealthSnapshot(
    string FilePath,
    StorageFailureKind FailureKind,
    StorageHealthSeverity Severity,
    string Summary,
    string? OperatingSystemMessage = null,
    string? MountPoint = null,
    string? FileSystemType = null,
    string? VolumeDevicePath = null,
    string? PhysicalDevicePath = null,
    string? Model = null,
    string? SerialNumber = null,
    bool? IsRotational = null,
    long? CapacityBytes = null,
    bool SmartDataWasQueried = false,
    string? SmartNote = null)
{
    public bool SuspectsPhysicalDevice =>
        FailureKind == StorageFailureKind.SuspectedDeviceIoFailure;

    public string? DisplayDevice =>
        !string.IsNullOrWhiteSpace(Model)
            ? Model
            : PhysicalDevicePath
              ?? VolumeDevicePath;
}
