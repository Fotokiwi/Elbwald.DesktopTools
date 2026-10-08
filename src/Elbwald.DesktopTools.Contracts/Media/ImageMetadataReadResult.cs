namespace Elbwald.DesktopTools.Contracts.Media;

public sealed class ImageMetadataReadResult
{
    public ImageMetadataReadResult(
        ImageMetadataReadState state,
        ImageMetadata? metadata = null,
        IEnumerable<string>? warnings = null,
        string? errorMessage = null,
        bool isFromCache = false)
    {
        State = state;
        Metadata = metadata;
        Warnings = warnings?.ToArray()
            ?? Array.Empty<string>();
        ErrorMessage = errorMessage;
        IsFromCache = isFromCache;
    }

    public ImageMetadataReadState State { get; }

    public ImageMetadata? Metadata { get; }

    public IReadOnlyList<string> Warnings { get; }

    public string? ErrorMessage { get; }

    public bool IsFromCache { get; }

    public bool IsSuccessful =>
        State == ImageMetadataReadState.Success;

    public bool HasWarnings =>
        Warnings.Count > 0;
}
