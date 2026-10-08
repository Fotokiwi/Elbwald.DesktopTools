using System.Text;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MetadataExtractorImageMetadataReaderTests
{
    [Fact]
    public async Task ReadAsync_ExifJpeg_ReadsCoreMetadataAndGps()
    {
        using var directory =
            new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.jpg",
                CreateExifJpeg());

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Image);

        var reader =
            new MetadataExtractorImageMetadataReader();

        var result =
            await reader.ReadAsync(file);

        Assert.True(
            result.IsSuccessful);

        var metadata =
            Assert.IsType<ImageMetadata>(
                result.Metadata);

        Assert.Equal(
            4000,
            metadata.PixelWidth);

        Assert.Equal(
            3000,
            metadata.PixelHeight);

        Assert.Equal(
            new DateTime(
                2026,
                10,
                7,
                21,
                15,
                30,
                DateTimeKind.Unspecified),
            metadata.CapturedAt);

        Assert.Equal(
            "Elbwald",
            metadata.CameraMake);

        Assert.Equal(
            "TestCam",
            metadata.CameraModel);

        Assert.Equal(
            "Test Lens",
            metadata.LensModel);

        Assert.Equal(
            6,
            metadata.Orientation);

        Assert.True(
            metadata.HasExif);

        Assert.True(
            metadata.HasGps);

        Assert.NotNull(
            metadata.Latitude);

        Assert.NotNull(
            metadata.Longitude);

        Assert.InRange(
            metadata.Latitude!.Value,
            51.0499,
            51.0501);

        Assert.InRange(
            metadata.Longitude!.Value,
            13.7332,
            13.7335);
    }

    [Fact]
    public async Task ReadAsync_SonyRawSizeTag_ReadsDimensions()
    {
        using var directory =
            new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.arw",
                CreateSonyRawSizeExifJpeg());

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Image);

        var result =
            await new MetadataExtractorImageMetadataReader()
                .ReadAsync(file);

        Assert.True(
            result.IsSuccessful);

        var metadata =
            Assert.IsType<ImageMetadata>(
                result.Metadata);

        Assert.Equal(
            6000,
            metadata.PixelWidth);

        Assert.Equal(
            4000,
            metadata.PixelHeight);

        Assert.Equal(
            ImageDisplayOrientation.Landscape,
            metadata.DisplayOrientation);
    }

    [Fact]
    public async Task ReadAsync_NonImage_ReturnsNotImageWithoutParsing()
    {
        using var directory =
            new TestDirectory();

        var path =
            directory.CreateFile(
                "notes.txt",
                Encoding.UTF8.GetBytes(
                    "not an image"));

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Unknown);

        var result =
            await new MetadataExtractorImageMetadataReader()
                .ReadAsync(file);

        Assert.Equal(
            ImageMetadataReadState.NotImage,
            result.State);

        Assert.Null(
            result.Metadata);
    }

    [Fact]
    public async Task ReadAsync_MissingImage_ReturnsFileNotFound()
    {
        using var directory =
            new TestDirectory();

        var path =
            Path.Combine(
                directory.RootPath,
                "missing.jpg");

        var file =
            new MediaFile(
                Path.GetFullPath(path),
                "missing.jpg",
                ".jpg",
                0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                MediaFileType.Image,
                IsSymbolicLink: false);

        var result =
            await new MetadataExtractorImageMetadataReader()
                .ReadAsync(file);

        Assert.Equal(
            ImageMetadataReadState.FileNotFound,
            result.State);

        Assert.Null(
            result.Metadata);
    }

    [Fact]
    public async Task ReadAsync_InvalidImage_ReturnsUnsupportedFormat()
    {
        using var directory =
            new TestDirectory();

        var path =
            directory.CreateFile(
                "broken.jpg",
                Encoding.UTF8.GetBytes(
                    "this is not a jpeg"));

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Image);

        var result =
            await new MetadataExtractorImageMetadataReader()
                .ReadAsync(file);

        Assert.Equal(
            ImageMetadataReadState.UnsupportedFormat,
            result.State);

        Assert.Null(
            result.Metadata);
    }

    [Fact]
    public async Task ReadAsync_DoesNotModifyImage()
    {
        using var directory =
            new TestDirectory();

        var original =
            CreateExifJpeg();

        var path =
            directory.CreateFile(
                "photo.jpg",
                original);

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Image);

        await new MetadataExtractorImageMetadataReader()
            .ReadAsync(file);

        var after =
            await File.ReadAllBytesAsync(path);

        Assert.Equal(
            original,
            after);
    }

    [Fact]
    public async Task ReadAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
    {
        using var directory =
            new TestDirectory();

        var path =
            directory.CreateFile(
                "photo.jpg",
                CreateExifJpeg());

        var file =
            CreateMediaFile(
                path,
                MediaFileType.Image);

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new MetadataExtractorImageMetadataReader()
                .ReadAsync(
                    file,
                    cancellation.Token));
    }

    private static MediaFile CreateMediaFile(
        string path,
        MediaFileType mediaType)
    {
        var info =
            new FileInfo(path);

        return new MediaFile(
            info.FullName,
            info.Name,
            info.Extension.ToLowerInvariant(),
            info.Length,
            new DateTimeOffset(
                info.CreationTimeUtc),
            new DateTimeOffset(
                info.LastWriteTimeUtc),
            mediaType,
            IsSymbolicLink: false);
    }

    private static byte[] CreateSonyRawSizeExifJpeg()
    {
        const int exifIfdOffset = 26;
        const int rawSizeOffset = 44;

        var tiff =
            new byte[52];

        using (var stream = new MemoryStream(tiff))
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.ASCII,
                   leaveOpen: true))
        {
            writer.Write((byte)'I');
            writer.Write((byte)'I');
            writer.Write((ushort)42);
            writer.Write((uint)8);

            stream.Position = 8;

            writer.Write((ushort)1);

            WriteIfdEntry(
                writer,
                tag: 0x8769,
                type: 4,
                count: 1,
                valueOrOffset: exifIfdOffset);

            writer.Write((uint)0);

            stream.Position =
                exifIfdOffset;

            writer.Write((ushort)1);

            WriteIfdEntry(
                writer,
                tag: 0x7038,
                type: 4,
                count: 2,
                valueOrOffset: rawSizeOffset);

            writer.Write((uint)0);

            stream.Position =
                rawSizeOffset;

            writer.Write((uint)6000);
            writer.Write((uint)4000);
        }

        var exifPayload =
            new byte[6 + tiff.Length];

        Encoding.ASCII
            .GetBytes("Exif\0\0")
            .CopyTo(
                exifPayload,
                0);

        tiff.CopyTo(
            exifPayload,
            6);

        using var jpeg =
            new MemoryStream();

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xD8);

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xE1);

        WriteBigEndianUInt16(
            jpeg,
            checked(
                (ushort)(exifPayload.Length + 2)));

        jpeg.Write(
            exifPayload);

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xD9);

        return jpeg.ToArray();
    }

    private static byte[] CreateExifJpeg()
    {
        const int makeOffset = 74;
        const int modelOffset = 82;
        const int exifIfdOffset = 90;
        const int dateOffset = 144;
        const int lensOffset = 164;
        const int gpsIfdOffset = 174;
        const int latitudeOffset = 228;
        const int longitudeOffset = 252;
        const int tiffLength = 276;

        var tiff =
            new byte[tiffLength];

        using (var stream = new MemoryStream(tiff))
        using (var writer = new BinaryWriter(
                   stream,
                   Encoding.ASCII,
                   leaveOpen: true))
        {
            writer.Write(
                (byte)'I');

            writer.Write(
                (byte)'I');

            writer.Write(
                (ushort)42);

            writer.Write(
                (uint)8);

            stream.Position = 8;

            writer.Write(
                (ushort)5);

            WriteIfdEntry(
                writer,
                tag: 0x010F,
                type: 2,
                count: 8,
                valueOrOffset: makeOffset);

            WriteIfdEntry(
                writer,
                tag: 0x0110,
                type: 2,
                count: 8,
                valueOrOffset: modelOffset);

            WriteIfdShortEntry(
                writer,
                tag: 0x0112,
                value: 6);

            WriteIfdEntry(
                writer,
                tag: 0x8769,
                type: 4,
                count: 1,
                valueOrOffset: exifIfdOffset);

            WriteIfdEntry(
                writer,
                tag: 0x8825,
                type: 4,
                count: 1,
                valueOrOffset: gpsIfdOffset);

            writer.Write(
                (uint)0);

            WriteAscii(
                stream,
                makeOffset,
                "Elbwald\0");

            WriteAscii(
                stream,
                modelOffset,
                "TestCam\0");

            stream.Position =
                exifIfdOffset;

            writer.Write(
                (ushort)4);

            WriteIfdEntry(
                writer,
                tag: 0x9003,
                type: 2,
                count: 20,
                valueOrOffset: dateOffset);

            WriteIfdEntry(
                writer,
                tag: 0xA002,
                type: 4,
                count: 1,
                valueOrOffset: 4000);

            WriteIfdEntry(
                writer,
                tag: 0xA003,
                type: 4,
                count: 1,
                valueOrOffset: 3000);

            WriteIfdEntry(
                writer,
                tag: 0xA434,
                type: 2,
                count: 10,
                valueOrOffset: lensOffset);

            writer.Write(
                (uint)0);

            WriteAscii(
                stream,
                dateOffset,
                "2026:10:07 21:15:30\0");

            WriteAscii(
                stream,
                lensOffset,
                "Test Lens\0");

            stream.Position =
                gpsIfdOffset;

            writer.Write(
                (ushort)4);

            WriteInlineAsciiEntry(
                writer,
                tag: 0x0001,
                value: "N");

            WriteIfdEntry(
                writer,
                tag: 0x0002,
                type: 5,
                count: 3,
                valueOrOffset: latitudeOffset);

            WriteInlineAsciiEntry(
                writer,
                tag: 0x0003,
                value: "E");

            WriteIfdEntry(
                writer,
                tag: 0x0004,
                type: 5,
                count: 3,
                valueOrOffset: longitudeOffset);

            writer.Write(
                (uint)0);

            stream.Position =
                latitudeOffset;

            WriteRational(
                writer,
                51,
                1);

            WriteRational(
                writer,
                3,
                1);

            WriteRational(
                writer,
                0,
                1);

            stream.Position =
                longitudeOffset;

            WriteRational(
                writer,
                13,
                1);

            WriteRational(
                writer,
                44,
                1);

            WriteRational(
                writer,
                0,
                1);
        }

        var exifPayload =
            new byte[6 + tiff.Length];

        Encoding.ASCII
            .GetBytes("Exif\0\0")
            .CopyTo(
                exifPayload,
                0);

        tiff.CopyTo(
            exifPayload,
            6);

        using var jpeg =
            new MemoryStream();

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xD8);

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xE1);

        WriteBigEndianUInt16(
            jpeg,
            checked(
                (ushort)(exifPayload.Length + 2)));

        jpeg.Write(
            exifPayload);

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xC0);

        WriteBigEndianUInt16(
            jpeg,
            17);

        jpeg.WriteByte(8);

        WriteBigEndianUInt16(
            jpeg,
            3000);

        WriteBigEndianUInt16(
            jpeg,
            4000);

        jpeg.WriteByte(3);

        jpeg.Write(
            new byte[]
            {
                1, 0x11, 0,
                2, 0x11, 0,
                3, 0x11, 0
            });

        jpeg.WriteByte(0xFF);
        jpeg.WriteByte(0xD9);

        return jpeg.ToArray();
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

    private static void WriteIfdShortEntry(
        BinaryWriter writer,
        ushort tag,
        ushort value)
    {
        writer.Write(tag);
        writer.Write((ushort)3);
        writer.Write((uint)1);
        writer.Write(value);
        writer.Write((ushort)0);
    }

    private static void WriteInlineAsciiEntry(
        BinaryWriter writer,
        ushort tag,
        string value)
    {
        writer.Write(tag);
        writer.Write((ushort)2);
        writer.Write((uint)2);
        writer.Write((byte)value[0]);
        writer.Write((byte)0);
        writer.Write((ushort)0);
    }

    private static void WriteRational(
        BinaryWriter writer,
        uint numerator,
        uint denominator)
    {
        writer.Write(numerator);
        writer.Write(denominator);
    }

    private static void WriteAscii(
        MemoryStream stream,
        long offset,
        string value)
    {
        stream.Position =
            offset;

        stream.Write(
            Encoding.ASCII.GetBytes(value));
    }

    private static void WriteBigEndianUInt16(
        Stream stream,
        ushort value)
    {
        stream.WriteByte(
            (byte)(value >> 8));

        stream.WriteByte(
            (byte)value);
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

            var parent =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(
                    parent);
            }

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
