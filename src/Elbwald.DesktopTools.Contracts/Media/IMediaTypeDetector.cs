namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaTypeDetector
{
    MediaFileType Detect(
        string filePath);
}
