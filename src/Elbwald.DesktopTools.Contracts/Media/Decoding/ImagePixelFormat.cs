namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

/// <summary>
/// Host-neutral pixel layouts that can cross module boundaries without depending
/// on Avalonia, ImageMagick or another concrete imaging backend.
/// </summary>
public enum ImagePixelFormat
{
    Rgba32,
    Bgra32,
    Rgba64
}
