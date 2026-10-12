namespace Elbwald.DesktopTools.Contracts.Settings;

public interface ISettingsContributionRegistry
{
    IReadOnlyList<SettingsContribution> Contributions { get; }
}
