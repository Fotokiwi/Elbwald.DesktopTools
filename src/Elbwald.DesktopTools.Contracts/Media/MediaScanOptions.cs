namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaScanOptions
{
    public static MediaScanOptions Default { get; } = new();

    public bool Recursive { get; init; } = true;

    public bool IncludeUnknownFiles { get; init; } = true;
}
