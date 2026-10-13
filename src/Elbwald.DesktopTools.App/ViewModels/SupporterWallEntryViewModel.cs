namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class SupporterWallEntryViewModel
{
    public SupporterWallEntryViewModel(
        string initials,
        string displayName,
        string dateText,
        string tier,
        string amountText)
    {
        Initials = initials;
        DisplayName = displayName;
        DateText = dateText;
        Tier = tier;
        AmountText = amountText;
    }

    public string Initials { get; }

    public string DisplayName { get; }

    public string DateText { get; }

    public string Tier { get; }

    public string AmountText { get; }
}
