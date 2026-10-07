namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationPlanItem(
    int Index,
    FileOperationKind Kind,
    string SourcePath,
    string DestinationPath);
