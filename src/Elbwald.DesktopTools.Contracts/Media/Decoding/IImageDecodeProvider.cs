using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

/// <summary>
/// Decodes an image into a host-neutral raster frame. Implementations may live in
/// optional format modules. They must never modify the source file.
/// </summary>
public interface IImageDecodeProvider
{
    string ProviderId { get; }

    int Priority { get; }

    bool CanDecode(MediaFile file);

    Task<DecodedImageFrame> DecodeAsync(
        ImageDecodeRequest request,
        CancellationToken cancellationToken = default);
}
