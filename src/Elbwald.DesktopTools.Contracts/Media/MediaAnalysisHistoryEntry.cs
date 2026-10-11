namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaAnalysisHistoryEntry(
    string SourcePath,
    bool IncludeSubdirectories,
    bool IsFolder,
    DateTimeOffset CompletedUtc,
    int MediaCount,
    int ProblemCount);
