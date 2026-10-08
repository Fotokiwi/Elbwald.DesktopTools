namespace Elbwald.DesktopTools.UI.Formatting;

public static class ProcessingDurationFormatter
{
    public static string Format(
        TimeSpan duration,
        bool includeTenths = false)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalHours =
            (long)duration.TotalHours;

        var baseText =
            $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

        if (!includeTenths)
        {
            return baseText;
        }

        return $"{baseText}.{duration.Milliseconds / 100}";
    }
}
