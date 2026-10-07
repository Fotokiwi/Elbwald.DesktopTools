namespace Elbwald.DesktopTools.Contracts.FileOperations;

public interface IFileOperationSafetyChecker
{
    FileOperationPreflightResult Check(FileOperationPlan plan);

    FileOperationPreflightResult CheckOperation(
        FileOperationPlanItem operation);
}
