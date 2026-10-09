using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.PhotoSort.ViewModels;

namespace Elbwald.DesktopTools.Core.Tests.PhotoSort;

public sealed class PhotoSortExecutionReportViewModelTests
{
    [Fact]
    public void FromResult_CompletedMove_ShowsSuccessfulSafetySummary()
    {
        var result =
            new MediaSortLiveExecutionResult(
                MediaSortLiveExecutionState.Completed,
                completedOperationCount: 8,
                totalOperationCount: 8,
                completedGroupCount: 3,
                totalGroupCount: 3);

        var report =
            PhotoSortExecutionReportViewModel.FromResult(
                result,
                isMove: true,
                TimeSpan.FromSeconds(65));

        Assert.True(report.IsSuccess);
        Assert.False(report.IsWarning);
        Assert.False(report.IsFailure);
        Assert.False(report.RequiresRecovery);
        Assert.Equal("Verschieben", report.OperationLabel);
        Assert.Equal("8 / 8", report.SuccessfulOperationsText);
        Assert.Equal("00:01:05", report.DurationText);
        Assert.Contains("Quellen wurden nur nach verifizierter", report.SafetyText);
        Assert.Contains("Recovery: nicht erforderlich", report.RecoveryText);
        Assert.False(report.HasDiagnosticsHint);
    }

    [Fact]
    public void FromResult_CompletedWithIssues_ShowsSkippedGroupAndDiagnosticsHint()
    {
        var storageHealth =
            new StorageHealthSnapshot(
                "/source/broken.jpg",
                StorageFailureKind.SuspectedDeviceIoFailure,
                StorageHealthSeverity.Critical,
                "Möglicher Datenträgerfehler.");

        var problem =
            new MediaSortExecutionProblem(
                MediaSortExecutionProblemKind.SuspectedStorageFailure,
                "/source/broken.jpg",
                "/target/broken.jpg",
                2,
                "Gruppe sicher übersprungen.",
                storageHealth);

        var result =
            new MediaSortLiveExecutionResult(
                MediaSortLiveExecutionState.CompletedWithIssues,
                completedOperationCount: 6,
                totalOperationCount: 8,
                completedGroupCount: 2,
                totalGroupCount: 3,
                skippedOperationCount: 2,
                skippedGroupCount: 1,
                problems: new[] { problem });

        var report =
            PhotoSortExecutionReportViewModel.FromResult(
                result,
                isMove: true,
                TimeSpan.FromMinutes(2));

        Assert.False(report.IsSuccess);
        Assert.True(report.IsWarning);
        Assert.False(report.IsFailure);
        Assert.Equal("2", report.SkippedOperationsText);
        Assert.Contains("1 übersprungen", report.GroupsText);
        Assert.Contains("nicht teilweise gelöscht", report.SafetyText);
        Assert.True(report.HasDiagnosticsHint);
        Assert.Contains("Werkzeuge → Protokoll", report.DiagnosticsText);
    }

    [Fact]
    public void FromResult_RecoveryRequired_FailsClosedInReport()
    {
        var result =
            new MediaSortLiveExecutionResult(
                MediaSortLiveExecutionState.RecoveryRequired,
                completedOperationCount: 1,
                totalOperationCount: 4,
                completedGroupCount: 0,
                totalGroupCount: 2,
                errorMessage: "Recovery muss geprüft werden.");

        var report =
            PhotoSortExecutionReportViewModel.FromResult(
                result,
                isMove: true,
                TimeSpan.FromSeconds(12));

        Assert.True(report.IsFailure);
        Assert.True(report.RequiresRecovery);
        Assert.Equal("Recovery erforderlich", report.StateLabel);
        Assert.Contains("Fail closed", report.SafetyText);
        Assert.Contains("nicht manuell löschen", report.RecoveryText);
    }

    [Fact]
    public void FromUnexpectedFailure_DoesNotClaimKnownRecoveryState()
    {
        var report =
            PhotoSortExecutionReportViewModel.FromUnexpectedFailure(
                isMove: true,
                TimeSpan.FromSeconds(3),
                "Unerwarteter Fehler.",
                cancelled: false);

        Assert.True(report.IsFailure);
        Assert.True(report.RequiresRecovery);
        Assert.Contains("nicht aus dem Ergebnis ableitbar", report.RecoveryText);
        Assert.Contains("nichts manuell löschen", report.RecoveryText);
    }
}
