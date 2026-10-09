using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortDateContextHintViewModel
{
    public PhotoSortDateContextHintViewModel(
        MediaDateContextHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);

        DirectoryPath =
            hint.DirectoryPath;

        CameraLabel =
            hint.CameraLabel;

        Offset =
            FormatDifference(
                hint.DominantDifference);

        Coverage =
            $"{hint.MatchingFileCount:N0} / {hint.ComparableFileCount:N0} · {hint.CoverageRatio:P0}";

        Outliers =
            hint.OutlierCount > 0
                ? $"{hint.OutlierCount:N0} Ausreißer"
                : "Keine starken Ausreißer";

        Strength =
            hint.Strength
            == MediaDateContextStrength.High
                ? "Stark"
                : "Plausibel";

        IsHigh =
            hint.Strength
            == MediaDateContextStrength.High;

        IsMedium =
            !IsHigh;

        Message =
            hint.Message;
    }

    public string DirectoryPath { get; }

    public string CameraLabel { get; }

    public string Offset { get; }

    public string Coverage { get; }

    public string Outliers { get; }

    public string Strength { get; }

    public bool IsHigh { get; }

    public bool IsMedium { get; }

    public string Message { get; }

    private static string FormatDifference(
        TimeSpan difference)
    {
        var sign =
            difference < TimeSpan.Zero
                ? "-"
                : "+";

        var absolute =
            difference.Duration();

        if (absolute.TotalDays >= 1)
        {
            return $"{sign}{(int)absolute.TotalDays}d "
                   + $"{absolute.Hours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
        }

        return $"{sign}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
    }
}
