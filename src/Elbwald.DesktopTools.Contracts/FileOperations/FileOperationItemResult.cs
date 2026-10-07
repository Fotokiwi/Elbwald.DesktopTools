namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationItemResult(
    FileOperationPlanItem Operation,
    FileOperationItemStatus Status,
    string? ErrorMessage = null);
