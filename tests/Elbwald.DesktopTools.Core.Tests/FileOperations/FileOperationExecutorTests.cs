using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.FileOperations;

public sealed class FileOperationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_ValidMove_MovesFileAndCreatesDirectory()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile(
            "incoming/photo.jpg",
            "photo-data");
        var destination = directory.GetPath(
            "sorted/2026/photo.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.IsSuccessful);
        Assert.Equal(FileOperationExecutionState.Completed, result.State);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.RemainingCount);

        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Equal("photo-data", File.ReadAllText(destination));
    }

    [Fact]
    public async Task ExecuteAsync_ValidCopy_CopiesFileWithoutRemovingSource()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile(
            "incoming/photo.jpg",
            "photo-data");
        var destination = directory.GetPath(
            "backup/photo.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Copy(source, destination)
        ]);

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.IsSuccessful);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(destination));
        Assert.Equal("photo-data", File.ReadAllText(destination));

        var partialFiles = Directory.Exists(Path.GetDirectoryName(destination))
            ? Directory.EnumerateFiles(
                Path.GetDirectoryName(destination)!,
                "*.partial",
                SearchOption.TopDirectoryOnly).ToArray()
            : Array.Empty<string>();

        Assert.Empty(partialFiles);
    }

    [Fact]
    public async Task ExecuteAsync_ConflictedPlan_IsRejectedBeforeFileChanges()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile(
            "source.jpg",
            "source");
        var destination = directory.CreateFile(
            "destination.jpg",
            "existing");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(plan));

        Assert.True(File.Exists(source));
        Assert.Equal("existing", File.ReadAllText(destination));
    }

    [Fact]
    public async Task ExecuteAsync_DestinationAppearsAfterPlanning_DoesNotOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile(
            "source.jpg",
            "source");
        var destination = directory.GetPath(
            "destination.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        File.WriteAllText(
            destination,
            "appeared-later");

        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(FileOperationExecutionState.Failed, result.State);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.RemainingCount);

        Assert.True(File.Exists(source));
        Assert.Equal(
            "appeared-later",
            File.ReadAllText(destination));
    }

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeStart_DoesNotTouchFiles()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.CreateFile(
            "source.jpg",
            "source");
        var destination = directory.GetPath(
            "destination.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(source, destination)
        ]);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await executor.ExecuteAsync(
            plan,
            cancellationToken: cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(FileOperationExecutionState.Cancelled, result.State);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(1, result.RemainingCount);

        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExecuteAsync_FirstRuntimeFailure_StopsFollowingOperations()
    {
        using var directory = new TemporaryDirectory();

        var firstSource = directory.CreateFile(
            "one.jpg",
            "one");
        var secondSource = directory.CreateFile(
            "two.jpg",
            "two");

        var firstDestination = directory.GetPath(
            "sorted/one.jpg");
        var secondDestination = directory.GetPath(
            "sorted/two.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);

        var plan = planner.CreatePlan([
            FileOperationRequest.Move(
                firstSource,
                firstDestination),
            FileOperationRequest.Move(
                secondSource,
                secondDestination)
        ]);

        Directory.CreateDirectory(
            Path.GetDirectoryName(firstDestination)!);
        File.WriteAllText(
            firstDestination,
            "blocking-file");

        var result = await executor.ExecuteAsync(plan);

        Assert.Equal(FileOperationExecutionState.Failed, result.State);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RemainingCount);

        Assert.True(File.Exists(firstSource));
        Assert.True(File.Exists(secondSource));
        Assert.False(File.Exists(secondDestination));
    }

    [Fact]
    public async Task ExecuteAsync_ReportsProgressAfterSuccessfulOperations()
    {
        using var directory = new TemporaryDirectory();

        var firstSource = directory.CreateFile("one.jpg");
        var secondSource = directory.CreateFile("two.jpg");

        var planner = new FileOperationPlanner();
        var executor = CreateExecutor(directory);
        var progressValues = new List<FileOperationProgress>();

        var plan = planner.CreatePlan([
            FileOperationRequest.Copy(
                firstSource,
                directory.GetPath("copies/one.jpg")),
            FileOperationRequest.Copy(
                secondSource,
                directory.GetPath("copies/two.jpg"))
        ]);

        var progress = new InlineProgress<FileOperationProgress>(
            progressValues.Add);

        var result = await executor.ExecuteAsync(
            plan,
            progress);

        Assert.True(result.IsSuccessful);
        Assert.Equal(2, progressValues.Count);

        Assert.Equal(1, progressValues[0].CompletedCount);
        Assert.Equal(50, progressValues[0].Percentage);

        Assert.Equal(2, progressValues[1].CompletedCount);
        Assert.Equal(100, progressValues[1].Percentage);
    }

    [Fact]
    public async Task ExecuteAsync_SafetyCheckFails_DoesNotTouchSourceOrDestination()
    {
        using var directory = new TemporaryDirectory();

        var source = directory.CreateFile(
            "source.jpg",
            "source");

        var destination = directory.GetPath(
            "destination.jpg");

        var planner = new FileOperationPlanner();

        var plan = planner.CreatePlan([
            FileOperationRequest.Copy(source, destination)
        ]);

        var executor = CreateExecutor(
            directory,
            new AlwaysUnsafeSafetyChecker());

        var result = await executor.ExecuteAsync(plan);

        Assert.True(result.WasBlockedBySafetyCheck);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(destination));
        Assert.Empty(result.Items);
    }

    private static FileOperationExecutor CreateExecutor(
        TemporaryDirectory directory,
        IFileOperationSafetyChecker? safetyChecker = null)
    {
        var recoveryOptions = new RecoveryOptions
        {
            PersistentDirectory = directory.GetPath("recovery"),
            MaxMemoryBytes = 0,
            MaxSingleMemoryItemBytes = 0,
            MinimumPersistentFreeSpaceReserveBytes = 0,
            PersistentFreeSpaceReserveRatio = 0,
            MaximumPersistentRatioReserveBytes = 0
        };

        var recoveryStore =
            new FileRecoveryStore(recoveryOptions);

        var journal =
            new JsonLinesOperationJournal(
                directory.GetPath(
                    "journal/operations.jsonl"));

        return new FileOperationExecutor(
            safetyChecker ?? new AlwaysSafeSafetyChecker(),
            journal,
            recoveryStore,
            FixedStartupRecoveryService.Clean(),
            FixedFileOperationProcessLock.Held());
    }

    private sealed class AlwaysSafeSafetyChecker
        : IFileOperationSafetyChecker
    {
        public FileOperationPreflightResult Check(
            FileOperationPlan plan)
        {
            return FileOperationPreflightResult.Safe;
        }

        public FileOperationPreflightResult CheckOperation(
            FileOperationPlanItem operation)
        {
            return FileOperationPreflightResult.Safe;
        }
    }

    private sealed class AlwaysUnsafeSafetyChecker
        : IFileOperationSafetyChecker
    {
        private static readonly FileOperationPreflightResult Unsafe =
            new([
                new FileOperationSafetyIssue(
                    FileOperationSafetyIssueKind.InsufficientFreeSpace,
                    "Simulierter Sicherheitsabbruch.")
            ]);

        public FileOperationPreflightResult Check(
            FileOperationPlan plan)
        {
            return Unsafe;
        }

        public FileOperationPreflightResult CheckOperation(
            FileOperationPlanItem operation)
        {
            return Unsafe;
        }
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value)
        {
            _report(value);
        }
    }
}
