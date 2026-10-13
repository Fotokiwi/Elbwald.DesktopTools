using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ProjectJournalEntryItemViewModel
{
    public ProjectJournalEntryItemViewModel(
        ProjectJournalEntry entry,
        int mediaCount)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        MediaCount = mediaCount;
    }

    public ProjectJournalEntry Entry { get; }

    public string Id => Entry.Id;

    public string Title => Entry.Title;

    public string DateText => Entry.EntryDate.ToString("dd.MM.yyyy");

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

            return body.Length <= 110
                ? body
                : body[..107] + "…";
        }
    }

    public int MediaCount { get; }

    public string MediaCountText => MediaCount switch
    {
        0 => "Keine Medien",
        1 => "1 Medium",
        _ => $"{MediaCount:N0} Medien"
    };
}
