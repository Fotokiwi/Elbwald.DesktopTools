using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

public interface IModuleRegistry
{
    IReadOnlyCollection<IToolModule> Modules { get; }

    void Register(IToolModule module);
}
