using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class TiffEmbeddedJpegPreviewExtractorTests
{
    [Fact]
    public async Task ExtractAsync_TiffRawWithJpegPreview_ReturnsEmbeddedJpeg()
    {
        using var directory = new TestDirectory();

        var jpeg =
            new byte[]
            {
                0xFF, 0xD8,
                0xFF, 0xE0,
                0x00, 0x04,
                0x12, 0x34,
                0xFF, 0xD9
            };

        var path =
            directory.CreateFile(
                "photo.arw",
                CreateTiffRawWithEmbeddedJpeg(jpeg));

        var result =
            await new TiffEmbeddedJpegPreviewExtractor()
                .ExtractAsync(
                    CreateMediaFile(path));

        Assert.True(result.IsSuccessful);
        Assert.Equal(
            RawPreviewState.Success,
            result.State);
        Assert.Equal(
            "image/jpeg",
            result.MimeType);
        Assert.Equal(
            jpeg,
            result.EncodedImage);
    }

    [Fact]
    public async Task ExtractAsync_NonTiffRaw_FindsEmbeddedJpegByMarkerScan()
    {
        using var directory = new TestDirectory();

        var jpeg =
            new byte[]
            {
                0xFF, 0xD8,
                0xFF, 0xDB,
                0x00, 0x04,
                0x56, 0x78,
                0xFF, 0xD9
            };

        var bytes =
            Enumerable
                .Repeat(
                    (byte)0x11,
                    32)
                .Concat(jpeg)
                .Concat(
                    Enumerable.Repeat(
                        (byte)0x22,
                        16))
                .ToArray();

        var path =
            directory.CreateFile(
                "photo.raf",
                bytes);

        var result =
            await new TiffEmbeddedJpegPreviewExtractor()
                .ExtractAsync(
                    CreateMediaFile(
                        path,
                        ".raf"));

        Assert.True(result.IsSuccessful);
        Assert.Equal(
            jpeg,
            result.EncodedImage);
    }

    [Fact]
    public async Task ExtractAsync_NonRaw_ReturnsNotRaw()
    {
        using var directory = new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.jpg",
                new byte[]
                {
                    0xFF, 0xD8, 0xFF, 0xD9
                });

        var result =
            await new TiffEmbeddedJpegPreviewExtractor()
                .ExtractAsync(
                    CreateMediaFile(
                        path,
                        ".jpg"));

        Assert.Equal(
            RawPreviewState.NotRaw,
            result.State);
        Assert.Null(
            result.EncodedImage);
    }

    [Fact]
    public async Task ExtractAsync_RawWithoutPreview_ReturnsPreviewNotFound()
    {
        using var directory = new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.arw",
                CreateTiffWithoutPreview());

        var result =
            await new TiffEmbeddedJpegPreviewExtractor()
                .ExtractAsync(
                    CreateMediaFile(path));

        Assert.Equal(
            RawPreviewState.PreviewNotFound,
            result.State);
        Assert.Null(
            result.EncodedImage);
    }

    [Fact]
    public async Task ExtractAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
    {
        using var directory = new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.arw",
                CreateTiffWithoutPreview());

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new TiffEmbeddedJpegPreviewExtractor()
                .ExtractAsync(
                    CreateMediaFile(path),
                    cancellation.Token));
    }

    private static MediaFile CreateMediaFile(
        string path,
        string extension = ".arw")
    {
        var info =
            new FileInfo(path);

        return new MediaFile(
            info.FullName,
            info.Name,
            extension,
            info.Length,
            new DateTimeOffset(
                info.CreationTimeUtc),
            new DateTimeOffset(
                info.LastWriteTimeUtc),
            MediaFileType.Image,
            IsSymbolicLink: false);
    }

    private static byte[] CreateTiffRawWithEmbeddedJpeg(
        byte[] jpeg)
    {
        const uint firstIfdOffset = 8;
        const uint previewIfdOffset = 14;
        const uint jpegOffset = 64;

        var totalLength =
            checked(
                (int)jpegOffset
                + jpeg.Length);

        var bytes =
            new byte[totalLength];

        using var stream =
            new MemoryStream(bytes);

        using var writer =
            new BinaryWriter(
                stream,
                System.Text.Encoding.ASCII,
                leaveOpen: true);

        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write(firstIfdOffset);

        stream.Position = firstIfdOffset;

        writer.Write((ushort)0);
        writer.Write(previewIfdOffset);

        stream.Position = previewIfdOffset;

        writer.Write((ushort)2);

        WriteIfdEntry(
            writer,
            tag: 0x0201,
            type: 4,
            count: 1,
            valueOrOffset: jpegOffset);

        WriteIfdEntry(
            writer,
            tag: 0x0202,
            type: 4,
            count: 1,
            valueOrOffset:
                checked((uint)jpeg.Length));

        writer.Write((uint)0);

        stream.Position = jpegOffset;
        writer.Write(jpeg);

        return bytes;
    }

    private static byte[] CreateTiffWithoutPreview()
    {
        var bytes =
            new byte[16];

        using var stream =
            new MemoryStream(bytes);

        using var writer =
            new BinaryWriter(
                stream,
                System.Text.Encoding.ASCII,
                leaveOpen: true);

        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write((uint)8);

        stream.Position = 8;
        writer.Write((ushort)0);
        writer.Write((uint)0);

        return bytes;
    }

    private static void WriteIfdEntry(
        BinaryWriter writer,
        ushort tag,
        ushort type,
        uint count,
        uint valueOrOffset)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(valueOrOffset);
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
            byte[] bytes)
        {
            var path =
                Path.Combine(
                    RootPath,
                    relativePath);

            File.WriteAllBytes(
                path,
                bytes);

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
