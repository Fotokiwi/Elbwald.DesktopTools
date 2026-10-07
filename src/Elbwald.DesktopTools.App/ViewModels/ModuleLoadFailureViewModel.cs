using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ModuleLoadFailureViewModel
{
    public ModuleLoadFailureViewModel(ModuleLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Name = string.IsNullOrWhiteSpace(result.ModuleId)
            ? Path.GetFileName(result.ModuleDirectory)
            : result.ModuleId;

        Message = result.Message;
    }

    public string Name { get; }

    public string Message { get; }
}
