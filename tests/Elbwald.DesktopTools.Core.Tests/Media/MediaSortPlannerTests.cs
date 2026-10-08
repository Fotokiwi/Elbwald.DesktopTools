using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaSortPlannerTests
{
    [Fact]
    public void CreatePlan_YearMonth_UsesExifDateWithoutCreatingTargetDirectories()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/nested/photo.jpg",
                "photo");

        var file =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2024,
                    5,
                    17,
                    14,
                    30,
                    0),
                "Sony",
                "ILCE-6400");

        var planner =
            CreatePlanner();

        var plan =
            planner.CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    file
                });

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            Path.Combine(
                "2024",
                "05"),
            item.RelativeDirectory);

        Assert.Equal(
            Path.Combine(
                destinationRoot,
                "2024",
                "05",
                "photo.jpg"),
            item.DestinationPath);

        Assert.False(
            Directory.Exists(
                Path.Combine(
                    destinationRoot,
                    "2024")));

        Assert.True(
            plan.CanExecute);

        Assert.Empty(
            plan.OperationPlan.Conflicts);
    }

    [Fact]
    public void CreatePlan_YearCameraMonth_SanitizesCameraSegment()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/photo.arw",
                "raw");

        var file =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2025,
                    12,
                    1,
                    8,
                    0,
                    0),
                "SONY",
                "ILCE/6400:Test");

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    file
                },
                new MediaSortOptions
                {
                    Rule =
                        MediaSortRule.YearCameraMonth
                });

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            Path.Combine(
                "2025",
                "SONY ILCE_6400_Test",
                "12"),
            item.RelativeDirectory);

        Assert.DoesNotContain(
            "/",
            Path.GetFileName(
                Path.GetDirectoryName(
                    item.RelativeDirectory)!));
    }

    [Fact]
    public void CreatePlan_MissingCaptureDate_SkipsFileWithoutTimestampFallback()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/no-date.jpg",
                "photo");

        var file =
            CreateAnalyzedImage(
                sourcePath,
                capturedAt: null,
                cameraMake: "Sony",
                cameraModel: "ILCE-6400");

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    file
                });

        Assert.Empty(
            plan.Items);

        Assert.Equal(
            1,
            plan.MissingCaptureDateCount);

        var issue =
            Assert.Single(
                plan.Issues);

        Assert.Equal(
            MediaSortIssueKind.MissingCaptureDate,
            issue.Kind);

        Assert.Equal(
            MediaSortIssueSeverity.Warning,
            issue.Severity);

        Assert.Empty(
            plan.OperationPlan.Operations);
    }

    [Fact]
    public void CreatePlan_DuplicateDestination_IsBlockingConflict()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var firstPath =
            directory.CreateFile(
                "source/a/photo.jpg",
                "first");

        var secondPath =
            directory.CreateFile(
                "source/b/photo.jpg",
                "second");

        var capturedAt =
            new DateTime(
                2026,
                1,
                2,
                12,
                0,
                0);

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    CreateAnalyzedImage(
                        firstPath,
                        capturedAt,
                        "Sony",
                        "ILCE-6400"),
                    CreateAnalyzedImage(
                        secondPath,
                        capturedAt,
                        "Sony",
                        "ILCE-6400")
                });

        Assert.Equal(
            2,
            plan.PlannedFileCount);

        Assert.Contains(
            plan.OperationPlan.Conflicts,
            conflict =>
                conflict.Kind
                == FileOperationConflictKind.DuplicateDestination);

        Assert.True(
            plan.ProblemCount > 0);

        Assert.False(
            plan.CanExecute);
    }

    [Fact]
    public void CreatePlan_ExistingDestination_IsBlockingConflict()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "source");

        directory.CreateFile(
            "target/2026/03/photo.jpg",
            "existing");

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    CreateAnalyzedImage(
                        sourcePath,
                        new DateTime(
                            2026,
                            3,
                            10,
                            10,
                            0,
                            0),
                        "Sony",
                        "ILCE-6400")
                });

        Assert.Contains(
            plan.OperationPlan.Conflicts,
            conflict =>
                conflict.Kind
                == FileOperationConflictKind.DestinationAlreadyExists);

        Assert.False(
            plan.CanExecute);
    }

    [Fact]
    public void CreatePlan_ConflictingExifAndFilename_DefaultConfidenceSkipsFile()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/IMG_20250517_143215.jpg",
                "photo");

        var analyzed =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2024,
                    5,
                    17,
                    14,
                    32,
                    15),
                "Sony",
                "ILCE-6400");

        var metadata =
            analyzed.ImageMetadata! with
            {
                DateTimeOriginal =
                    new DateTime(
                        2024,
                        5,
                        17,
                        14,
                        32,
                        15)
            };

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    analyzed with
                    {
                        ImageMetadata =
                            metadata
                    }
                });

        Assert.Empty(
            plan.Items);

        var dateIssue =
            Assert.Single(
                plan.Issues.Where(issue =>
                    issue.Kind
                    == MediaSortIssueKind.DateReview));

        Assert.Equal(
            MediaSortIssueSeverity.Warning,
            dateIssue.Severity);

        Assert.Contains(
            "Automatische Sortierung ausgesetzt",
            dateIssue.Message);
    }

    [Fact]
    public void CreatePlan_ConflictingExifAndFilename_MediumConfidenceCanBePreviewed()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/IMG_20250517_143215.jpg",
                "photo");

        var analyzed =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2024,
                    5,
                    17,
                    14,
                    32,
                    15),
                "Sony",
                "ILCE-6400");

        var metadata =
            analyzed.ImageMetadata! with
            {
                DateTimeOriginal =
                    new DateTime(
                        2024,
                        5,
                        17,
                        14,
                        32,
                        15)
            };

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    analyzed with
                    {
                        ImageMetadata =
                            metadata
                    }
                },
                new MediaSortOptions
                {
                    MinimumDateConfidence =
                        Elbwald.DesktopTools.Contracts.Media.Dates.MediaDateConfidence.Medium
                });

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            2024,
            item.CapturedAt.Year);

        Assert.Equal(
            Elbwald.DesktopTools.Contracts.Media.Dates.MediaDateConfidence.Medium,
            item.DateResolution.Confidence);

        Assert.True(
            item.DateResolution.HasConflicts);

        var dateIssue =
            Assert.Single(
                plan.Issues.Where(issue =>
                    issue.Kind
                    == MediaSortIssueKind.DateReview));

        Assert.Equal(
            MediaSortIssueSeverity.Warning,
            dateIssue.Severity);
    }

    [Fact]
    public void CreatePlan_MinorTwoMinuteDeviation_DefaultHighKeepsItemWithSingleInfoReview()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/IMG_20240608_061655.jpg",
                "photo");

        var analyzed =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2024,
                    6,
                    8,
                    6,
                    14,
                    53),
                "Sony",
                "ILCE-6400");

        var metadata =
            analyzed.ImageMetadata! with
            {
                DateTimeOriginal =
                    new DateTime(
                        2024,
                        6,
                        8,
                        6,
                        14,
                        53),
                DateTimeDigitized =
                    new DateTime(
                        2024,
                        6,
                        8,
                        6,
                        14,
                        53)
            };

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    analyzed with
                    {
                        ImageMetadata =
                            metadata
                    }
                });

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            Elbwald.DesktopTools.Contracts.Media.Dates.MediaDateConfidence.High,
            item.DateResolution.Confidence);

        Assert.True(
            item.DateResolution.NeedsReview);

        Assert.False(
            item.DateResolution.HasConflicts);

        var dateIssue =
            Assert.Single(
                plan.Issues.Where(issue =>
                    issue.Kind
                    == MediaSortIssueKind.DateReview));

        Assert.Equal(
            MediaSortIssueSeverity.Info,
            dateIssue.Severity);

        Assert.Equal(
            1,
            plan.DateReviewCount);
    }

    [Fact]
    public void CreatePlan_NonImage_IsIgnored()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sourcePath =
            directory.CreateFile(
                "source/readme.txt",
                "text");

        var mediaFile =
            new MediaFile(
                Path.GetFullPath(sourcePath),
                "readme.txt",
                ".txt",
                new FileInfo(sourcePath).Length,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                MediaFileType.Unknown,
                IsSymbolicLink: false);

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    new MediaAnalyzedFile(
                        mediaFile,
                        ImageMetadata: null)
                });

        Assert.Equal(
            1,
            plan.IgnoredNonImageCount);

        Assert.Empty(
            plan.Items);
    }

    [Fact]
    public void CreatePlan_DestinationInsideSource_AddsWarningButDoesNotWrite()
    {
        using var directory =
            new TestDirectory();

        var sourceRoot =
            directory.CreateDirectory(
                "source");

        var destinationRoot =
            Path.Combine(
                sourceRoot,
                "sorted");

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var plan =
            CreatePlanner().CreatePlan(
                sourceRoot,
                destinationRoot,
                new[]
                {
                    CreateAnalyzedImage(
                        sourcePath,
                        new DateTime(
                            2026,
                            4,
                            2,
                            9,
                            0,
                            0),
                        "Sony",
                        "ILCE-6400")
                });

        Assert.Contains(
            plan.Issues,
            issue =>
                issue.Kind
                == MediaSortIssueKind.DestinationInsideSource);

        Assert.False(
            Directory.Exists(
                destinationRoot));
    }

    private static MediaSortPlanner CreatePlanner()
    {
        return new MediaSortPlanner(
            new FileOperationPlanner(),
            new MediaDateResolver());
    }

    private static MediaAnalyzedFile CreateAnalyzedImage(
        string path,
        DateTime? capturedAt,
        string? cameraMake,
        string? cameraModel)
    {
        var info =
            new FileInfo(path);

        var mediaFile =
            new MediaFile(
                Path.GetFullPath(path),
                Path.GetFileName(path),
                Path.GetExtension(path).ToLowerInvariant(),
                info.Length,
                new DateTimeOffset(
                    info.CreationTimeUtc),
                new DateTimeOffset(
                    info.LastWriteTimeUtc),
                MediaFileType.Image,
                IsSymbolicLink: false);

        var metadata =
            new ImageMetadata(
                PixelWidth: 6000,
                PixelHeight: 4000,
                CapturedAt: capturedAt,
                CameraMake: cameraMake,
                CameraModel: cameraModel,
                LensModel: null,
                Orientation: 1,
                HasExif: true,
                HasGps: false,
                Latitude: null,
                Longitude: null);

        return new MediaAnalyzedFile(
            mediaFile,
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

            return Path.GetFullPath(path);
        }

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
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(
                path,
                content);

            return Path.GetFullPath(path);
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
