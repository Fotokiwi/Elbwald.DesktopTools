namespace Elbwald.DesktopTools.Contracts.Media;

public sealed class RawPreviewResult
{
    public RawPreviewResult(
        RawPreviewState state,
        byte[]? encodedImage = null,
        string? mimeType = null,
        string? errorMessage = null)
    {
        State = state;
        EncodedImage = encodedImage;
        MimeType = mimeType;
        ErrorMessage = errorMessage;
    }

    public RawPreviewState State { get; }

    public byte[]? EncodedImage { get; }

    public string? MimeType { get; }

    public string? ErrorMessage { get; }

    public bool IsSuccessful =>
        State == RawPreviewState.Success
        && EncodedImage is { Length: > 0 };
}
