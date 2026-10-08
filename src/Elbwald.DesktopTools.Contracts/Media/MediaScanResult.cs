namespace Elbwald.DesktopTools.Contracts.Media;

public sealed class MediaScanResult
{
    public MediaScanResult(
        string rootPath,
        IEnumerable<MediaFile> files,
        IEnumerable<MediaScanError> errors,
        int directoriesVisited,
        int filesDiscovered,
        int skippedSymbolicLinkDirectories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(errors);

        if (directoriesVisited < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(directoriesVisited));
        }

        if (filesDiscovered < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filesDiscovered));
        }

        if (skippedSymbolicLinkDirectories < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skippedSymbolicLinkDirectories));
        }

        RootPath = rootPath;
        Files = files.ToArray();
        Errors = errors.ToArray();
        DirectoriesVisited = directoriesVisited;
        FilesDiscovered = filesDiscovered;
        SkippedSymbolicLinkDirectories =
            skippedSymbolicLinkDirectories;
    }

    public string RootPath { get; }

    public IReadOnlyList<MediaFile> Files { get; }

    public IReadOnlyList<MediaScanError> Errors { get; }

    public int DirectoriesVisited { get; }

    public int FilesDiscovered { get; }

    public int SkippedSymbolicLinkDirectories { get; }

    public int ImageCount =>
        Files.Count(file =>
            file.MediaType == MediaFileType.Image);

    public bool HasErrors =>
        Errors.Count > 0;
}
