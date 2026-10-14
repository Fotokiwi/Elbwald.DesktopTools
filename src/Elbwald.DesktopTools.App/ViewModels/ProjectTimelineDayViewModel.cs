using System.Globalization;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ProjectTimelineDayViewModel
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public ProjectTimelineDayViewModel(
        DateOnly date,
        DateOnly? projectStartDate,
        IReadOnlyList<ProjectJournalEntryItemViewModel> journalEntries,
        IReadOnlyList<ProjectMediaItemViewModel> media)
    {
        Date = date;
        JournalEntries = journalEntries ?? throw new ArgumentNullException(nameof(journalEntries));
        Media = media ?? throw new ArgumentNullException(nameof(media));
        MediaPreview = Media.Take(8).ToArray();

        DayNumberText = projectStartDate.HasValue && date >= projectStartDate.Value
            ? $"Tag {date.DayNumber - projectStartDate.Value.DayNumber + 1:N0}"
            : "Projekttag";

        var locations = JournalEntries
            .Select(entry => entry.Entry.Location?.Trim())
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToArray();

        LocationText = locations.Length switch
        {
            0 => string.Empty,
            1 => locations[0],
            _ => string.Join(" · ", locations.Take(2))
        };
    }

    public DateOnly Date { get; }

    public IReadOnlyList<ProjectJournalEntryItemViewModel> JournalEntries { get; }

    public IReadOnlyList<ProjectMediaItemViewModel> Media { get; }

    public IReadOnlyList<ProjectMediaItemViewModel> MediaPreview { get; }

    public string DayNumberText { get; }

    public string DateText => Date.ToString("dddd, dd. MMMM yyyy", GermanCulture);

    public string ShortDateText => Date.ToString("dd.MM.yyyy", GermanCulture);

    public bool HasJournalEntries => JournalEntries.Count > 0;

    public bool HasMedia => Media.Count > 0;

    public bool HasLocation => !string.IsNullOrWhiteSpace(LocationText);

    public string LocationText { get; }

    public string JournalCountText => JournalEntries.Count switch
    {
        0 => "Kein Tagebucheintrag",
        1 => "1 Tagebucheintrag",
        _ => $"{JournalEntries.Count:N0} Tagebucheinträge"
    };

    public string MediaCountText => Media.Count switch
    {
        0 => "Keine Medien",
        1 => "1 Medium",
        _ => $"{Media.Count:N0} Medien"
    };

    public bool HasMoreMedia => Media.Count > MediaPreview.Count;

    public string MoreMediaText => HasMoreMedia
        ? $"+ {Media.Count - MediaPreview.Count:N0} weitere"
        : string.Empty;
}
