namespace Elbwald.DesktopTools.Contracts.Media.Exporting;

public sealed record ImageExportSupport(
    bool IsSupported,
    string? Reason = null)
{
    public static ImageExportSupport Supported { get; } = new(true);

    public static ImageExportSupport Unsupported(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new ImageExportSupport(false, reason);
    }
}
