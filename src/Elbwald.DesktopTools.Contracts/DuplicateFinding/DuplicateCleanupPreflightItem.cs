namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateCleanupPreflightItem(
    long LocationId,
    DuplicateCleanupAction Action,
    string EndpointId,
    string RelativePath,
    string? ResolvedPath,
    DuplicateCleanupPreflightStatus Status,
    string Message)
{
    public bool IsReady => Status == DuplicateCleanupPreflightStatus.Ready;
}
