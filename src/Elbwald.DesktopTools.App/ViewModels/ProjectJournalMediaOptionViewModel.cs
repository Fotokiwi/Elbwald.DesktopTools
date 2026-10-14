using CommunityToolkit.Mvvm.ComponentModel;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectJournalMediaOptionViewModel : ObservableObject
{
    public ProjectJournalMediaOptionViewModel(
        string mediaItemId,
        string title,
        string detail,
        bool isSelected,
        DateOnly? projectDate = null,
        DateOnly? captureDate = null)
    {
        MediaItemId = mediaItemId;
        Title = title;
        Detail = detail;
        ProjectDate = projectDate;
        CaptureDate = captureDate;
        this.isSelected = isSelected;
    }

    public string MediaItemId { get; }

    public string Title { get; }

    public string Detail { get; }

    public DateOnly? ProjectDate { get; }

    public DateOnly? CaptureDate { get; }

    public bool HasDayHint => ProjectDate.HasValue || CaptureDate.HasValue;

    public string DayHintText
    {
        get
        {
            if (ProjectDate.HasValue)
            {
                return $"Projekt-Tag · {ProjectDate.Value:dd.MM.yyyy}";
            }

            return CaptureDate.HasValue
                ? $"Aufnahmedatum · {CaptureDate.Value:dd.MM.yyyy}"
                : string.Empty;
        }
    }

    public string DaySuggestionText => IsSuggestedForEntryDay
        ? ProjectDate.HasValue
            ? "Passt zum Projekttag"
            : "Passt laut Aufnahmedatum"
        : string.Empty;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DaySuggestionText))]
    private bool isSuggestedForEntryDay;

    public void UpdateEntryDate(DateOnly? entryDate)
    {
        IsSuggestedForEntryDay = entryDate.HasValue
            && (ProjectDate.HasValue
                ? ProjectDate.Value == entryDate.Value
                : CaptureDate.HasValue && CaptureDate.Value == entryDate.Value);
    }
}
