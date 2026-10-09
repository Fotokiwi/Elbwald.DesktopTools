using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Storage;
using System.Security.Cryptography;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaSortExecutionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WrongConfirmation_DoesNotTouchDestination()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal();

        var executor =
            CreateExecutor(
                journal);

        var result =
            await executor.ExecuteAsync(
                plan,
                new MediaSortExecutionConfirmation(
                    plan.Fingerprint,
                    "JA",
                    ApproveDateReviews: false));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByConfirmation,
            result.State);

        Assert.True(
            File.Exists(
                source));

        Assert.False(
            File.Exists(
                destination));

        Assert.Empty(
            journal.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_StaleFingerprint_DoesNotTouchDestination()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        var result =
            await CreateExecutor(
                    new FakeJournal())
                .ExecuteAsync(
                    plan,
                    new MediaSortExecutionConfirmation(
                        new string(
                            'B',
                            64),
                        MediaSortExecutionConfirmation.RequiredText,
                        ApproveDateReviews: false));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByConfirmation,
            result.State);

        Assert.True(
            File.Exists(
                source));

        Assert.False(
            File.Exists(
                destination));
    }

    [Fact]
    public async Task ExecuteAsync_Copy_CommitsVerifiedTargetAndKeepsSource()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/2024/06/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal();

        var executor =
            CreateExecutor(
                journal);

        var result =
            await executor.ExecuteAsync(
                plan,
                Confirm(
                    plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Completed,
            result.State);

        Assert.Equal(
            1,
            result.CompletedOperationCount);

        Assert.True(
            File.Exists(
                source));

        Assert.True(
            File.Exists(
                destination));

        Assert.Equal(
            File.ReadAllBytes(
                source),
            File.ReadAllBytes(
                destination));

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.Prepared);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.Executing);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.Committed);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_MoveWithoutExplicitDeletionApproval_IsBlockedWithoutMutation()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal();

        var recovery =
            new FakePersistentRecoveryStore();

        var result =
            await CreateExecutor(
                    journal,
                    recoveryStore:
                        recovery)
                .ExecuteAsync(
                    plan,
                    new MediaSortExecutionConfirmation(
                        plan.Fingerprint,
                        MediaSortExecutionConfirmation.MoveRequiredText,
                        ApproveDateReviews: false,
                        ApproveSourceDeletion: false));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByConfirmation,
            result.State);

        Assert.True(
            File.Exists(
                source));

        Assert.False(
            File.Exists(
                destination));

        Assert.Empty(
            journal.Entries);

        Assert.Equal(
            0,
            recovery.PreserveCount);
    }

    [Fact]
    public async Task ExecuteAsync_MoveWithCopyConfirmationText_IsBlockedWithoutMutation()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        source,
                        destination)
                });

        var result =
            await CreateExecutor(
                    new FakeJournal())
                .ExecuteAsync(
                    plan,
                    new MediaSortExecutionConfirmation(
                        plan.Fingerprint,
                        MediaSortExecutionConfirmation.CopyRequiredText,
                        ApproveDateReviews: false,
                        ApproveSourceDeletion: true));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByConfirmation,
            result.State);

        Assert.True(
            File.Exists(
                source));

        Assert.False(
            File.Exists(
                destination));
    }

    [Fact]
    public async Task ExecuteAsync_Move_PreservesRecoveryVerifiesTargetThenDeletesSource()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var originalBytes =
            File.ReadAllBytes(
                source);

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal();

        var recovery =
            new FakePersistentRecoveryStore();

        var result =
            await CreateExecutor(
                    journal,
                    recoveryStore:
                        recovery)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Completed,
            result.State);

        Assert.False(
            File.Exists(
                source));

        Assert.True(
            File.Exists(
                destination));

        Assert.Equal(
            originalBytes,
            File.ReadAllBytes(
                destination));

        Assert.Equal(
            1,
            recovery.PreserveCount);

        Assert.True(
            recovery.VerifyCount >= 2);

        Assert.Equal(
            1,
            recovery.ReleaseCount);

        Assert.Equal(
            0,
            recovery.ActiveRecoveryCount);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                    == OperationJournalState.Prepared
                && entry.Recovery is not null);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                    == OperationJournalState.Committed
                && entry.Recovery is not null);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                    == OperationJournalState.Completed
                && entry.Recovery is not null);
    }

    [Fact]
    public async Task ExecuteAsync_MoveSourceReadFailure_SkipsAffectedGroupAndContinues()
    {
        using var directory =
            new TestDirectory();

        var brokenSource =
            directory.CreateFile(
                "source/broken.jpg",
                "broken-content");

        var goodSource =
            directory.CreateFile(
                "source/good.jpg",
                "good-content");

        var brokenDestination =
            directory.Resolve(
                "target/broken.jpg");

        var goodDestination =
            directory.Resolve(
                "target/good.jpg");

        var plan =
            CreatePlanWithSeparateMoveGroups(
                new[]
                {
                    OperationSpec.Move(
                        brokenSource,
                        brokenDestination),
                    OperationSpec.Move(
                        goodSource,
                        goodDestination)
                });

        var recovery =
            new FakePersistentRecoveryStore
            {
                SourceReadFailureOnPreserveCall = 1
            };

        var health =
            new FakeStorageHealthService();

        var progressValues =
            new List<MediaSortLiveExecutionProgress>();

        var result =
            await CreateExecutor(
                    new FakeJournal(),
                    recoveryStore:
                        recovery,
                    storageHealthService:
                        health)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan),
                    new InlineProgress<MediaSortLiveExecutionProgress>(
                        value =>
                            progressValues.Add(
                                value)));

        Assert.Equal(
            MediaSortLiveExecutionState.CompletedWithIssues,
            result.State);

        Assert.Equal(
            1,
            result.CompletedOperationCount);

        Assert.Equal(
            1,
            result.SkippedOperationCount);

        Assert.Equal(
            1,
            result.SkippedGroupCount);

        var problem =
            Assert.Single(
                result.Problems);

        Assert.Equal(
            MediaSortExecutionProblemKind.SuspectedStorageFailure,
            problem.Kind);

        Assert.Equal(
            Path.GetFullPath(brokenSource),
            problem.SourcePath);

        Assert.Equal(
            1,
            health.InspectionCount);

        Assert.True(
            File.Exists(
                brokenSource));

        Assert.False(
            File.Exists(
                brokenDestination));

        Assert.False(
            File.Exists(
                goodSource));

        Assert.True(
            File.Exists(
                goodDestination));

        Assert.Contains(
            progressValues,
            value =>
                value.LatestProblem is not null
                && value.SkippedOperationCount == 1);
    }

    [Fact]
    public async Task ExecuteAsync_AtomicMoveSourceReadFailure_SkipsWholeCompanionGroup()
    {
        using var directory =
            new TestDirectory();

        var raw =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw-content");

        var jpeg =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg-content");

        var xmp =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp-content");

        var rawDestination =
            directory.Resolve(
                "target/DSC0001.ARW");

        var jpegDestination =
            directory.Resolve(
                "target/DSC0001.JPG");

        var xmpDestination =
            directory.Resolve(
                "target/DSC0001.xmp");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        raw,
                        rawDestination),
                    OperationSpec.Move(
                        jpeg,
                        jpegDestination),
                    OperationSpec.Move(
                        xmp,
                        xmpDestination,
                        isSidecar: true)
                },
                atomicGroup: true);

        var recovery =
            new FakePersistentRecoveryStore
            {
                SourceReadFailureOnPreserveCall = 2
            };

        var result =
            await CreateExecutor(
                    new FakeJournal(),
                    recoveryStore:
                        recovery)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.CompletedWithIssues,
            result.State);

        Assert.Equal(
            0,
            result.CompletedOperationCount);

        Assert.Equal(
            3,
            result.SkippedOperationCount);

        Assert.Equal(
            1,
            result.SkippedGroupCount);

        Assert.All(
            new[]
            {
                raw,
                jpeg,
                xmp
            },
            source =>
                Assert.True(
                    File.Exists(
                        source)));

        Assert.All(
            new[]
            {
                rawDestination,
                jpegDestination,
                xmpDestination
            },
            destination =>
                Assert.False(
                    File.Exists(
                        destination)));

        Assert.Equal(
            0,
            recovery.ActiveRecoveryCount);

        var problem =
            Assert.Single(
                result.Problems);

        Assert.Equal(
            3,
            problem.SkippedOperationCount);
    }

    [Fact]
    public async Task ExecuteAsync_DateReviewNeedsExplicitApproval()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                },
                planningIssues:
                    new[]
                    {
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.ReviewRequiresConfirmation,
                            "Datum prüfen.")
                    });

        var executor =
            CreateExecutor(
                new FakeJournal());

        var blocked =
            await executor.ExecuteAsync(
                plan,
                new MediaSortExecutionConfirmation(
                    plan.Fingerprint,
                    MediaSortExecutionConfirmation.RequiredText,
                    ApproveDateReviews: false));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByPlan,
            blocked.State);

        Assert.False(
            File.Exists(
                destination));

        var approved =
            await executor.ExecuteAsync(
                plan,
                new MediaSortExecutionConfirmation(
                    plan.Fingerprint,
                    MediaSortExecutionConfirmation.RequiredText,
                    ApproveDateReviews: true));

        Assert.Equal(
            MediaSortLiveExecutionState.Completed,
            approved.State);

        Assert.True(
            File.Exists(
                source));

        Assert.True(
            File.Exists(
                destination));
    }

    [Fact]
    public async Task ExecuteAsync_CompanionReviewCannotBeOverriddenByDateApproval()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                },
                planningIssues:
                    new[]
                    {
                        new MediaSortExecutionIssue(
                            MediaSortExecutionIssueKind.CompanionReviewRequiresConfirmation,
                            "Dateigruppe unvollständig.")
                    });

        var result =
            await CreateExecutor(
                    new FakeJournal())
                .ExecuteAsync(
                    plan,
                    new MediaSortExecutionConfirmation(
                        plan.Fingerprint,
                        MediaSortExecutionConfirmation.RequiredText,
                        ApproveDateReviews: true));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByPlan,
            result.State);

        Assert.False(
            File.Exists(
                destination));
    }

    [Fact]
    public async Task ExecuteAsync_AtomicMove_AllTargetsAndSourcesExistBeforeDestructivePhase()
    {
        using var directory =
            new TestDirectory();

        var raw =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw-content");

        var jpeg =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg-content");

        var xmp =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp-content");

        var specs =
            new[]
            {
                OperationSpec.Move(
                    raw,
                    directory.Resolve(
                        "target/DSC0001.ARW")),
                OperationSpec.Move(
                    jpeg,
                    directory.Resolve(
                        "target/DSC0001.JPG")),
                OperationSpec.Move(
                    xmp,
                    directory.Resolve(
                        "target/DSC0001.xmp"),
                    isSidecar: true)
            };

        var plan =
            CreatePlan(
                specs,
                atomicGroup: true);

        var destructiveGateObserved =
            false;

        var journal =
            new FakeJournal
            {
                OnAppend =
                    entry =>
                    {
                        if (destructiveGateObserved
                            || entry.State
                                != OperationJournalState.Executing
                            || entry.Message is null
                            || !entry.Message.Contains(
                                "destruktive Commit-Phase beginnt jetzt",
                                StringComparison.Ordinal))
                        {
                            return;
                        }

                        destructiveGateObserved =
                            true;

                        Assert.All(
                            specs,
                            spec =>
                            {
                                Assert.True(
                                    File.Exists(
                                        spec.SourcePath));

                                Assert.True(
                                    File.Exists(
                                        spec.DestinationPath));
                            });
                    }
            };

        var result =
            await CreateExecutor(
                    journal)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Completed,
            result.State);

        Assert.True(
            destructiveGateObserved);

        Assert.All(
            specs,
            spec =>
            {
                Assert.False(
                    File.Exists(
                        spec.SourcePath));

                Assert.True(
                    File.Exists(
                        spec.DestinationPath));
            });
    }

    [Fact]
    public async Task ExecuteAsync_MoveRecoveryPreserveFailure_LeavesAllSourcesAndNoTargets()
    {
        using var directory =
            new TestDirectory();

        var first =
            directory.CreateFile(
                "source/first.jpg",
                "first");

        var second =
            directory.CreateFile(
                "source/second.jpg",
                "second");

        var firstDestination =
            directory.Resolve(
                "target/first.jpg");

        var secondDestination =
            directory.Resolve(
                "target/second.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        first,
                        firstDestination),
                    OperationSpec.Move(
                        second,
                        secondDestination)
                },
                atomicGroup: true);

        var recovery =
            new FakePersistentRecoveryStore
            {
                FailOnPreserveCall = 2
            };

        var result =
            await CreateExecutor(
                    new FakeJournal(),
                    recoveryStore:
                        recovery)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Failed,
            result.State);

        Assert.True(
            File.Exists(
                first));

        Assert.True(
            File.Exists(
                second));

        Assert.False(
            File.Exists(
                firstDestination));

        Assert.False(
            File.Exists(
                secondDestination));

        Assert.Equal(
            0,
            recovery.ActiveRecoveryCount);
    }

    [Fact]
    public async Task ExecuteAsync_MoveJournalFailureAfterFirstSourceDeletion_RetainsRecovery()
    {
        using var directory =
            new TestDirectory();

        var first =
            directory.CreateFile(
                "source/first.jpg",
                "first");

        var second =
            directory.CreateFile(
                "source/second.jpg",
                "second");

        var firstDestination =
            directory.Resolve(
                "target/first.jpg");

        var secondDestination =
            directory.Resolve(
                "target/second.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Move(
                        first,
                        firstDestination),
                    OperationSpec.Move(
                        second,
                        secondDestination)
                },
                atomicGroup: true);

        var journal =
            new FakeJournal
            {
                FailOnCommittedNumber = 1
            };

        var recovery =
            new FakePersistentRecoveryStore();

        var result =
            await CreateExecutor(
                    journal,
                    recoveryStore:
                        recovery)
                .ExecuteAsync(
                    plan,
                    ConfirmMove(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.RecoveryRequired,
            result.State);

        Assert.False(
            File.Exists(
                first));

        Assert.True(
            File.Exists(
                second));

        Assert.True(
            File.Exists(
                firstDestination));

        Assert.True(
            File.Exists(
                secondDestination));

        Assert.Equal(
            2,
            recovery.ActiveRecoveryCount);

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.RecoveryRequired);
    }

    [Fact]
    public async Task ExecuteAsync_GroupWithOccupiedDestination_CommitsNothing()
    {
        using var directory =
            new TestDirectory();

        var raw =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw-content");

        var jpeg =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg-content");

        var xmp =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp-content");

        var rawDestination =
            directory.Resolve(
                "target/DSC0001.ARW");

        var jpegDestination =
            directory.Resolve(
                "target/DSC0001.JPG");

        var xmpDestination =
            directory.CreateFile(
                "target/DSC0001.xmp",
                "already-there");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        raw,
                        rawDestination),
                    OperationSpec.Copy(
                        jpeg,
                        jpegDestination),
                    OperationSpec.Copy(
                        xmp,
                        xmpDestination,
                        isSidecar: true)
                },
                atomicGroup: true);

        var result =
            await CreateExecutor(
                    new FakeJournal())
                .ExecuteAsync(
                    plan,
                    Confirm(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Failed,
            result.State);

        Assert.False(
            File.Exists(
                rawDestination));

        Assert.False(
            File.Exists(
                jpegDestination));

        Assert.Equal(
            "already-there",
            File.ReadAllText(
                xmpDestination));

        Assert.True(
            File.Exists(
                raw));

        Assert.True(
            File.Exists(
                jpeg));

        Assert.True(
            File.Exists(
                xmp));
    }

    [Fact]
    public async Task ExecuteAsync_AtomicCompanionCopy_CopiesAllMembersAndKeepsAllSources()
    {
        using var directory =
            new TestDirectory();

        var raw =
            directory.CreateFile(
                "source/DSC0001.ARW",
                "raw-content");

        var jpeg =
            directory.CreateFile(
                "source/DSC0001.JPG",
                "jpeg-content");

        var xmp =
            directory.CreateFile(
                "source/DSC0001.xmp",
                "xmp-content");

        var specs =
            new[]
            {
                OperationSpec.Copy(
                    raw,
                    directory.Resolve(
                        "target/DSC0001.ARW")),
                OperationSpec.Copy(
                    jpeg,
                    directory.Resolve(
                        "target/DSC0001.JPG")),
                OperationSpec.Copy(
                    xmp,
                    directory.Resolve(
                        "target/DSC0001.xmp"),
                    isSidecar: true)
            };

        var plan =
            CreatePlan(
                specs,
                atomicGroup: true);

        var result =
            await CreateExecutor(
                    new FakeJournal())
                .ExecuteAsync(
                    plan,
                    Confirm(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Completed,
            result.State);

        Assert.Equal(
            3,
            result.CompletedOperationCount);

        foreach (var spec in specs)
        {
            Assert.True(
                File.Exists(
                    spec.SourcePath));

            Assert.True(
                File.Exists(
                    spec.DestinationPath));

            Assert.Equal(
                File.ReadAllBytes(
                    spec.SourcePath),
                File.ReadAllBytes(
                    spec.DestinationPath));
        }
    }

    [Fact]
    public async Task ExecuteAsync_JournalFailureAfterCommit_ReturnsRecoveryRequired()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo-content");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal
            {
                FailOnCompleted =
                    true
            };

        var result =
            await CreateExecutor(
                    journal)
                .ExecuteAsync(
                    plan,
                    Confirm(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.RecoveryRequired,
            result.State);

        Assert.True(
            File.Exists(
                source));

        Assert.True(
            File.Exists(
                destination));

        Assert.Contains(
            journal.Entries,
            entry =>
                entry.State
                == OperationJournalState.RecoveryRequired);
    }

    [Fact]
    public async Task ExecuteAsync_SourceChangedAfterPlan_FailsClosedEvenIfValidatorSaysValid()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        File.AppendAllText(
            source,
            "-changed");

        var journal =
            new FakeJournal();

        var result =
            await CreateExecutor(
                    journal)
                .ExecuteAsync(
                    plan,
                    Confirm(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.Failed,
            result.State);

        Assert.False(
            File.Exists(
                destination));

        Assert.Empty(
            journal.Entries);
    }

    [Fact]
    public async Task ExecuteAsync_FreshValidationFailure_BlocksBeforeJournalAndCopy()
    {
        using var directory =
            new TestDirectory();

        var source =
            directory.CreateFile(
                "source/photo.jpg",
                "photo");

        var destination =
            directory.Resolve(
                "target/photo.jpg");

        var plan =
            CreatePlan(
                new[]
                {
                    OperationSpec.Copy(
                        source,
                        destination)
                });

        var journal =
            new FakeJournal();

        var validation =
            new MediaSortExecutionValidationResult(
                new[]
                {
                    new MediaSortExecutionIssue(
                        MediaSortExecutionIssueKind.DestinationAppeared,
                        "Test: Zustand hat sich geändert.")
                });

        var result =
            await CreateExecutor(
                    journal,
                    validation)
                .ExecuteAsync(
                    plan,
                    Confirm(
                        plan));

        Assert.Equal(
            MediaSortLiveExecutionState.BlockedByValidation,
            result.State);

        Assert.False(
            File.Exists(
                destination));

        Assert.Empty(
            journal.Entries);
    }

    private static MediaSortExecutionExecutor CreateExecutor(
        FakeJournal journal,
        MediaSortExecutionValidationResult? validation = null,
        FakePersistentRecoveryStore? recoveryStore = null,
        FakeStorageHealthService? storageHealthService = null)
    {
        return new MediaSortExecutionExecutor(
            new FakeExecutionPlanner(
                validation
                ?? MediaSortExecutionValidationResult.Valid),
            new FakeProcessLock(),
            journal,
            recoveryStore
            ?? new FakePersistentRecoveryStore(),
            storageHealthService
            ?? new FakeStorageHealthService());
    }

    private static MediaSortExecutionConfirmation Confirm(
        MediaSortExecutionPlan plan)
    {
        return new MediaSortExecutionConfirmation(
            plan.Fingerprint,
            MediaSortExecutionConfirmation.CopyRequiredText,
            ApproveDateReviews: false);
    }

    private static MediaSortExecutionConfirmation ConfirmMove(
        MediaSortExecutionPlan plan)
    {
        return new MediaSortExecutionConfirmation(
            plan.Fingerprint,
            MediaSortExecutionConfirmation.MoveRequiredText,
            ApproveDateReviews: false,
            ApproveSourceDeletion: true);
    }

    private static MediaSortExecutionPlan CreatePlan(
        IReadOnlyList<OperationSpec> specs,
        bool atomicGroup = false,
        IReadOnlyList<MediaSortExecutionIssue>? planningIssues = null)
    {
        var planItems =
            specs
                .Select(
                    (spec, index) =>
                        new FileOperationPlanItem(
                            index,
                            spec.Kind,
                            spec.SourcePath,
                            spec.DestinationPath))
                .ToArray();

        var filePlan =
            new FileOperationPlan(
                planItems,
                Array.Empty<FileOperationConflict>());

        var operations =
            specs
                .Select(
                    (spec, index) =>
                    {
                        var info =
                            new FileInfo(
                                spec.SourcePath);

                        return new MediaSortExecutionOperation(
                            index,
                            "group-1",
                            spec.Kind,
                            spec.SourcePath,
                            spec.DestinationPath,
                            spec.IsSidecar,
                            new MediaSortSourceSnapshot(
                                spec.SourcePath,
                                info.Length,
                                info.LastWriteTimeUtc));
                    })
                .ToArray();

        var group =
            new MediaSortExecutionGroup(
                "group-1",
                "Testgruppe",
                IsCompanionGroup:
                    atomicGroup,
                RequiresAtomicExecution:
                    atomicGroup,
                operations);

        return new MediaSortExecutionPlan(
            fingerprint:
                new string(
                    'A',
                    64),
            DateTimeOffset.UtcNow,
            new[]
            {
                group
            },
            filePlan,
            FileOperationPreflightResult.Safe,
            planningIssues
            ?? Array.Empty<MediaSortExecutionIssue>(),
            validationIssues:
                Array.Empty<MediaSortExecutionIssue>(),
            peakPersistentRecoveryBytes: 0);
    }

    private static MediaSortExecutionPlan CreatePlanWithSeparateMoveGroups(
        IReadOnlyList<OperationSpec> specs)
    {
        var planItems =
            specs
                .Select(
                    (spec, index) =>
                        new FileOperationPlanItem(
                            index,
                            spec.Kind,
                            spec.SourcePath,
                            spec.DestinationPath))
                .ToArray();

        var groups =
            specs
                .Select(
                    (spec, index) =>
                    {
                        var info =
                            new FileInfo(
                                spec.SourcePath);

                        var operation =
                            new MediaSortExecutionOperation(
                                index,
                                $"group-{index + 1}",
                                spec.Kind,
                                spec.SourcePath,
                                spec.DestinationPath,
                                spec.IsSidecar,
                                new MediaSortSourceSnapshot(
                                    spec.SourcePath,
                                    info.Length,
                                    info.LastWriteTimeUtc));

                        return new MediaSortExecutionGroup(
                            $"group-{index + 1}",
                            $"Testgruppe {index + 1}",
                            IsCompanionGroup: false,
                            RequiresAtomicExecution: false,
                            new[]
                            {
                                operation
                            });
                    })
                .ToArray();

        return new MediaSortExecutionPlan(
            fingerprint:
                new string(
                    'B',
                    64),
            DateTimeOffset.UtcNow,
            groups,
            new FileOperationPlan(
                planItems,
                Array.Empty<FileOperationConflict>()),
            FileOperationPreflightResult.Safe,
            planningIssues:
                Array.Empty<MediaSortExecutionIssue>(),
            validationIssues:
                Array.Empty<MediaSortExecutionIssue>(),
            peakPersistentRecoveryBytes: 0);
    }

    private sealed record OperationSpec(
        FileOperationKind Kind,
        string SourcePath,
        string DestinationPath,
        bool IsSidecar)
    {
        public static OperationSpec Copy(
            string sourcePath,
            string destinationPath,
            bool isSidecar = false)
        {
            return new OperationSpec(
                FileOperationKind.Copy,
                sourcePath,
                destinationPath,
                isSidecar);
        }

        public static OperationSpec Move(
            string sourcePath,
            string destinationPath,
            bool isSidecar = false)
        {
            return new OperationSpec(
                FileOperationKind.Move,
                sourcePath,
                destinationPath,
                isSidecar);
        }
    }

    private sealed class InlineProgress<T>
        : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(
            Action<T> report)
        {
            _report =
                report;
        }

        public void Report(
            T value)
        {
            _report(
                value);
        }
    }

    private sealed class FakeExecutionPlanner
        : IMediaSortExecutionPlanner
    {
        private readonly MediaSortExecutionValidationResult _validation;

        public FakeExecutionPlanner(
            MediaSortExecutionValidationResult validation)
        {
            _validation =
                validation;
        }

        public Task<MediaSortExecutionPlan> CreateAsync(
            MediaSortPlan sortPlan,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<MediaSortExecutionValidationResult> ValidateCurrentStateAsync(
            MediaSortExecutionPlan executionPlan,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _validation);
        }
    }

    private sealed class FakeProcessLock
        : IFileOperationProcessLock
    {
        public bool IsHeld =>
            true;

        public ValueTask<FileOperationProcessLockAcquireResult> TryAcquireAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return ValueTask.FromResult(
                new FileOperationProcessLockAcquireResult(
                    FileOperationProcessLockAcquireState.Acquired));
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeJournal
        : IOperationJournal
    {
        public List<OperationJournalEntry> Entries { get; } =
            new();

        private int _committedAttemptCount;

        public bool FailOnCompleted { get; init; }

        public int? FailOnCommittedNumber { get; init; }

        public Action<OperationJournalEntry>? OnAppend { get; init; }

        public Task AppendAsync(
            OperationJournalEntry entry,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OnAppend?.Invoke(
                entry);

            if (entry.State
                == OperationJournalState.Committed)
            {
                _committedAttemptCount++;

                if (FailOnCommittedNumber
                    == _committedAttemptCount)
                {
                    throw new IOException(
                        "Test: Committed-Journalfehler.");
                }
            }

            if (FailOnCompleted
                && entry.State
                == OperationJournalState.Completed)
            {
                throw new IOException(
                    "Test: Completed-Journalfehler.");
            }

            Entries.Add(
                entry);

            return Task.CompletedTask;
        }

        public Task<OperationJournalReadResult> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                new OperationJournalReadResult(
                    Entries,
                    skippedCorruptLineCount: 0));
        }

        public Task<IReadOnlyList<OperationJournalEntry>>
            GetIncompleteTransactionsAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<OperationJournalEntry> result =
                Entries
                    .GroupBy(entry =>
                        entry.TransactionId)
                    .Select(group =>
                        group.Last())
                    .Where(entry =>
                        entry.State
                        is not OperationJournalState.Completed
                        and not OperationJournalState.Failed
                        and not OperationJournalState.Recovered
                        and not OperationJournalState.Cancelled)
                    .ToArray();

            return Task.FromResult(
                result);
        }
    }

    private sealed class FakePersistentRecoveryStore
        : IPersistentRecoveryStore
    {
        private readonly Dictionary<string, byte[]> _items =
            new(StringComparer.Ordinal);

        public int PreserveCount { get; private set; }

        public int VerifyCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public int? FailOnPreserveCall { get; init; }

        public int? SourceReadFailureOnPreserveCall { get; init; }

        public int ActiveRecoveryCount =>
            _items.Count;

        public Task<RecoveryHandle> PreserveAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PreserveCount++;

            if (FailOnPreserveCall
                == PreserveCount)
            {
                throw new IOException(
                    "Test: Recovery-Preserve fehlgeschlagen.");
            }

            if (SourceReadFailureOnPreserveCall
                == PreserveCount)
            {
                throw new SourceReadIOException(
                    sourcePath,
                    "Testlesen",
                    new IOException(
                        "Input/output error"));
            }

            var fullPath =
                Path.GetFullPath(
                    sourcePath);

            var bytes =
                File.ReadAllBytes(
                    fullPath);

            var hash =
                Convert.ToHexString(
                    SHA256.HashData(
                        bytes));

            var id =
                Guid.NewGuid().ToString("N");

            _items.Add(
                id,
                bytes);

            var info =
                new FileInfo(
                    fullPath);

            return Task.FromResult(
                new RecoveryHandle(
                    id,
                    fullPath,
                    info.Length,
                    hash,
                    info.LastWriteTimeUtc,
                    DateTimeOffset.UtcNow,
                    RecoveryStorageKind.PersistentFile,
                    StorageLocation:
                        "fake://" + id));
        }

        public Task<bool> VerifyAsync(
            RecoveryHandle handle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            VerifyCount++;

            if (!_items.TryGetValue(
                    handle.Id,
                    out var bytes))
            {
                return Task.FromResult(
                    false);
            }

            var hash =
                Convert.ToHexString(
                    SHA256.HashData(
                        bytes));

            return Task.FromResult(
                bytes.LongLength == handle.Length
                && string.Equals(
                    hash,
                    handle.Sha256,
                    StringComparison.Ordinal));
        }

        public Task RestoreAsync(
            RecoveryHandle handle,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_items.TryGetValue(
                    handle.Id,
                    out var bytes))
            {
                throw new FileNotFoundException(
                    "Test-Recovery fehlt.",
                    handle.Id);
            }

            if (File.Exists(
                    destinationPath)
                || Directory.Exists(
                    destinationPath))
            {
                throw new IOException(
                    "Test-Recovery überschreibt nicht.");
            }

            var directory =
                Path.GetDirectoryName(
                    destinationPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllBytes(
                destinationPath,
                bytes);

            return Task.CompletedTask;
        }

        public Task ReleaseAsync(
            RecoveryHandle handle,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ReleaseCount++;

            _items.Remove(
                handle.Id);

            return Task.CompletedTask;
        }
    }

    private sealed class FakeStorageHealthService
        : IStorageHealthService
    {
        public int InspectionCount { get; private set; }

        public Task<StorageHealthSnapshot> InspectFailureAsync(
            string filePath,
            Exception exception,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectionCount++;

            return Task.FromResult(
                new StorageHealthSnapshot(
                    Path.GetFullPath(filePath),
                    StorageFailureKind.SuspectedDeviceIoFailure,
                    StorageHealthSeverity.Critical,
                    "Test: möglicher Datenträgerfehler.",
                    exception.GetBaseException().Message,
                    MountPoint: "/test",
                    FileSystemType: "testfs",
                    VolumeDevicePath: "/dev/test1",
                    PhysicalDevicePath: "/dev/test",
                    Model: "Test Disk",
                    SerialNumber: "TEST-1",
                    IsRotational: true,
                    CapacityBytes: 1_000_000));
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

        public string CreateFile(
            string relativePath,
            string content)
        {
            var path =
                Resolve(
                    relativePath);

            var directory =
                Path.GetDirectoryName(
                    path);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                path,
                content);

            return path;
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
