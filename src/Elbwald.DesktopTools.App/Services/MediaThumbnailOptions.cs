namespace Elbwald.DesktopTools.App.Services;

public sealed record MediaThumbnailOptions
{
    public required string CacheDirectory { get; init; }

    public int DefaultWidth { get; init; } = 640;

    public int MaximumWidth { get; init; } = 1600;
}
