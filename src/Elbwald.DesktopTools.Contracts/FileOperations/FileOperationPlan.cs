namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed class FileOperationPlan
{
    public FileOperationPlan(
        IEnumerable<FileOperationPlanItem> operations,
        IEnumerable<FileOperationConflict> conflicts)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(conflicts);

        Operations = operations.ToArray();
        Conflicts = conflicts.ToArray();
    }

    public IReadOnlyList<FileOperationPlanItem> Operations { get; }

    public IReadOnlyList<FileOperationConflict> Conflicts { get; }

    public bool CanExecute => Conflicts.Count == 0;

    public int MoveCount =>
        Operations.Count(operation => operation.Kind == FileOperationKind.Move);

    public int CopyCount =>
        Operations.Count(operation => operation.Kind == FileOperationKind.Copy);
}
