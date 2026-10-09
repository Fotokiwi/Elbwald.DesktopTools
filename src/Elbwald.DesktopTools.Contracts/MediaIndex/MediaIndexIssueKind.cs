namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public enum MediaIndexIssueKind
{
    EndpointUnavailable = 0,
    ScanError = 1,
    FileReadError = 2,
    InvalidRelativePath = 3,
    IndexStoreError = 4,
    SymbolicLinkSkipped = 5
}
