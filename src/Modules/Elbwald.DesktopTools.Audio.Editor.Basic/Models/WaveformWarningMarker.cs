namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public enum WaveformWarningSeverity
{
    Warning,
    Critical
}

public sealed record WaveformWarningMarker(
    double TimeSeconds,
    double LevelDb,
    WaveformWarningSeverity Severity,
    string? Label = null,
    double DurationSeconds = 0d,
    bool IsSustained = false);
