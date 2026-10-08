namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaAnalysisOptions
{
    public static MediaAnalysisOptions Default { get; } = new();

    public bool Recursive { get; init; } = true;

    public bool IncludeUnknownFiles { get; init; } = true;
}
