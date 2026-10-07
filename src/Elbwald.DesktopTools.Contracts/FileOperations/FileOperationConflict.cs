namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationConflict(
    FileOperationConflictKind Kind,
    int OperationIndex,
    int? RelatedOperationIndex = null);
