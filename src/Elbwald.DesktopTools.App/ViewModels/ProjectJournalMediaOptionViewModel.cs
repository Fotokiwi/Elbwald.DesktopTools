using CommunityToolkit.Mvvm.ComponentModel;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectJournalMediaOptionViewModel : ObservableObject
{
    public ProjectJournalMediaOptionViewModel(
        string mediaItemId,
        string title,
        string detail,
        bool isSelected)
    {
        MediaItemId = mediaItemId;
        Title = title;
        Detail = detail;
        this.isSelected = isSelected;
    }

    public string MediaItemId { get; }

    public string Title { get; }

    public string Detail { get; }

    [ObservableProperty]
    private bool isSelected;
}
