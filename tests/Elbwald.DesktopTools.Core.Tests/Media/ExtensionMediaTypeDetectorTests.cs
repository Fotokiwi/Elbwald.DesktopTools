using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class ExtensionMediaTypeDetectorTests
{
    private readonly Elbwald.DesktopTools.Core.Media.ExtensionMediaTypeDetector _detector =
        TestMediaFormatServices.CreateMediaTypeDetector();

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("photo.JPEG")]
    [InlineData("photo.png")]
    [InlineData("photo.tiff")]
    [InlineData("photo.HEIC")]
    [InlineData("photo.dng")]
    [InlineData("photo.CR3")]
    [InlineData("photo.nef")]
    [InlineData("photo.arw")]
    public void Detect_KnownImageExtension_ReturnsImage(
        string fileName)
    {
        var result = _detector.Detect(fileName);

        Assert.Equal(
            MediaFileType.Image,
            result);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("video.mp4")]
    [InlineData("audio.mp3")]
    [InlineData("photo.webp")]
    [InlineData("README")]
    public void Detect_UnsupportedExtension_ReturnsUnknown(
        string fileName)
    {
        var result = _detector.Detect(fileName);

        Assert.Equal(
            MediaFileType.Unknown,
            result);
    }
}
