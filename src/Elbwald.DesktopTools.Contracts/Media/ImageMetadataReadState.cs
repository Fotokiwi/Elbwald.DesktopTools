namespace Elbwald.DesktopTools.Contracts.Media;

public enum ImageMetadataReadState
{
    Success,
    NotImage,
    FileNotFound,
    AccessDenied,
    UnsupportedFormat,
    IoError,
    Failed
}
