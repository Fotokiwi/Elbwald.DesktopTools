using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class ModuleRegistry : IModuleRegistry
{
    private readonly List<IToolModule> _modules = [];

    public IReadOnlyCollection<IToolModule> Modules => _modules;

    public void Register(IToolModule module)
    {
        if (_modules.Any(
                x => string.Equals(
                    x.Id,
                    module.Id,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Module '{module.Id}' is already registered.");
        }

        _modules.Add(module);
    }
}
