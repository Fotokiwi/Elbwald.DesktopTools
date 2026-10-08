using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class FileSystemMediaScannerTests
{
    [Fact]
    public async Task ScanAsync_SingleFile_ReturnsMediaFile()
    {
        using var directory = new TestDirectory();

        var filePath =
            directory.CreateFile(
                "photo.JPG",
                "image-data");

        var result =
            await CreateScanner().ScanAsync(filePath);

        var file =
            Assert.Single(result.Files);

        Assert.Equal(
            Path.GetFullPath(filePath),
            file.FullPath);

        Assert.Equal(
            "photo.JPG",
            file.FileName);

        Assert.Equal(
            ".jpg",
            file.Extension);

        Assert.Equal(
            MediaFileType.Image,
            file.MediaType);

        Assert.Equal(
            new FileInfo(filePath).Length,
            file.Length);

        Assert.Equal(
            1,
            result.FilesDiscovered);

        Assert.Equal(
            1,
            result.ImageCount);

        Assert.False(
            result.HasErrors);
    }

    [Fact]
    public async Task ScanAsync_DirectoryWithoutRecursion_DoesNotEnterSubdirectories()
    {
        using var directory = new TestDirectory();

        directory.CreateFile(
            "root.jpg",
            "root");

        directory.CreateFile(
            "nested/nested.jpg",
            "nested");

        var result =
            await CreateScanner().ScanAsync(
                directory.RootPath,
                new MediaScanOptions
                {
                    Recursive = false
                });

        var file =
            Assert.Single(result.Files);

        Assert.Equal(
            "root.jpg",
            file.FileName);

        Assert.Equal(
            1,
            result.DirectoriesVisited);
    }

    [Fact]
    public async Task ScanAsync_Recursive_IncludesNestedFiles()
    {
        using var directory = new TestDirectory();

        directory.CreateFile(
            "root.jpg",
            "root");

        directory.CreateFile(
            "nested/one.png",
            "one");

        directory.CreateFile(
            "nested/deeper/two.webp",
            "two");

        var result =
            await CreateScanner().ScanAsync(
                directory.RootPath);

        Assert.Equal(
            3,
            result.Files.Count);

        Assert.Equal(
            3,
            result.ImageCount);

        Assert.Equal(
            3,
            result.FilesDiscovered);

        Assert.Equal(
            3,
            result.DirectoriesVisited);

        Assert.False(
            result.HasErrors);
    }

    [Fact]
    public async Task ScanAsync_ExcludeUnknownFiles_CountsButDoesNotReturnThem()
    {
        using var directory = new TestDirectory();

        directory.CreateFile(
            "photo.jpg",
            "photo");

        directory.CreateFile(
            "notes.txt",
            "notes");

        var result =
            await CreateScanner().ScanAsync(
                directory.RootPath,
                new MediaScanOptions
                {
                    IncludeUnknownFiles = false
                });

        var file =
            Assert.Single(result.Files);

        Assert.Equal(
            "photo.jpg",
            file.FileName);

        Assert.Equal(
            2,
            result.FilesDiscovered);

        Assert.Equal(
            1,
            result.ImageCount);
    }

    [Fact]
    public async Task ScanAsync_MissingPath_ReturnsStructuredError()
    {
        using var directory = new TestDirectory();

        var missing =
            Path.Combine(
                directory.RootPath,
                "does-not-exist");

        var result =
            await CreateScanner().ScanAsync(
                missing);

        Assert.Empty(
            result.Files);

        var error =
            Assert.Single(result.Errors);

        Assert.Equal(
            MediaScanErrorKind.PathNotFound,
            error.Kind);

        Assert.Equal(
            Path.GetFullPath(missing),
            result.RootPath);
    }

    [Fact]
    public async Task ScanAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
    {
        using var directory = new TestDirectory();

        directory.CreateFile(
            "photo.jpg",
            "photo");

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateScanner().ScanAsync(
                directory.RootPath,
                cancellationToken:
                    cancellation.Token));
    }

    [Fact]
    public async Task ScanAsync_ReportsProgressWithFinalCounts()
    {
        using var directory = new TestDirectory();

        directory.CreateFile(
            "one.jpg",
            "one");

        directory.CreateFile(
            "two.txt",
            "two");

        var progress =
            new CapturingProgress();

        var result =
            await CreateScanner().ScanAsync(
                directory.RootPath,
                progress:
                    progress);

        Assert.NotEmpty(
            progress.Values);

        var final =
            progress.Values[^1];

        Assert.Equal(
            result.DirectoriesVisited,
            final.DirectoriesVisited);

        Assert.Equal(
            result.FilesDiscovered,
            final.FilesDiscovered);

        Assert.Equal(
            result.Files.Count,
            final.FilesIncluded);

        Assert.Equal(
            result.Errors.Count,
            final.Errors);
    }

    [Fact]
    public async Task ScanAsync_DoesNotModifyFileContents()
    {
        using var directory = new TestDirectory();

        var filePath =
            directory.CreateFile(
                "photo.jpg",
                "original-content");

        var before =
            await File.ReadAllBytesAsync(
                filePath);

        await CreateScanner().ScanAsync(
            directory.RootPath);

        var after =
            await File.ReadAllBytesAsync(
                filePath);

        Assert.Equal(
            before,
            after);
    }

    private static FileSystemMediaScanner CreateScanner()
    {
        return new FileSystemMediaScanner(
            new ExtensionMediaTypeDetector());
    }

    private sealed class CapturingProgress
        : IProgress<MediaScanProgress>
    {
        public List<MediaScanProgress> Values { get; } = new();

        public void Report(
            MediaScanProgress value)
        {
            Values.Add(value);
        }
    }

    private sealed class TestDirectory
        : IDisposable
    {
        public TestDirectory()
        {
            RootPath =
                Path.Combine(
                    Path.GetTempPath(),
                    "Elbwald.DesktopTools.Tests",
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(
                RootPath);
        }

        public string RootPath { get; }

        public string CreateFile(
            string relativePath,
            string content)
        {
            var path =
                Path.Combine(
                    RootPath,
                    relativePath);

            var parent =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(
                    parent);
            }

            File.WriteAllText(
                path,
                content);

            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(
                    RootPath,
                    recursive: true);
            }
        }
    }
}
