namespace Elbwald.DesktopTools.Contracts.Consolidation;

public sealed record MediaConsolidationPlanItem(
    string MediaItemId,
    string Sha256,
    long Length,
    string SourcePath,
    IReadOnlyList<string> SourceOccurrences,
    string TargetRelativePath,
    string TargetPath,
    DateTime EffectiveMediaDate,
    bool UsesCaptureDate,
    MediaConsolidationPlanItemStatus Status,
    string Message);
