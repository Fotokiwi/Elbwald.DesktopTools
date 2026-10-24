namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public enum DuplicateCleanupPreflightStatus
{
    Ready = 0,
    EndpointMissing = 1,
    EndpointUnavailable = 2,
    EndpointDisabled = 3,
    ProtectedEndpoint = 4,
    VolumeChanged = 5,
    InvalidRelativePath = 6,
    FileMissing = 7,
    SymbolicLinkUnsupported = 8,
    LengthMismatch = 9,
    HashMismatch = 10,
    ReadOnly = 11,
    AccessDenied = 12,
    IoError = 13,
    IndexChanged = 14
}
