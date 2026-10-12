using Elbwald.DesktopTools.Contracts.Settings;

namespace Elbwald.DesktopTools.App.Services;

public interface ISettingsContributionContentFactory
{
    object? CreateContent(SettingsContribution contribution);
}
