namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public enum LibraryHealthIssueKind
{
    VolumeUnavailable = 0,
    PathMissing = 1,
    AmbiguousVolume = 2,
    InvalidConfiguration = 3,
    ScanError = 4,
    MetadataProblem = 5,
    MissingCaptureDate = 6
}
