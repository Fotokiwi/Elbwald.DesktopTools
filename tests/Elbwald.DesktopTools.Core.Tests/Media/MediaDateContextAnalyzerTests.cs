using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaDateContextAnalyzerTests
{
    [Fact]
    public void Analyze_RepeatedTwoMinuteOffset_ProducesHint()
    {
        var directory =
            Path.Combine(
                Path.GetTempPath(),
                "Elbwald",
                "Camera");

        var entries =
            Enumerable.Range(
                    0,
                    8)
                .Select(index =>
                    CreateEntry(
                        directory,
                        $"IMG_{index:0000}.jpg",
                        "Sony",
                        "ILCE-6400",
                        new DateTime(
                            2024,
                            6,
                            8,
                            6,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromMinutes(2)
                        + TimeSpan.FromSeconds(2)))
                .ToArray();

        var hint =
            Assert.Single(
                new MediaDateContextAnalyzer().Analyze(
                    entries));

        Assert.Equal(
            MediaDateContextHintKind.RepeatedFileNameOffset,
            hint.Kind);

        Assert.Equal(
            TimeSpan.FromMinutes(2)
            + TimeSpan.FromSeconds(2),
            hint.DominantDifference);

        Assert.Equal(
            8,
            hint.MatchingFileCount);

        Assert.Equal(
            8,
            hint.ComparableFileCount);

        Assert.Equal(
            0,
            hint.OutlierCount);

        Assert.Equal(
            1d,
            hint.CoverageRatio);

        Assert.Equal(
            MediaDateContextStrength.Medium,
            hint.Strength);

        Assert.Contains(
            "+00:02:02",
            hint.Message);

        Assert.Contains(
            "korrigiert deshalb nichts automatisch",
            hint.Message);
    }

    [Fact]
    public void Analyze_SmallSecondJitter_RemainsOneDominantCluster()
    {
        var offsets =
            new[]
            {
                TimeSpan.FromSeconds(120),
                TimeSpan.FromSeconds(121),
                TimeSpan.FromSeconds(122),
                TimeSpan.FromSeconds(123),
                TimeSpan.FromSeconds(124),
                TimeSpan.FromSeconds(122)
            };

        var entries =
            offsets
                .Select(
                    (offset, index) =>
                        CreateEntry(
                            "/archive/camera",
                            $"IMG_{index:0000}.jpg",
                            "Sony",
                            "ILCE-6400",
                            new DateTime(
                                2024,
                                6,
                                8,
                                6,
                                0,
                                0).AddMinutes(index),
                            offset))
                .ToArray();

        var hint =
            Assert.Single(
                new MediaDateContextAnalyzer().Analyze(
                    entries));

        Assert.Equal(
            6,
            hint.MatchingFileCount);

        Assert.Equal(
            TimeSpan.FromSeconds(122),
            hint.DominantDifference);
    }

    [Fact]
    public void Analyze_FewerThanFiveComparableFiles_ProducesNoHint()
    {
        var entries =
            Enumerable.Range(
                    0,
                    4)
                .Select(index =>
                    CreateEntry(
                        "/archive/camera",
                        $"IMG_{index:0000}.jpg",
                        "Sony",
                        "ILCE-6400",
                        new DateTime(
                            2024,
                            6,
                            8,
                            6,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromMinutes(7)))
                .ToArray();

        var hints =
            new MediaDateContextAnalyzer().Analyze(
                entries);

        Assert.Empty(
            hints);
    }

    [Fact]
    public void Analyze_PracticallyIdenticalTimes_ProducesNoSystematicOffsetHint()
    {
        var entries =
            Enumerable.Range(
                    0,
                    12)
                .Select(index =>
                    CreateEntry(
                        "/archive/camera",
                        $"IMG_{index:0000}.jpg",
                        "Sony",
                        "ILCE-6400",
                        new DateTime(
                            2024,
                            6,
                            8,
                            6,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromSeconds(2)))
                .ToArray();

        var hints =
            new MediaDateContextAnalyzer().Analyze(
                entries);

        Assert.Empty(
            hints);
    }

    [Fact]
    public void Analyze_DifferentCameras_AreNotMergedIntoOneContext()
    {
        var firstCamera =
            Enumerable.Range(
                    0,
                    3)
                .Select(index =>
                    CreateEntry(
                        "/archive/camera",
                        $"SONY_{index:0000}.jpg",
                        "Sony",
                        "ILCE-6400",
                        new DateTime(
                            2024,
                            6,
                            8,
                            6,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromMinutes(10)));

        var secondCamera =
            Enumerable.Range(
                    0,
                    3)
                .Select(index =>
                    CreateEntry(
                        "/archive/camera",
                        $"PHONE_{index:0000}.jpg",
                        "Xiaomi",
                        "Redmi Note 8 Pro",
                        new DateTime(
                            2024,
                            6,
                            8,
                            7,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromMinutes(10)));

        var hints =
            new MediaDateContextAnalyzer().Analyze(
                firstCamera.Concat(
                    secondCamera));

        Assert.Empty(
            hints);
    }

    [Fact]
    public void Analyze_DominantOffsetWithOutlier_ReportsOutlierAndHighStrength()
    {
        var directory =
            "/archive/camera";

        var normalEntries =
            Enumerable.Range(
                    0,
                    10)
                .Select(index =>
                    CreateEntry(
                        directory,
                        $"IMG_{index:0000}.jpg",
                        "Sony",
                        "ILCE-6400",
                        new DateTime(
                            2024,
                            6,
                            8,
                            6,
                            0,
                            0).AddMinutes(index),
                        TimeSpan.FromMinutes(7)))
                .ToList();

        normalEntries.Add(
            CreateEntry(
                directory,
                "IMG_OUTLIER.jpg",
                "Sony",
                "ILCE-6400",
                new DateTime(
                    2024,
                    6,
                    8,
                    8,
                    0,
                    0),
                TimeSpan.FromMinutes(20)));

        var hint =
            Assert.Single(
                new MediaDateContextAnalyzer().Analyze(
                    normalEntries));

        Assert.Equal(
            MediaDateContextStrength.High,
            hint.Strength);

        Assert.Equal(
            10,
            hint.MatchingFileCount);

        Assert.Equal(
            11,
            hint.ComparableFileCount);

        Assert.Equal(
            1,
            hint.OutlierCount);

        Assert.Contains(
            "1 Datei(en)",
            hint.Message);
    }

    private static MediaDateContextEntry CreateEntry(
        string directory,
        string fileName,
        string cameraMake,
        string cameraModel,
        DateTime exifDate,
        TimeSpan fileNameOffset)
    {
        var fullPath =
            Path.Combine(
                directory,
                fileName);

        var mediaFile =
            new MediaFile(
                fullPath,
                fileName,
                ".jpg",
                1024,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                MediaFileType.Image,
                IsSymbolicLink: false);

        var metadata =
            new ImageMetadata(
                6000,
                4000,
                exifDate,
                cameraMake,
                cameraModel,
                null,
                1,
                HasExif: true,
                HasGps: false,
                Latitude: null,
                Longitude: null)
            {
                DateTimeOriginal =
                    exifDate
            };

        var analyzed =
            new MediaAnalyzedFile(
                mediaFile,
                metadata);

        var candidates =
            new[]
            {
                new MediaDateCandidate(
                    MediaDateSource.ExifDateTimeOriginal,
                    exifDate,
                    MediaDateTimeBasis.LocalTimeZoneUnknown,
                    MediaDatePrecision.Second,
                    "EXIF DateTimeOriginal",
                    IsCaptureEvidence: true),

                new MediaDateCandidate(
                    MediaDateSource.FileName,
                    exifDate + fileNameOffset,
                    MediaDateTimeBasis.LocalTimeZoneUnknown,
                    MediaDatePrecision.Second,
                    "Dateiname",
                    IsCaptureEvidence: true)
            };

        var resolution =
            new MediaDateResolution(
                exifDate,
                MediaDateConfidence.High,
                MediaDateSource.ExifDateTimeOriginal,
                candidates,
                Array.Empty<MediaDateComparison>(),
                "Testauflösung");

        return new MediaDateContextEntry(
            analyzed,
            resolution);
    }
}
