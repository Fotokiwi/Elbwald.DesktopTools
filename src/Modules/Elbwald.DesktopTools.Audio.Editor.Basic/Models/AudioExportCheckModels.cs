namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public enum AudioExportReadiness
{
    Ready,
    Review,
    NotReady
}

public sealed record AudioExportCheckResult(
    double IntegratedLufs,
    double TruePeakLeftDbtp,
    double TruePeakRightDbtp,
    long SamplesAboveFullScale,
    bool LimiterEnabled,
    double LimiterCeilingDbtp,
    MasterLimiterReductionSnapshot LimiterReduction,
    double DurationSeconds,
    AudioExportReadiness Readiness,
    IReadOnlyList<string> Hints)
{
    public double TruePeakMaxDbtp => Math.Max(TruePeakLeftDbtp, TruePeakRightDbtp);

    public bool HasClipping => SamplesAboveFullScale > 0 || TruePeakMaxDbtp >= 0d;
}
