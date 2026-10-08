namespace Elbwald.DesktopTools.Contracts.Media;

public enum MediaThumbnailState
{
    Success,
    NotImage,
    FileNotFound,
    AccessDenied,
    UnsupportedFormat,
    IoError,
    Failed
}
