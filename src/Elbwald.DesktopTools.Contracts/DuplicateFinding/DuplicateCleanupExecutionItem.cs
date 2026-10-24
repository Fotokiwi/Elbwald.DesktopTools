namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record DuplicateCleanupExecutionItem(
    long LocationId,
    string EndpointId,
    string RelativePath,
    string? SourcePath,
    string? TrashPath,
    DuplicateCleanupExecutionStatus Status,
    string Message,
    long MovedBytes)
{
    public bool Succeeded => Status == DuplicateCleanupExecutionStatus.MovedToTrash;
}
