using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Settings;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SettingsContributionItemViewModel
{
    public SettingsContributionItemViewModel(
        SettingsContribution contribution,
        ISettingsContributionContentFactory contentFactory)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(contentFactory);

        ExtensionId = contribution.ExtensionId;
        Id = contribution.Id;
        Title = contribution.Title;
        Description = contribution.Description;
        Icon = contribution.Icon;
        Content = contentFactory.CreateContent(contribution);
    }

    public string ExtensionId { get; }
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Icon { get; }
    public object? Content { get; }
    public bool HasContent => Content is not null;
}
