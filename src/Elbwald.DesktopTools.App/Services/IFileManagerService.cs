namespace Elbwald.DesktopTools.App.Services;

public interface IFileManagerService
{
    bool TryOpenContainingFolder(string absolutePath, out string? errorMessage);
}
