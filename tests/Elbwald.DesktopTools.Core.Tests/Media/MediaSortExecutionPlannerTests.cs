using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Companions;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Media;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaSortExecutionPlannerTests
{
    [Fact]
    public async Task CreateAsync_SingleCopy_CreatesExecutableStableFingerprint()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var sortPlan =
            CreateSingleImageSortPlan(
                sourcePath,
                Path.Combine(
                    destinationRoot,
                    "2024",
                    "06",
                    "photo.jpg"),
                FileOperationKind.Copy);

        var planner =
            CreateExecutionPlanner(
                directory,
                StartupRecoveryState.Clean);

        var first =
            await planner.CreateAsync(
                sortPlan);

        var second =
            await planner.CreateAsync(
                sortPlan);

        Assert.True(
            first.CanExecute);

        Assert.Empty(
            first.Issues);

        Assert.Equal(
            1,
            first.OperationCount);

        Assert.Equal(
            1,
            first.GroupCount);

        Assert.Equal(
            0,
            first.AtomicGroupCount);

        Assert.Equal(
            64,
            first.Fingerprint.Length);

        Assert.Equal(
            first.Fingerprint,
            second.Fingerprint);
    }

    [Fact]
    public async Task CreateAsync_FreshDryRunAfterSourceChange_ChangesFingerprint()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var destinationPath =
            Path.Combine(
                destinationRoot,
                "2024",
                "06",
                "photo.jpg");

        var planner =
            CreateExecutionPlanner(
                directory,
                StartupRecoveryState.Clean);

        var first =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    destinationPath,
                    FileOperationKind.Copy));

        File.AppendAllText(
            sourcePath,
            "-changed");

        var second =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    destinationPath,
                    FileOperationKind.Copy));

        Assert.NotEqual(
            first.Fingerprint,
            second.Fingerprint);
    }

    [Fact]
    public async Task CreateAsync_RawJpegXmp_BecomesOneAtomicThreeOperationGroup()
    {
        using var directory =
            new TestDirectory();

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var rawPath =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw");

        var jpegPath =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg");

        var xmpPath =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp");

        var sortPlan =
            CreateCompanionSortPlan(
                rawPath,
                jpegPath,
                xmpPath,
                destinationRoot,
                FileOperationKind.Copy,
                MediaCompanionGroupState.Consistent);

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    sortPlan);

        Assert.True(
            execution.CanExecute);

        Assert.Equal(
            3,
            execution.OperationCount);

        Assert.Equal(
            1,
            execution.GroupCount);

        Assert.Equal(
            1,
            execution.AtomicGroupCount);

        Assert.Equal(
            1,
            execution.SidecarOperationCount);

        var group =
            Assert.Single(
                execution.Groups);

        Assert.True(
            group.IsCompanionGroup);

        Assert.True(
            group.RequiresAtomicExecution);

        Assert.Equal(
            3,
            group.Operations.Count);

        var sidecar =
            Assert.Single(
                group.Operations.Where(operation =>
                    operation.IsSidecar));

        Assert.Equal(
            xmpPath,
            sidecar.SourcePath);

        Assert.Equal(
            Path.Combine(
                destinationRoot,
                "2024",
                "06",
                "DSC0001.xmp"),
            sidecar.DestinationPath);
    }

    [Fact]
    public async Task CreateAsync_CompanionReview_IsFailClosedUntilConfirmationExists()
    {
        using var directory =
            new TestDirectory();

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var rawPath =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw");

        var jpegPath =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg");

        var xmpPath =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp");

        var sortPlan =
            CreateCompanionSortPlan(
                rawPath,
                jpegPath,
                xmpPath,
                destinationRoot,
                FileOperationKind.Copy,
                MediaCompanionGroupState.Review);

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    sortPlan);

        Assert.False(
            execution.CanExecute);

        Assert.Contains(
            execution.PlanningIssues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.CompanionReviewRequiresConfirmation);
    }

    [Fact]
    public async Task CreateAsync_AnySortReview_IsFailClosedUntilConfirmationExists()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var basePlan =
            CreateSingleImageSortPlan(
                sourcePath,
                Path.Combine(
                    destinationRoot,
                    "2024",
                    "06",
                    "photo.jpg"),
                FileOperationKind.Copy);

        var sortPlan =
            new MediaSortPlan(
                basePlan.SourceRoot,
                basePlan.DestinationRoot,
                basePlan.Options,
                basePlan.Items,
                new[]
                {
                    new MediaSortIssue(
                        sourcePath,
                        MediaSortIssueKind.DateReview,
                        MediaSortIssueSeverity.Info,
                        "Geringe Zeitabweichung.")
                },
                basePlan.DateContextHints,
                basePlan.CompanionGroups,
                basePlan.OperationPlan,
                basePlan.IgnoredNonImageCount);

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    sortPlan);

        Assert.False(
            execution.CanExecute);

        Assert.Contains(
            execution.PlanningIssues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.ReviewRequiresConfirmation);

        Assert.True(
            execution.HasDateReviewApprovalRequirement);

        Assert.False(
            execution.HasBlockingPlanningIssues);

        Assert.True(
            execution.CanExecuteAfterDateReviewApproval);
    }

    [Fact]
    public async Task CreateAsync_DestinationInsideSource_IsHardBlockForLiveExecution()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var basePlan =
            CreateSingleImageSortPlan(
                sourcePath,
                Path.Combine(
                    destinationRoot,
                    "2024",
                    "06",
                    "photo.jpg"),
                FileOperationKind.Copy);

        var sortPlan =
            new MediaSortPlan(
                basePlan.SourceRoot,
                basePlan.DestinationRoot,
                basePlan.Options,
                basePlan.Items,
                new[]
                {
                    new MediaSortIssue(
                        basePlan.DestinationRoot,
                        MediaSortIssueKind.DestinationInsideSource,
                        MediaSortIssueSeverity.Warning,
                        "Ziel liegt in Quelle.")
                },
                basePlan.DateContextHints,
                basePlan.CompanionGroups,
                basePlan.OperationPlan,
                basePlan.IgnoredNonImageCount);

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    sortPlan);

        Assert.True(
            execution.HasBlockingPlanningIssues);

        Assert.False(
            execution.CanExecuteAfterDateReviewApproval);

        Assert.Contains(
            execution.PlanningIssues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.SortPlanNotExecutable);
    }

    [Fact]
    public async Task CreateAsync_MissingCameraInfo_DoesNotRequireLiveApproval()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var basePlan =
            CreateSingleImageSortPlan(
                sourcePath,
                Path.Combine(
                    destinationRoot,
                    "2024",
                    "06",
                    "photo.jpg"),
                FileOperationKind.Copy);

        var sortPlan =
            new MediaSortPlan(
                basePlan.SourceRoot,
                basePlan.DestinationRoot,
                basePlan.Options,
                basePlan.Items,
                new[]
                {
                    new MediaSortIssue(
                        sourcePath,
                        MediaSortIssueKind.MissingCameraInformation,
                        MediaSortIssueSeverity.Info,
                        "Kamera unbekannt.")
                },
                basePlan.DateContextHints,
                basePlan.CompanionGroups,
                basePlan.OperationPlan,
                basePlan.IgnoredNonImageCount);

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    sortPlan);

        Assert.False(
            execution.HasDateReviewApprovalRequirement);

        Assert.False(
            execution.HasBlockingPlanningIssues);

        Assert.True(
            execution.CanExecuteAfterDateReviewApproval);
    }

    [Fact]
    public async Task ValidateCurrentStateAsync_SourceLengthChanged_RequiresNewDryRun()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var planner =
            CreateExecutionPlanner(
                directory,
                StartupRecoveryState.Clean);

        var execution =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    Path.Combine(
                        destinationRoot,
                        "2024",
                        "06",
                        "photo.jpg"),
                    FileOperationKind.Copy));

        File.AppendAllText(
            sourcePath,
            "-changed");

        var validation =
            await planner.ValidateCurrentStateAsync(
                execution);

        Assert.False(
            validation.IsValid);

        Assert.Contains(
            validation.Issues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.SourceLengthChanged);
    }

    [Fact]
    public async Task ValidateCurrentStateAsync_LastWriteChangedWithSameLength_RequiresNewDryRun()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var planner =
            CreateExecutionPlanner(
                directory,
                StartupRecoveryState.Clean);

        var execution =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    Path.Combine(
                        destinationRoot,
                        "2024",
                        "06",
                        "photo.jpg"),
                    FileOperationKind.Copy));

        var originalLength =
            new FileInfo(
                sourcePath).Length;

        File.SetLastWriteTimeUtc(
            sourcePath,
            File.GetLastWriteTimeUtc(
                sourcePath).AddMinutes(10));

        Assert.Equal(
            originalLength,
            new FileInfo(
                sourcePath).Length);

        var validation =
            await planner.ValidateCurrentStateAsync(
                execution);

        Assert.False(
            validation.IsValid);

        Assert.Contains(
            validation.Issues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.SourceLastWriteTimeChanged);
    }

    [Fact]
    public async Task ValidateCurrentStateAsync_DestinationAppeared_BlocksOverwrite()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var destinationPath =
            Path.Combine(
                destinationRoot,
                "2024",
                "06",
                "photo.jpg");

        var planner =
            CreateExecutionPlanner(
                directory,
                StartupRecoveryState.Clean);

        var execution =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    destinationPath,
                    FileOperationKind.Copy));

        directory.CreateAbsoluteFile(
            destinationPath,
            "new-target");

        var validation =
            await planner.ValidateCurrentStateAsync(
                execution);

        Assert.False(
            validation.IsValid);

        Assert.Contains(
            validation.Issues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.DestinationAppeared);
    }

    [Fact]
    public async Task CreateAsync_MoveSafetyPreflightUsesCopySemanticsButExecutionPlanStaysMove()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var destinationPath =
            Path.Combine(
                destinationRoot,
                "2024",
                "06",
                "photo.jpg");

        var safetyChecker =
            new CapturingSafetyChecker();

        var planner =
            new MediaSortExecutionPlanner(
                new FileOperationPlanner(),
                safetyChecker,
                new FakeStartupRecoveryService(
                    StartupRecoveryState.Clean),
                RecoveryOptions.CreateDefault(
                    directory.Resolve(
                        "recovery")));

        var execution =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    destinationPath,
                    FileOperationKind.Move));

        Assert.True(
            execution.CanExecute);

        Assert.Equal(
            FileOperationKind.Move,
            Assert.Single(
                execution.FileOperationPlan.Operations)
                .Kind);

        Assert.NotNull(
            safetyChecker.LastPlan);

        Assert.Equal(
            FileOperationKind.Copy,
            Assert.Single(
                safetyChecker.LastPlan!.Operations)
                .Kind);
    }

    [Fact]
    public async Task CreateAsync_AtomicMoveGroup_ComputesPeakPersistentRecoveryBytesForWholeGroup()
    {
        using var directory =
            new TestDirectory();

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var rawPath =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "12345");

        var jpegPath =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "1234567");

        var xmpPath =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "123");

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.Clean)
                .CreateAsync(
                    CreateCompanionSortPlan(
                        rawPath,
                        jpegPath,
                        xmpPath,
                        destinationRoot,
                        FileOperationKind.Move,
                        MediaCompanionGroupState.Consistent));

        Assert.True(
            execution.CanExecute);

        Assert.Equal(
            new FileInfo(rawPath).Length
            + new FileInfo(jpegPath).Length
            + new FileInfo(xmpPath).Length,
            execution.PeakPersistentRecoveryBytes);
    }

    [Fact]
    public async Task CreateAsync_RecoveryNotClean_BlocksExecutionPlan()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var execution =
            await CreateExecutionPlanner(
                    directory,
                    StartupRecoveryState.AttentionRequired)
                .CreateAsync(
                    CreateSingleImageSortPlan(
                        sourcePath,
                        Path.Combine(
                            destinationRoot,
                            "2024",
                            "06",
                            "photo.jpg"),
                        FileOperationKind.Copy));

        Assert.False(
            execution.CanExecute);

        Assert.Contains(
            execution.ValidationIssues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.RecoveryNotReady);
    }

    [Fact]
    public async Task CreateAsync_RecoveryReserveImpossible_BlocksMovePlan()
    {
        using var directory =
            new TestDirectory();

        var sourcePath =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destinationRoot =
            directory.CreateDirectory(
                "target");

        var recoveryOptions =
            new RecoveryOptions
            {
                PersistentDirectory =
                    directory.Resolve(
                        "recovery"),
                MinimumPersistentFreeSpaceReserveBytes =
                    long.MaxValue,
                PersistentFreeSpaceReserveRatio =
                    0,
                MaximumPersistentRatioReserveBytes =
                    0
            };

        var planner =
            new MediaSortExecutionPlanner(
                new FileOperationPlanner(),
                new FileOperationSafetyChecker(),
                new FakeStartupRecoveryService(
                    StartupRecoveryState.Clean),
                recoveryOptions);

        var execution =
            await planner.CreateAsync(
                CreateSingleImageSortPlan(
                    sourcePath,
                    Path.Combine(
                        destinationRoot,
                        "2024",
                        "06",
                        "photo.jpg"),
                    FileOperationKind.Move));

        Assert.False(
            execution.CanExecute);

        Assert.Contains(
            execution.ValidationIssues,
            issue =>
                issue.Kind
                == MediaSortExecutionIssueKind.RecoveryStorageInsufficientSpace);
    }

    private static MediaSortExecutionPlanner CreateExecutionPlanner(
        TestDirectory directory,
        StartupRecoveryState recoveryState)
    {
        return new MediaSortExecutionPlanner(
            new FileOperationPlanner(),
            new FileOperationSafetyChecker(),
            new FakeStartupRecoveryService(
                recoveryState),
            RecoveryOptions.CreateDefault(
                directory.Resolve(
                    "recovery")));
    }

    private static MediaSortPlan CreateSingleImageSortPlan(
        string sourcePath,
        string destinationPath,
        FileOperationKind operationKind)
    {
        var analyzed =
            CreateAnalyzedImage(
                sourcePath,
                new DateTime(
                    2024,
                    6,
                    8,
                    10,
                    0,
                    0));

        var resolution =
            CreateResolution(
                analyzed.ImageMetadata!.DateTimeOriginal!.Value);

        var item =
            new MediaSortPlanItem(
                0,
                analyzed.File,
                resolution.Value!.Value,
                resolution,
                Path.Combine(
                    "2024",
                    "06"),
                destinationPath);

        var operationPlan =
            new FileOperationPlanner().CreatePlan(
                new[]
                {
                    new FileOperationRequest(
                        operationKind,
                        sourcePath,
                        destinationPath)
                });

        return new MediaSortPlan(
            analyzed.File.DirectoryPath,
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Path.GetDirectoryName(
                        destinationPath)!)!)!,
            new MediaSortOptions
            {
                OperationKind =
                    operationKind
            },
            new[]
            {
                item
            },
            Array.Empty<MediaSortIssue>(),
            Array.Empty<MediaDateContextHint>(),
            Array.Empty<MediaCompanionGroupPlan>(),
            operationPlan,
            ignoredNonImageCount: 0);
    }

    private static MediaSortPlan CreateCompanionSortPlan(
        string rawPath,
        string jpegPath,
        string xmpPath,
        string destinationRoot,
        FileOperationKind operationKind,
        MediaCompanionGroupState groupState)
    {
        var capturedAt =
            new DateTime(
                2024,
                6,
                8,
                10,
                0,
                0);

        var raw =
            CreateAnalyzedImage(
                rawPath,
                capturedAt);

        var jpeg =
            CreateAnalyzedImage(
                jpegPath,
                capturedAt);

        var xmp =
            CreateAnalyzedUnknown(
                xmpPath);

        var resolution =
            CreateResolution(
                capturedAt);

        var relative =
            Path.Combine(
                "2024",
                "06");

        var rawDestination =
            Path.Combine(
                destinationRoot,
                relative,
                Path.GetFileName(
                    rawPath));

        var jpegDestination =
            Path.Combine(
                destinationRoot,
                relative,
                Path.GetFileName(
                    jpegPath));

        var xmpDestination =
            Path.Combine(
                destinationRoot,
                relative,
                Path.GetFileName(
                    xmpPath));

        var items =
            new[]
            {
                new MediaSortPlanItem(
                    0,
                    raw.File,
                    capturedAt,
                    resolution,
                    relative,
                    rawDestination),

                new MediaSortPlanItem(
                    1,
                    jpeg.File,
                    capturedAt,
                    resolution,
                    relative,
                    jpegDestination)
            };

        var operationPlan =
            new FileOperationPlanner().CreatePlan(
                new[]
                {
                    new FileOperationRequest(
                        operationKind,
                        rawPath,
                        rawDestination),

                    new FileOperationRequest(
                        operationKind,
                        jpegPath,
                        jpegDestination)
                });

        var companionGroup =
            new MediaCompanionGroupPlan(
                raw.File.DirectoryPath,
                "DSC0001",
                new[]
                {
                    new MediaCompanionMember(
                        raw,
                        MediaCompanionKind.RawImage,
                        "DSC0001",
                        AssociatedImageFileName: null),

                    new MediaCompanionMember(
                        jpeg,
                        MediaCompanionKind.JpegImage,
                        "DSC0001",
                        AssociatedImageFileName: null),

                    new MediaCompanionMember(
                        xmp,
                        MediaCompanionKind.XmpSidecar,
                        "DSC0001",
                        AssociatedImageFileName: null)
                },
                new[]
                {
                    new MediaCompanionProjection(
                        xmpPath,
                        xmpDestination,
                        MediaCompanionKind.XmpSidecar,
                        DestinationAlreadyExists: false)
                },
                groupState,
                "Testgruppe");

        return new MediaSortPlan(
            raw.File.DirectoryPath,
            destinationRoot,
            new MediaSortOptions
            {
                OperationKind =
                    operationKind
            },
            items,
            Array.Empty<MediaSortIssue>(),
            Array.Empty<MediaDateContextHint>(),
            new[]
            {
                companionGroup
            },
            operationPlan,
            ignoredNonImageCount: 0);
    }

    private static MediaAnalyzedFile CreateAnalyzedImage(
        string path,
        DateTime capturedAt)
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
                MediaFileType.Image,
                IsSymbolicLink: false),
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
    }

    private static MediaAnalyzedFile CreateAnalyzedUnknown(
        string path)
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
                MediaFileType.Unknown,
                IsSymbolicLink: false),
            ImageMetadata: null);
    }

    private static MediaDateResolution CreateResolution(
        DateTime capturedAt)
    {
        return new MediaDateResolution(
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
    }

    private sealed class CapturingSafetyChecker
        : IFileOperationSafetyChecker
    {
        public FileOperationPlan? LastPlan { get; private set; }

        public FileOperationPreflightResult Check(
            FileOperationPlan plan)
        {
            LastPlan =
                plan;

            return FileOperationPreflightResult.Safe;
        }

        public FileOperationPreflightResult CheckOperation(
            FileOperationPlanItem operation)
        {
            return FileOperationPreflightResult.Safe;
        }
    }

    private sealed class FakeStartupRecoveryService
        : IStartupRecoveryService
    {
        private readonly StartupRecoverySnapshot _snapshot;

        public FakeStartupRecoveryService(
            StartupRecoveryState state)
        {
            _snapshot =
                new StartupRecoverySnapshot(
                    state,
                    DateTimeOffset.UtcNow,
                    Array.Empty<FileOperationRecoveryCandidate>(),
                    corruptJournalLineCount:
                        state == StartupRecoveryState.AttentionRequired
                            ? 1
                            : 0,
                    errorMessage:
                        state == StartupRecoveryState.Clean
                            ? null
                            : "Test-Recovery ist nicht clean.");
        }

        public StartupRecoverySnapshot Current =>
            _snapshot;

        public Task<StartupRecoverySnapshot> ScanAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _snapshot);
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

        public string Resolve(
            string relativePath)
        {
            return Path.GetFullPath(
                Path.Combine(
                    RootPath,
                    relativePath));
        }

        public string CreateDirectory(
            string relativePath)
        {
            var path =
                Resolve(
                    relativePath);

            Directory.CreateDirectory(
                path);

            return path;
        }

        public string CreateFile(
            string relativePath,
            string content)
        {
            var path =
                Resolve(
                    relativePath);

            return CreateAbsoluteFile(
                path,
                content);
        }

        public string CreateAbsoluteFile(
            string path,
            string content)
        {
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
