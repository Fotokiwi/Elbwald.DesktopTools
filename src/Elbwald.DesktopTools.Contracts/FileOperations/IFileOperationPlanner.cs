namespace Elbwald.DesktopTools.Contracts.FileOperations;

public interface IFileOperationPlanner
{
    FileOperationPlan CreatePlan(
        IEnumerable<FileOperationRequest> requests);
}
