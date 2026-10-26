namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public sealed record MasteringLevelHistoryPoint(
    double TimeSeconds,
    double LeftPeakDbfs,
    double RightPeakDbfs);

public sealed record MasteringTrackAnalysisResult(
    Guid TrackId,
    string TrackName,
    double? IntegratedLufs,
    double TruePeakLeftDbtp,
    double TruePeakRightDbtp)
{
    public double TruePeakMaxDbtp => Math.Max(TruePeakLeftDbtp, TruePeakRightDbtp);

    public string TruePeakReadout =>
        TruePeakMaxDbtp <= -119.9 ? "– dBTP" : $"{TruePeakMaxDbtp:0.0} dBTP";

    public string IntegratedReadout =>
        IntegratedLufs is null ? "– LUFS" : $"{IntegratedLufs.Value:0.0} LUFS";
}


public sealed record MasteringPeakEventContribution(
    Guid TrackId,
    string TrackName,
    double PeakDbfs,
    double ContributionPercent)
{
    public string PeakReadout => PeakDbfs <= -119.9 ? "– dBFS" : $"{PeakDbfs:0.0} dBFS";

    public string ContributionReadout => $"{ContributionPercent:0}%";
}

public sealed record MasteringPeakEventResult(
    double TimeSeconds,
    double ApproxTruePeakDbtp,
    IReadOnlyList<MasteringPeakEventContribution> Contributions)
{
    public string TimeReadout
    {
        get
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, TimeSeconds));
            return time.TotalHours >= 1
                ? time.ToString(@"hh\:mm\:ss\.fff")
                : time.ToString(@"mm\:ss\.fff");
        }
    }

    public string TruePeakReadout =>
        ApproxTruePeakDbtp <= -119.9 ? "– dBTP" : $"≈ {ApproxTruePeakDbtp:0.0} dBTP";

    public IReadOnlyList<MasteringPeakEventContribution> TopContributions =>
        Contributions.Take(4).ToArray();

    public string ContributorsSummary => TopContributions.Count == 0
        ? "Keine eindeutigen Spurbeiträge"
        : string.Join(" · ", TopContributions.Take(3).Select(item =>
            $"{item.TrackName} {item.ContributionPercent:0}%"));
}


public sealed record MasteringPeakWarningEventResult(
    double StartSeconds,
    double EndSeconds,
    double MaxTruePeakDbtp,
    bool IsCritical,
    bool IsSustained)
{
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);

    public double MarkerTimeSeconds => StartSeconds + DurationSeconds / 2.0;

    public string TimeReadout
    {
        get
        {
            var time = TimeSpan.FromSeconds(Math.Max(0, MarkerTimeSeconds));
            return time.TotalHours >= 1
                ? time.ToString(@"hh\:mm\:ss\.fff")
                : time.ToString(@"mm\:ss\.fff");
        }
    }

    public string LevelReadout =>
        MaxTruePeakDbtp <= -119.9 ? "– dBTP" : $"≈ {MaxTruePeakDbtp:0.0} dBTP";

    public string KindLabel => IsCritical
        ? IsSustained ? "längere Übersteuerung" : "kritischer Transient"
        : "Vorwarnung";
}

public sealed record MasteringAnalysisResult(
    double IntegratedLufs,
    double? MaxShortTermLufs,
    double? MaxMomentaryLufs,
    double TruePeakLeftDbtp,
    double TruePeakRightDbtp,
    double PreLimiterIntegratedLufs,
    double PreLimiterTruePeakLeftDbtp,
    double PreLimiterTruePeakRightDbtp,
    double DurationSeconds,
    MasterLimiterReductionSnapshot LimiterReduction,
    IReadOnlyList<MasteringTrackAnalysisResult> Tracks,
    IReadOnlyList<MasteringPeakEventResult> PeakEvents,
    IReadOnlyList<MasteringPeakWarningEventResult> PeakWarningEvents,
    IReadOnlyList<MasteringLevelHistoryPoint> PreLimiterLevelHistory,
    IReadOnlyList<MasteringLevelHistoryPoint> LevelHistory)
{
    public double TruePeakMaxDbtp => Math.Max(TruePeakLeftDbtp, TruePeakRightDbtp);

    public double PreLimiterTruePeakMaxDbtp =>
        Math.Max(PreLimiterTruePeakLeftDbtp, PreLimiterTruePeakRightDbtp);
}
