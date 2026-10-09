namespace Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;

public sealed class MediaSortExecutionGroup
{
    public MediaSortExecutionGroup(
        string id,
        string displayName,
        bool isCompanionGroup,
        bool requiresAtomicExecution,
        IEnumerable<MediaSortExecutionOperation> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(operations);

        Id = id;
        DisplayName = displayName;
        IsCompanionGroup = isCompanionGroup;
        RequiresAtomicExecution = requiresAtomicExecution;
        Operations = operations.ToArray();
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool IsCompanionGroup { get; }

    public bool RequiresAtomicExecution { get; }

    public IReadOnlyList<MediaSortExecutionOperation> Operations { get; }

    public long TotalBytes =>
        Operations.Sum(operation =>
            operation.SourceSnapshot.Length);

    public int SidecarCount =>
        Operations.Count(operation =>
            operation.IsSidecar);
}
