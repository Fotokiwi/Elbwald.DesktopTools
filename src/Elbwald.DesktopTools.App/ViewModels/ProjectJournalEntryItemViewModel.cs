using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectJournalEntryItemViewModel : ObservableObject
{
    public ProjectJournalEntryItemViewModel(
        ProjectJournalEntry entry,
        int mediaCount,
        bool showDayHeader = false)
    {
        this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        this.mediaCount = mediaCount;
        this.showDayHeader = showDayHeader;
    }

    [ObservableProperty]
    private ProjectJournalEntry entry;

    [ObservableProperty]
    private int mediaCount;

    [ObservableProperty]
    private bool showDayHeader;

    public string Id => Entry.Id;

    public string Title => Entry.Title;

    public string DateText => Entry.EntryDate.ToString("dd.MM.yyyy");

    public string DayHeaderText => Entry.EntryDate.ToString("dddd, dd. MMMM yyyy");

    public string LocationText => string.IsNullOrWhiteSpace(Entry.Location)
        ? "Kein Ort angegeben"
        : Entry.Location;

    public string PreviewText
    {
        get
        {
            var body = Entry.Body;
            if (string.IsNullOrWhiteSpace(body))
            {
                return "Noch keine Notiz.";
            }

            var plain = body
                .Replace("**", string.Empty, StringComparison.Ordinal)
                .Replace("*", string.Empty, StringComparison.Ordinal)
                .Replace("## ", string.Empty, StringComparison.Ordinal)
                .Replace("> ", string.Empty, StringComparison.Ordinal)
                .Replace("- ", string.Empty, StringComparison.Ordinal);

            return plain.Length <= 110
                ? plain
                : plain[..107] + "…";
        }
    }

    public string MediaCountText => MediaCount switch
    {
        0 => "Keine Medien",
        1 => "1 Medium",
        _ => $"{MediaCount:N0} Medien"
    };

    public void Update(
        ProjectJournalEntry updatedEntry,
        int updatedMediaCount)
    {
        Entry = updatedEntry ?? throw new ArgumentNullException(nameof(updatedEntry));
        MediaCount = updatedMediaCount;
    }

    partial void OnEntryChanged(ProjectJournalEntry value)
    {
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(DayHeaderText));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(PreviewText));
    }

    partial void OnMediaCountChanged(int value)
    {
        OnPropertyChanged(nameof(MediaCountText));
    }
}
