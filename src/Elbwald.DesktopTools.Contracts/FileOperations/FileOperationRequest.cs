namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileOperationRequest(
    FileOperationKind Kind,
    string SourcePath,
    string DestinationPath)
{
    public static FileOperationRequest Move(
        string sourcePath,
        string destinationPath)
    {
        return new FileOperationRequest(
            FileOperationKind.Move,
            sourcePath,
            destinationPath);
    }

    public static FileOperationRequest Copy(
        string sourcePath,
        string destinationPath)
    {
        return new FileOperationRequest(
            FileOperationKind.Copy,
            sourcePath,
            destinationPath);
    }
}
