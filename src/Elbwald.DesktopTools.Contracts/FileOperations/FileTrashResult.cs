namespace Elbwald.DesktopTools.Contracts.FileOperations;

public sealed record FileTrashResult(
    bool Succeeded,
    string SourcePath,
    string? TrashPath,
    string Message);
