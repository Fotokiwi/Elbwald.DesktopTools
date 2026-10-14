using System.Globalization;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class ProjectTravelDayViewModel
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    public ProjectTravelDayViewModel(
        DateOnly date,
        int dayNumber,
        IReadOnlyList<ProjectJournalEntryItemViewModel> journalEntries,
        IReadOnlyList<ProjectMediaItemViewModel> media)
    {
        Date = date;
        DayNumber = dayNumber;
        JournalEntries = journalEntries ?? throw new ArgumentNullException(nameof(journalEntries));
        Media = media ?? throw new ArgumentNullException(nameof(media));
        MediaPreview = Media.Take(3).ToArray();

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

        PrimaryTitle = JournalEntries.FirstOrDefault()?.Entry.Title?.Trim() ?? string.Empty;
    }

    public DateOnly Date { get; }

    public int DayNumber { get; }

    public IReadOnlyList<ProjectJournalEntryItemViewModel> JournalEntries { get; }

    public IReadOnlyList<ProjectMediaItemViewModel> Media { get; }

    public IReadOnlyList<ProjectMediaItemViewModel> MediaPreview { get; }

    public string DayLabel => $"Tag {DayNumber:N0}";

    public string DateText => Date.ToString("dddd, dd. MMMM", GermanCulture);

    public bool HasJournalEntries => JournalEntries.Count > 0;

    public bool HasMedia => Media.Count > 0;

    public bool HasContent => HasJournalEntries || HasMedia;

    public bool HasNoContent => !HasContent;

    public bool HasLocation => !string.IsNullOrWhiteSpace(LocationText);

    public bool HasPrimaryTitle => !string.IsNullOrWhiteSpace(PrimaryTitle);

    public string LocationText { get; }

    public string PrimaryTitle { get; }

    public string ActivityText
    {
        get
        {
            if (!HasContent)
            {
                return "Noch nicht dokumentiert";
            }

            var parts = new List<string>(2);
            if (JournalEntries.Count > 0)
            {
                parts.Add(JournalEntries.Count == 1
                    ? "1 Tagebucheintrag"
                    : $"{JournalEntries.Count:N0} Tagebucheinträge");
            }

            if (Media.Count > 0)
            {
                parts.Add(Media.Count == 1
                    ? "1 Medium"
                    : $"{Media.Count:N0} Medien");
            }

            return string.Join(" · ", parts);
        }
    }
}
