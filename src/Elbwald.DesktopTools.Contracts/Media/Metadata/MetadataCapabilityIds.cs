namespace Elbwald.DesktopTools.Contracts.Media.Metadata;

/// <summary>
/// Well-known structural metadata capability identifiers shared by the host
/// and extensions. Format-specific declarations such as
/// media.image.webp.metadata.read remain separate manifest capabilities.
/// </summary>
public static class MetadataCapabilityIds
{
    public const string ImageReaderProvider = "media.metadata.image.reader-provider";
}
