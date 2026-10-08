using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class FileSystemMediaScanner
    : IMediaScanner
{
    private readonly IMediaTypeDetector _mediaTypeDetector;

    public FileSystemMediaScanner(
        IMediaTypeDetector mediaTypeDetector)
    {
        ArgumentNullException.ThrowIfNull(
            mediaTypeDetector);

        _mediaTypeDetector = mediaTypeDetector;
    }

    public Task<MediaScanResult> ScanAsync(
        string path,
        MediaScanOptions? options = null,
        IProgress<MediaScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path);

        options ??= MediaScanOptions.Default;

        return Task.Run(
            () => ScanCore(
                path,
                options,
                progress,
                cancellationToken),
            cancellationToken);
    }

    private MediaScanResult ScanCore(
        string path,
        MediaScanOptions options,
        IProgress<MediaScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or IOException)
        {
            return new MediaScanResult(
                path,
                Array.Empty<MediaFile>(),
                new[]
                {
                    CreateError(
                        path,
                        "Pfad normalisieren",
                        exception,
                        MediaScanErrorKind.InvalidPath)
                },
                directoriesVisited: 0,
                filesDiscovered: 0,
                skippedSymbolicLinkDirectories: 0);
        }

        FileAttributes rootAttributes;

        try
        {
            rootAttributes =
                File.GetAttributes(fullPath);
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException
                or IOException
                or ArgumentException
                or NotSupportedException)
        {
            return new MediaScanResult(
                fullPath,
                Array.Empty<MediaFile>(),
                new[]
                {
                    CreateFileSystemError(
                        fullPath,
                        "Startpfad prüfen",
                        exception)
                },
                directoriesVisited: 0,
                filesDiscovered: 0,
                skippedSymbolicLinkDirectories: 0);
        }

        var files = new List<MediaFile>();
        var errors = new List<MediaScanError>();

        var directoriesVisited = 0;
        var filesDiscovered = 0;
        var skippedSymbolicLinkDirectories = 0;

        if ((rootAttributes & FileAttributes.Directory) == 0)
        {
            ScanFile(
                fullPath,
                rootAttributes,
                options,
                files,
                errors,
                ref filesDiscovered,
                directoriesVisited,
                skippedSymbolicLinkDirectories,
                progress,
                cancellationToken);

            return CreateResult(
                fullPath,
                files,
                errors,
                directoriesVisited,
                filesDiscovered,
                skippedSymbolicLinkDirectories);
        }

        var pendingDirectories =
            new Stack<string>();

        pendingDirectories.Push(
            fullPath);

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory =
                pendingDirectories.Pop();

            directoriesVisited++;

            ReportProgress(
                progress,
                directoriesVisited,
                filesDiscovered,
                files.Count,
                errors.Count,
                skippedSymbolicLinkDirectories);

            IEnumerator<string>? enumerator = null;

            try
            {
                enumerator =
                    Directory
                        .EnumerateFileSystemEntries(directory)
                        .GetEnumerator();

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string entry;

                    try
                    {
                        if (!enumerator.MoveNext())
                        {
                            break;
                        }

                        entry = enumerator.Current;
                    }
                    catch (Exception exception) when (
                        exception is UnauthorizedAccessException
                            or IOException
                            or ArgumentException
                            or NotSupportedException)
                    {
                        errors.Add(
                            CreateFileSystemError(
                                directory,
                                "Verzeichnis lesen",
                                exception));

                        ReportProgress(
                            progress,
                            directoriesVisited,
                            filesDiscovered,
                            files.Count,
                            errors.Count,
                            skippedSymbolicLinkDirectories);

                        break;
                    }

                    FileAttributes attributes;

                    try
                    {
                        attributes =
                            File.GetAttributes(entry);
                    }
                    catch (Exception exception) when (
                        exception is FileNotFoundException
                            or DirectoryNotFoundException
                            or UnauthorizedAccessException
                            or IOException
                            or ArgumentException
                            or NotSupportedException)
                    {
                        errors.Add(
                            CreateFileSystemError(
                                entry,
                                "Dateisystemeintrag prüfen",
                                exception));

                        ReportProgress(
                            progress,
                            directoriesVisited,
                            filesDiscovered,
                            files.Count,
                            errors.Count,
                            skippedSymbolicLinkDirectories);

                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!options.Recursive)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skippedSymbolicLinkDirectories++;

                            ReportProgress(
                                progress,
                                directoriesVisited,
                                filesDiscovered,
                                files.Count,
                                errors.Count,
                                skippedSymbolicLinkDirectories);

                            continue;
                        }

                        pendingDirectories.Push(
                            entry);

                        continue;
                    }

                    ScanFile(
                        entry,
                        attributes,
                        options,
                        files,
                        errors,
                        ref filesDiscovered,
                        directoriesVisited,
                        skippedSymbolicLinkDirectories,
                        progress,
                        cancellationToken);
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException
                    or IOException
                    or ArgumentException
                    or NotSupportedException)
            {
                errors.Add(
                    CreateFileSystemError(
                        directory,
                        "Verzeichnis öffnen",
                        exception));

                ReportProgress(
                    progress,
                    directoriesVisited,
                    filesDiscovered,
                    files.Count,
                    errors.Count,
                    skippedSymbolicLinkDirectories);
            }
            finally
            {
                enumerator?.Dispose();
            }
        }

        return CreateResult(
            fullPath,
            files,
            errors,
            directoriesVisited,
            filesDiscovered,
            skippedSymbolicLinkDirectories);
    }

    private void ScanFile(
        string path,
        FileAttributes attributes,
        MediaScanOptions options,
        ICollection<MediaFile> files,
        ICollection<MediaScanError> errors,
        ref int filesDiscovered,
        int directoriesVisited,
        int skippedSymbolicLinkDirectories,
        IProgress<MediaScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        filesDiscovered++;

        try
        {
            var fileInfo =
                new FileInfo(path);

            var mediaType =
                _mediaTypeDetector.Detect(path);

            if (mediaType != MediaFileType.Unknown
                || options.IncludeUnknownFiles)
            {
                files.Add(
                    new MediaFile(
                        FullPath:
                            fileInfo.FullName,
                        FileName:
                            fileInfo.Name,
                        Extension:
                            fileInfo.Extension.ToLowerInvariant(),
                        Length:
                            fileInfo.Length,
                        CreationTimeUtc:
                            new DateTimeOffset(
                                fileInfo.CreationTimeUtc),
                        LastWriteTimeUtc:
                            new DateTimeOffset(
                                fileInfo.LastWriteTimeUtc),
                        MediaType:
                            mediaType,
                        IsSymbolicLink:
                            (attributes & FileAttributes.ReparsePoint) != 0));
            }
        }
        catch (Exception exception) when (
            exception is FileNotFoundException
                or DirectoryNotFoundException
                or UnauthorizedAccessException
                or IOException
                or ArgumentException
                or NotSupportedException)
        {
            errors.Add(
                CreateError(
                    path,
                    "Dateimetadaten lesen",
                    exception,
                    exception is UnauthorizedAccessException
                        ? MediaScanErrorKind.AccessDenied
                        : MediaScanErrorKind.MetadataUnavailable));
        }

        ReportProgress(
            progress,
            directoriesVisited,
            filesDiscovered,
            files.Count,
            errors.Count,
            skippedSymbolicLinkDirectories);
    }

    private static MediaScanResult CreateResult(
        string rootPath,
        IEnumerable<MediaFile> files,
        IEnumerable<MediaScanError> errors,
        int directoriesVisited,
        int filesDiscovered,
        int skippedSymbolicLinkDirectories)
    {
        return new MediaScanResult(
            rootPath,
            files,
            errors,
            directoriesVisited,
            filesDiscovered,
            skippedSymbolicLinkDirectories);
    }

    private static MediaScanError CreateFileSystemError(
        string path,
        string operation,
        Exception exception)
    {
        var kind =
            exception switch
            {
                FileNotFoundException =>
                    MediaScanErrorKind.PathNotFound,

                DirectoryNotFoundException =>
                    MediaScanErrorKind.PathNotFound,

                UnauthorizedAccessException =>
                    MediaScanErrorKind.AccessDenied,

                ArgumentException =>
                    MediaScanErrorKind.InvalidPath,

                NotSupportedException =>
                    MediaScanErrorKind.InvalidPath,

                _ =>
                    MediaScanErrorKind.IoError
            };

        return CreateError(
            path,
            operation,
            exception,
            kind);
    }

    private static MediaScanError CreateError(
        string path,
        string operation,
        Exception exception,
        MediaScanErrorKind kind)
    {
        return new MediaScanError(
            path,
            operation,
            kind,
            exception.Message);
    }

    private static void ReportProgress(
        IProgress<MediaScanProgress>? progress,
        int directoriesVisited,
        int filesDiscovered,
        int filesIncluded,
        int errors,
        int skippedSymbolicLinkDirectories)
    {
        progress?.Report(
            new MediaScanProgress(
                directoriesVisited,
                filesDiscovered,
                filesIncluded,
                errors,
                skippedSymbolicLinkDirectories));
    }
}
