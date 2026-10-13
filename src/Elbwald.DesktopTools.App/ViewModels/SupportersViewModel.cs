namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SupportersViewModel
{
    public SupportersViewModel()
    {
        Supporters =
        [
            new SupporterWallEntryViewModel("AM", "Anna M.", "14. Apr. 2026", "Förderer", "50,00 €"),
            new SupporterWallEntryViewModel("MM", "Markus M.", "12. Apr. 2026", "Wegbegleiter", "25,00 €"),
            new SupporterWallEntryViewModel("SK", "Sven K.", "10. Apr. 2026", "Förderer", "100,00 €"),
            new SupporterWallEntryViewModel("JR", "Jana R.", "8. Apr. 2026", "Waldfreund", "10,00 €"),
            new SupporterWallEntryViewModel("FO", "Fotofreund", "5. Apr. 2026", "Waldfreund", "5,00 €"),
            new SupporterWallEntryViewModel("TL", "Tobias L.", "2. Apr. 2026", "Wegbegleiter", "75,00 €")
        ];
    }

    public string TotalSupportText => "1.284,50 €";

    public string SupporterCountText => "37";

    public string PlaceholderNotice =>
        "Platzhalteransicht · Beispieldaten · noch keine PayPal-Verbindung";

    public IReadOnlyList<SupporterWallEntryViewModel> Supporters { get; }
}
