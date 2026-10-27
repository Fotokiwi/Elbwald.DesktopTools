namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public enum StemRepairFindingType
{
    NoiseFloor,
    Hum,
    NarrowbandInterference,
    Sibilance
}

public enum StemRepairFindingSeverity
{
    Info,
    Low,
    Medium,
    High
}

public sealed record StemRepairFinding(
    StemRepairFindingType Type,
    StemRepairFindingSeverity Severity,
    double Confidence,
    string Title,
    string Detail,
    string Recommendation,
    double? FrequencyHz = null,
    double? SecondaryFrequencyHz = null,
    double? SuggestedStrength = null,
    double? SuggestedThresholdDb = null,
    double? SuggestedReductionDb = null,
    int? SuggestedHarmonics = null,
    double? SuggestedWidthPercent = null,
    double? Persistence = null,
    double? SuggestedAuditionTimeSeconds = null);

public sealed record StemRepairAnalysisResult(
    Guid TrackId,
    string TrackName,
    double TrackDurationSeconds,
    double AnalyzedDurationSeconds,
    int AnalysisSampleRate,
    double ActiveLevelDbfs,
    double NoiseFloorDbfs,
    IReadOnlyList<StemRepairFinding> Findings)
{
    public int FindingCount => Findings.Count;
}
