namespace Elbwald.DesktopTools.Core.Media;

public sealed record MediaAnalysisCacheOptions
{
    public required string DatabasePath { get; init; }

    public int AnalysisVersion { get; init; } = 1;
}
