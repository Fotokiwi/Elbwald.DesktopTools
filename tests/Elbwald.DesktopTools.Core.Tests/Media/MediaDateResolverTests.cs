using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaDateResolverTests
{
    private static readonly DateTime BaseDate =
        new(
            2024,
            5,
            17,
            14,
            32,
            15,
            DateTimeKind.Unspecified);

    [Fact]
    public void Resolve_OriginalAndDigitizedAgree_RemainsHighBecauseClockIsShared()
    {
        var file =
            CreateAnalyzedFile(
                "DSC01234.ARW",
                new ImageMetadata(
                    6000,
                    4000,
                    BaseDate,
                    "Sony",
                    "ILCE-6400",
                    null,
                    1,
                    HasExif: true,
                    HasGps: false,
                    Latitude: null,
                    Longitude: null)
                {
                    DateTimeOriginal = BaseDate,
                    DateTimeDigitized = BaseDate
                });

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.True(result.IsResolved);
        Assert.Equal(
            BaseDate,
            result.Value);

        Assert.Equal(
            MediaDateSource.ExifDateTimeOriginal,
            result.PrimarySource);

        Assert.Equal(
            MediaDateConfidence.High,
            result.Confidence);

        var comparison =
            Assert.Single(
                result.Comparisons);

        Assert.False(
            comparison.IsIndependentEvidence);

        Assert.Equal(
            MediaDateDeviationLevel.Equivalent,
            comparison.Deviation);

        Assert.Empty(
            result.Conflicts);
    }

    [Fact]
    public void Resolve_OriginalAndFilenameAgree_IsVeryHighConfidence()
    {
        var file =
            CreateAnalyzedFile(
                "IMG_20240517_143215.jpg",
                MetadataWithOriginal(
                    BaseDate));

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.Equal(
            MediaDateConfidence.VeryHigh,
            result.Confidence);

        Assert.Contains(
            result.Candidates,
            candidate =>
                candidate.Source == MediaDateSource.FileName);

        Assert.Empty(
            result.Conflicts);
    }

    [Fact]
    public void Resolve_FilenameDiffersByTwoMinutesTwoSeconds_IsMinorAndStaysHigh()
    {
        var metadata =
            MetadataWithOriginal(
                new DateTime(
                    2024,
                    6,
                    8,
                    6,
                    14,
                    53)) with
            {
                DateTimeDigitized =
                    new DateTime(
                        2024,
                        6,
                        8,
                        6,
                        14,
                        53)
            };

        var result =
            new MediaDateResolver().Resolve(
                CreateAnalyzedFile(
                    "IMG_20240608_061655.jpg",
                    metadata));

        Assert.Equal(
            MediaDateConfidence.High,
            result.Confidence);

        Assert.True(
            result.NeedsReview);

        Assert.False(
            result.HasConflicts);

        var fileNameComparison =
            Assert.Single(
                result.Comparisons.Where(comparison =>
                    comparison.ComparedSource
                    == MediaDateSource.FileName));

        Assert.True(
            fileNameComparison.IsIndependentEvidence);

        Assert.Equal(
            MediaDateDeviationLevel.Minor,
            fileNameComparison.Deviation);

        Assert.Equal(
            TimeSpan.FromMinutes(2)
            + TimeSpan.FromSeconds(2),
            fileNameComparison.Difference);

        Assert.Contains(
            "+00:02:02",
            fileNameComparison.Message);
    }

    [Fact]
    public void Resolve_OriginalAndFilenameDisagree_DowngradesToMedium()
    {
        var file =
            CreateAnalyzedFile(
                "IMG_20250517_143215.jpg",
                MetadataWithOriginal(
                    BaseDate));

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.True(result.IsResolved);

        Assert.Equal(
            BaseDate,
            result.Value);

        Assert.Equal(
            MediaDateSource.ExifDateTimeOriginal,
            result.PrimarySource);

        Assert.Equal(
            MediaDateConfidence.Medium,
            result.Confidence);

        Assert.True(
            result.HasConflicts);

        Assert.Contains(
            result.Conflicts,
            conflict =>
                conflict.ConflictingSource
                == MediaDateSource.FileName);
    }

    [Fact]
    public void Resolve_FileNameTimestampOnly_IsMediumConfidence()
    {
        var file =
            CreateAnalyzedFile(
                "Screenshot_2024-05-17-14-32-15.png",
                metadata: null);

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.True(result.IsResolved);

        Assert.Equal(
            BaseDate,
            result.Value);

        Assert.Equal(
            MediaDateSource.FileName,
            result.PrimarySource);

        Assert.Equal(
            MediaDateConfidence.Medium,
            result.Confidence);
    }

    [Fact]
    public void Resolve_FileNameDateOnly_IsMediumButKeepsDateOnlyPrecision()
    {
        var file =
            CreateAnalyzedFile(
                "IMG-20240517-WA0001.jpg",
                metadata: null);

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.True(result.IsResolved);

        Assert.Equal(
            new DateTime(
                2024,
                5,
                17),
            result.Value);

        var fileNameCandidate =
            Assert.Single(
                result.Candidates.Where(candidate =>
                    candidate.Source
                    == MediaDateSource.FileName));

        Assert.Equal(
            MediaDatePrecision.DateOnly,
            fileNameCandidate.Precision);
    }

    [Fact]
    public void Resolve_FileSystemTimesOnly_RemainsUnresolved()
    {
        var file =
            CreateAnalyzedFile(
                "DSC01234.jpg",
                metadata: null);

        var result =
            new MediaDateResolver().Resolve(
                file);

        Assert.False(
            result.IsResolved);

        Assert.Null(
            result.Value);

        Assert.Null(
            result.PrimarySource);

        Assert.Equal(
            MediaDateConfidence.Low,
            result.Confidence);

        Assert.Contains(
            "Dateisystemzeiten",
            result.Explanation);
    }

    [Fact]
    public void Resolve_GpsUtcCompatibleWithExif_IncreasesConfidence()
    {
        var gpsUtc =
            new DateTime(
                2024,
                5,
                17,
                12,
                32,
                15,
                DateTimeKind.Utc);

        var metadata =
            MetadataWithOriginal(
                BaseDate) with
            {
                GpsDateTimeUtc = gpsUtc
            };

        var result =
            new MediaDateResolver().Resolve(
                CreateAnalyzedFile(
                    "DSC01234.jpg",
                    metadata));

        Assert.Equal(
            MediaDateConfidence.VeryHigh,
            result.Confidence);

        Assert.Contains(
            "+02:00",
            result.Explanation);

        var gpsComparison =
            Assert.Single(
                result.Comparisons.Where(comparison =>
                    comparison.ComparedSource
                    == MediaDateSource.GpsUtc));

        Assert.True(
            gpsComparison.IsIndependentEvidence);

        Assert.Equal(
            MediaDateDeviationLevel.Equivalent,
            gpsComparison.Deviation);
    }

    [Fact]
    public void Resolve_ExifModifiedOnly_IsLowConfidence()
    {
        var metadata =
            new ImageMetadata(
                6000,
                4000,
                BaseDate,
                "Sony",
                "ILCE-6400",
                null,
                1,
                HasExif: true,
                HasGps: false,
                Latitude: null,
                Longitude: null)
            {
                DateTimeModified =
                    BaseDate
            };

        var result =
            new MediaDateResolver().Resolve(
                CreateAnalyzedFile(
                    "DSC01234.jpg",
                    metadata));

        Assert.True(
            result.IsResolved);

        Assert.Equal(
            MediaDateConfidence.Low,
            result.Confidence);

        Assert.Equal(
            MediaDateSource.ExifDateTimeModified,
            result.PrimarySource);
    }

    private static ImageMetadata MetadataWithOriginal(
        DateTime value)
    {
        return new ImageMetadata(
            6000,
            4000,
            value,
            "Sony",
            "ILCE-6400",
            null,
            1,
            HasExif: true,
            HasGps: false,
            Latitude: null,
            Longitude: null)
        {
            DateTimeOriginal =
                value
        };
    }

    private static MediaAnalyzedFile CreateAnalyzedFile(
        string fileName,
        ImageMetadata? metadata)
    {
        var creation =
            new DateTimeOffset(
                2026,
                10,
                8,
                15,
                0,
                0,
                TimeSpan.Zero);

        var lastWrite =
            new DateTimeOffset(
                2026,
                10,
                8,
                16,
                0,
                0,
                TimeSpan.Zero);

        var mediaFile =
            new MediaFile(
                Path.Combine(
                    Path.GetTempPath(),
                    fileName),
                fileName,
                Path.GetExtension(fileName),
                1234,
                creation,
                lastWrite,
                MediaFileType.Image,
                IsSymbolicLink: false);

        return new MediaAnalyzedFile(
            mediaFile,
            metadata);
    }
}
