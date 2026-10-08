namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaFile(
    string FullPath,
    string FileName,
    string Extension,
    long Length,
    DateTimeOffset CreationTimeUtc,
    DateTimeOffset LastWriteTimeUtc,
    MediaFileType MediaType,
    bool IsSymbolicLink)
{
    public string DirectoryPath =>
        Path.GetDirectoryName(FullPath)
        ?? string.Empty;

    public bool IsImage =>
        MediaType == MediaFileType.Image;
}
