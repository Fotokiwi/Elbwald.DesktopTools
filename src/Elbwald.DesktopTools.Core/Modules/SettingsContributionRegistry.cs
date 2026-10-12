using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Settings;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed class SettingsContributionRegistry : ISettingsContributionRegistry
{
    private readonly SettingsContribution[] _contributions;
    private readonly IExtensionRegistry _extensionRegistry;

    public SettingsContributionRegistry(
        IEnumerable<SettingsContribution> contributions,
        IExtensionRegistry extensionRegistry)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(extensionRegistry);

        _extensionRegistry = extensionRegistry;
        _contributions = contributions
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<SettingsContribution> Contributions =>
        _contributions
            .Where(item => _extensionRegistry.IsEnabled(item.ExtensionId))
            .ToArray();
}
