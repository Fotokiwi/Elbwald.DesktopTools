namespace Elbwald.DesktopTools.Contracts.Media;

public enum RawPreviewState
{
    Success,
    NotRaw,
    FileNotFound,
    AccessDenied,
    PreviewNotFound,
    InvalidFormat,
    IoError,
    Failed
}
