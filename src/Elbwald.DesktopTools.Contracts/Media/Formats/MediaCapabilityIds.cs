namespace Elbwald.DesktopTools.Contracts.Media.Formats;

/// <summary>
/// Well-known media capability identifiers shared by the host and extensions.
/// Format-specific capabilities such as media.image.webp.read remain separate
/// manifest declarations; this structural capability exposes format providers
/// to the central media format registry.
/// </summary>
public static class MediaCapabilityIds
{
    public const string FormatProvider = "media.format.provider";
}
