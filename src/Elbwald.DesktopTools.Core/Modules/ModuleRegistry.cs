using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class ModuleRegistry : IModuleRegistry
{
    private readonly List<IToolModule> _modules = [];
    private readonly Func<string, bool> _isEnabled;

    public ModuleRegistry()
        : this(_ => true)
    {
    }

    public ModuleRegistry(Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(isEnabled);
        _isEnabled = isEnabled;
    }

    public IReadOnlyCollection<IToolModule> Modules =>
        _modules
            .Where(module => _isEnabled(module.Id))
            .ToArray();

    public void Register(IToolModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

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
