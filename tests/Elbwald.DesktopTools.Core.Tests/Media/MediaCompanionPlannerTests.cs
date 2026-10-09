using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Companions;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaCompanionPlannerTests
{
    [Fact]
    public void CreatePlans_RawAndJpegSameStem_AreGrouped()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateDirectory(
                "source");

        var target =
            directory.CreateDirectory(
                "target");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                Path.Combine(
                    "2024",
                    "06"),
                Path.Combine(
                    target,
                    "2024",
                    "06",
                    "DSC0001.ARW"));

        var jpeg =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.JPG",
                    "jpeg"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                Path.Combine(
                    "2024",
                    "06"),
                Path.Combine(
                    target,
                    "2024",
                    "06",
                    "DSC0001.JPG"));

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        jpeg
                    }));

        Assert.True(
            group.IsRawJpegPair);

        Assert.False(
            group.HasSidecar);

        Assert.Equal(
            MediaCompanionGroupState.Consistent,
            group.State);

        Assert.Equal(
            2,
            group.ImageCount);

        Assert.Empty(
            group.ProjectedSidecars);
    }

    [Fact]
    public void CreatePlans_XmpSidecar_IsProjectedButNotExecuted()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var relative =
            Path.Combine(
                "2024",
                "06");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                relative,
                Path.Combine(
                    target,
                    relative,
                    "DSC0001.ARW"));

        var sidecar =
            CreateSidecarEntry(
                directory.CreateFile(
                    "source/DSC0001.xmp",
                    "xmp"));

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        sidecar
                    }));

        Assert.True(
            group.HasSidecar);

        var projection =
            Assert.Single(
                group.ProjectedSidecars);

        Assert.Equal(
            Path.Combine(
                target,
                relative,
                "DSC0001.xmp"),
            projection.ProjectedDestinationPath);

        Assert.False(
            projection.DestinationAlreadyExists);

        Assert.Contains(
            "noch nicht",
            group.Message);
    }

    [Fact]
    public void CreatePlans_ExtensionQualifiedXmp_JoinsRawStem()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                "2024",
                Path.Combine(
                    target,
                    "2024",
                    "DSC0001.ARW"));

        var sidecar =
            CreateSidecarEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW.xmp",
                    "xmp"));

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        sidecar
                    }));

        Assert.Equal(
            "DSC0001",
            group.CompanionStem);

        var xmp =
            Assert.Single(
                group.Members.Where(member =>
                    member.Kind
                    == MediaCompanionKind.XmpSidecar));

        Assert.Equal(
            "DSC0001.ARW",
            xmp.AssociatedImageFileName);
    }

    [Fact]
    public void CreatePlans_TwoRenderedImagesWithoutRawOrSidecar_AreNotAssumedCompanions()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var first =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.JPG",
                    "jpeg"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                "2024",
                Path.Combine(
                    target,
                    "2024",
                    "DSC0001.JPG"));

        var second =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.JPEG",
                    "jpeg-2"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                "2024",
                Path.Combine(
                    target,
                    "2024",
                    "DSC0001.JPEG"));

        var groups =
            new MediaCompanionPlanner().CreatePlans(
                target,
                new[]
                {
                    first,
                    second
                });

        Assert.Empty(
            groups);
    }

    [Fact]
    public void CreatePlans_DifferentDirectories_AreNeverMerged()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var first =
            CreateImageEntry(
                directory.CreateFile(
                    "source/a/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                "2024",
                Path.Combine(
                    target,
                    "2024",
                    "DSC0001.ARW"));

        var second =
            CreateImageEntry(
                directory.CreateFile(
                    "source/b/DSC0001.JPG",
                    "jpeg"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                "2024",
                Path.Combine(
                    target,
                    "2024",
                    "DSC0001.JPG"));

        var groups =
            new MediaCompanionPlanner().CreatePlans(
                target,
                new[]
                {
                    first,
                    second
                });

        Assert.Empty(
            groups);
    }

    [Fact]
    public void CreatePlans_PairedImagesWouldSplitDestinations_IsConflict()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    30,
                    23,
                    59,
                    0),
                Path.Combine(
                    "2024",
                    "06"),
                Path.Combine(
                    target,
                    "2024",
                    "06",
                    "DSC0001.ARW"));

        var jpeg =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.JPG",
                    "jpeg"),
                new DateTime(
                    2024,
                    7,
                    1,
                    0,
                    1,
                    0),
                Path.Combine(
                    "2024",
                    "07"),
                Path.Combine(
                    target,
                    "2024",
                    "07",
                    "DSC0001.JPG"));

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        jpeg
                    }));

        Assert.Equal(
            MediaCompanionGroupState.Conflict,
            group.State);

        Assert.Contains(
            "unterschiedliche Zielordner",
            group.Message);
    }

    [Fact]
    public void CreatePlans_OneImageExcludedFromPlan_IsReview()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                Path.Combine(
                    "2024",
                    "06"),
                Path.Combine(
                    target,
                    "2024",
                    "06",
                    "DSC0001.ARW"));

        var jpeg =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.JPG",
                    "jpeg"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                plannedRelativeDirectory: null,
                plannedDestinationPath: null);

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        jpeg
                    }));

        Assert.Equal(
            MediaCompanionGroupState.Review,
            group.State);

        Assert.Contains(
            "nicht im Plan",
            group.Message);
    }

    [Fact]
    public void CreatePlans_ExistingProjectedSidecarDestination_IsConflict()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var relative =
            Path.Combine(
                "2024",
                "06");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0),
                relative,
                Path.Combine(
                    target,
                    relative,
                    "DSC0001.ARW"));

        var sidecar =
            CreateSidecarEntry(
                directory.CreateFile(
                    "source/DSC0001.xmp",
                    "xmp"));

        directory.CreateFile(
            Path.Combine(
                "target",
                relative,
                "DSC0001.xmp"),
            "existing");

        var group =
            Assert.Single(
                new MediaCompanionPlanner().CreatePlans(
                    target,
                    new[]
                    {
                        raw,
                        sidecar
                    }));

        Assert.Equal(
            MediaCompanionGroupState.Conflict,
            group.State);

        Assert.True(
            Assert.Single(
                group.ProjectedSidecars)
                .DestinationAlreadyExists);

        Assert.Contains(
            "Überschreiben bleibt verboten",
            group.Message);
    }

    [Fact]
    public void CreatePlans_DoesNotCreateProjectedTargetDirectories()
    {
        using var directory =
            new TestDirectory();

        var target =
            directory.CreateDirectory(
                "target");

        var relative =
            Path.Combine(
                "2030",
                "12");

        var raw =
            CreateImageEntry(
                directory.CreateFile(
                    "source/DSC0001.ARW",
                    "raw"),
                new DateTime(
                    2030,
                    12,
                    1,
                    10,
                    0,
                    0),
                relative,
                Path.Combine(
                    target,
                    relative,
                    "DSC0001.ARW"));

        var sidecar =
            CreateSidecarEntry(
                directory.CreateFile(
                    "source/DSC0001.xmp",
                    "xmp"));

        var projectedDirectory =
            Path.Combine(
                target,
                relative);

        Assert.False(
            Directory.Exists(
                projectedDirectory));

        _ =
            new MediaCompanionPlanner().CreatePlans(
                target,
                new[]
                {
                    raw,
                    sidecar
                });

        Assert.False(
            Directory.Exists(
                projectedDirectory));
    }

    private static MediaCompanionContextEntry CreateImageEntry(
        string path,
        DateTime capturedAt,
        string? plannedRelativeDirectory,
        string? plannedDestinationPath)
    {
        var analyzed =
            CreateAnalyzedFile(
                path,
                MediaFileType.Image,
                new ImageMetadata(
                    6000,
                    4000,
                    capturedAt,
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
                        capturedAt
                });

        var resolution =
            new MediaDateResolution(
                capturedAt,
                MediaDateConfidence.High,
                MediaDateSource.ExifDateTimeOriginal,
                new[]
                {
                    new MediaDateCandidate(
                        MediaDateSource.ExifDateTimeOriginal,
                        capturedAt,
                        MediaDateTimeBasis.LocalTimeZoneUnknown,
                        MediaDatePrecision.Second,
                        "EXIF DateTimeOriginal",
                        IsCaptureEvidence: true)
                },
                Array.Empty<MediaDateComparison>(),
                "Testauflösung");

        return new MediaCompanionContextEntry(
            analyzed,
            resolution,
            plannedRelativeDirectory,
            plannedDestinationPath);
    }

    private static MediaCompanionContextEntry CreateSidecarEntry(
        string path)
    {
        return new MediaCompanionContextEntry(
            CreateAnalyzedFile(
                path,
                MediaFileType.Unknown,
                metadata: null),
            DateResolution: null,
            PlannedRelativeDirectory: null,
            PlannedDestinationPath: null);
    }

    private static MediaAnalyzedFile CreateAnalyzedFile(
        string path,
        MediaFileType mediaType,
        ImageMetadata? metadata)
    {
        var info =
            new FileInfo(
                path);

        return new MediaAnalyzedFile(
            new MediaFile(
                info.FullName,
                info.Name,
                info.Extension.ToLowerInvariant(),
                info.Length,
                new DateTimeOffset(
                    info.CreationTimeUtc),
                new DateTimeOffset(
                    info.LastWriteTimeUtc),
                mediaType,
                IsSymbolicLink: false),
            metadata);
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

        public string CreateDirectory(
            string relativePath)
        {
            var path =
                Path.Combine(
                    RootPath,
                    relativePath);

            Directory.CreateDirectory(
                path);

            return Path.GetFullPath(
                path);
        }

        public string CreateFile(
            string relativePath,
            string content)
        {
            var path =
                Path.IsPathRooted(
                    relativePath)
                    ? relativePath
                    : Path.Combine(
                        RootPath,
                        relativePath);

            var parent =
                Path.GetDirectoryName(
                    path);

            if (!string.IsNullOrWhiteSpace(
                    parent))
            {
                Directory.CreateDirectory(
                    parent);
            }

            File.WriteAllText(
                path,
                content);

            return Path.GetFullPath(
                path);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    RootPath))
            {
                Directory.Delete(
                    RootPath,
                    recursive: true);
            }
        }
    }
}
