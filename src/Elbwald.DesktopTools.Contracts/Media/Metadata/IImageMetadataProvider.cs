using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Metadata;

/// <summary>
/// Typed provider for image metadata reading. Providers decide whether they can
/// handle a concrete media file and are selected by the central registry.
/// </summary>
public interface IImageMetadataProvider : IImageMetadataReader
{
    /// <summary>
    /// Stable provider id used for diagnostics and deterministic ordering.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Higher values are preferred when multiple active providers can read the
    /// same file.
    /// </summary>
    int Priority { get; }

    bool CanRead(MediaFile file);
}
