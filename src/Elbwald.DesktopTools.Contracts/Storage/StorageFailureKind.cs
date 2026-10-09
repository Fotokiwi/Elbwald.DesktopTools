namespace Elbwald.DesktopTools.Contracts.Storage;

public enum StorageFailureKind
{
    Unknown,
    SourceReadFailure,
    FileAccessFailure,
    SuspectedDeviceIoFailure
}
