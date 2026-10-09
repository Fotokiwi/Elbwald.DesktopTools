namespace Elbwald.DesktopTools.Contracts.Storage;

public enum StorageLocationStatus
{
    Available = 0,
    VolumeUnavailable = 1,
    PathMissing = 2,
    AmbiguousVolume = 3,
    InvalidConfiguration = 4
}
