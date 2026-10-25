using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

/// <summary>
/// Decode request used by editor previews and later full-resolution rendering.
/// MaxEdge is a decode hint only; null requests the original raster dimensions.
/// </summary>
public sealed record ImageDecodeRequest(
    MediaFile File,
    int? MaxEdge = null,
    bool ApplyEmbeddedOrientation = true);
