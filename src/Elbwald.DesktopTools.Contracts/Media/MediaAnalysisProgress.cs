namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaAnalysisProgress(
    MediaAnalysisStage Stage,
    int FilesDiscovered,
    int MetadataProcessed,
    int MetadataTotal,
    string? CurrentPath = null,
    int MetadataCacheHits = 0,
    int MetadataCacheMisses = 0);
