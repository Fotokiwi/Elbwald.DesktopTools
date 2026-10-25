namespace Elbwald.DesktopTools.Contracts.Consolidation;

public sealed record MediaConsolidationPlanRequest(
    IReadOnlyList<string> SourcePaths,
    string TargetPath,
    MediaConsolidationStructureMode StructureMode = MediaConsolidationStructureMode.YearMonthOriginalName);
