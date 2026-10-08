namespace Elbwald.DesktopTools.Contracts.Media;

public sealed class MediaThumbnailResult
{
    public MediaThumbnailResult(
        MediaThumbnailState state,
        byte[]? encodedImage = null,
        string? mimeType = null,
        bool isFromCache = false,
        string? errorMessage = null)
    {
        State = state;
        EncodedImage = encodedImage;
        MimeType = mimeType;
        IsFromCache = isFromCache;
        ErrorMessage = errorMessage;
    }

    public MediaThumbnailState State { get; }

    public byte[]? EncodedImage { get; }

    public string? MimeType { get; }

    public bool IsFromCache { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccessful =>
        State == MediaThumbnailState.Success
        && EncodedImage is { Length: > 0 };
}
