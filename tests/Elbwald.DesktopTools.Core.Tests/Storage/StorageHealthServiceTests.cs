using Elbwald.DesktopTools.Contracts.Diagnostics;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Storage;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Storage;

public sealed class StorageHealthServiceTests
{
    [Fact]
    public async Task InspectFailureAsync_InputOutputError_IsCriticalAndDoesNotAutoQuerySmart()
    {
        var diagnosticStore =
            new FakeDiagnosticEventStore();

        var service =
            new StorageHealthService(
                diagnosticStore);

        var path =
            Path.Combine(
                Path.GetTempPath(),
                "elbwald-storage-health-test.jpg");

        var exception =
            new SourceReadIOException(
                path,
                "Testlesen",
                new IOException(
                    "Input/output error"));

        var result =
            await service.InspectFailureAsync(
                path,
                exception);

        Assert.Equal(
            StorageFailureKind.SuspectedDeviceIoFailure,
            result.FailureKind);

        Assert.Equal(
            StorageHealthSeverity.Critical,
            result.Severity);

        Assert.True(
            result.SuspectsPhysicalDevice);

        Assert.False(
            result.SmartDataWasQueried);

        Assert.Contains(
            "nicht automatisch",
            result.SmartNote,
            StringComparison.OrdinalIgnoreCase);

        var logged =
            Assert.Single(
                diagnosticStore.Events);

        Assert.Equal(
            DiagnosticEventSeverity.Critical,
            logged.Severity);

        Assert.Equal(
            DiagnosticEventCategory.Storage,
            logged.Category);

        Assert.Equal(
            Path.GetFullPath(path),
            logged.FilePath);

        Assert.Equal(
            "SourceRead",
            logged.OperationKind);
    }

    [Fact]
    public async Task InspectFailureAsync_NormalReadIOException_IsWarning()
    {
        var service =
            new StorageHealthService(
                new FakeDiagnosticEventStore());

        var path =
            Path.Combine(
                Path.GetTempPath(),
                "elbwald-storage-health-warning-test.jpg");

        var exception =
            new SourceReadIOException(
                path,
                "Testlesen",
                new IOException(
                    "Test read failure"));

        var result =
            await service.InspectFailureAsync(
                path,
                exception);

        Assert.Equal(
            StorageFailureKind.SourceReadFailure,
            result.FailureKind);

        Assert.Equal(
            StorageHealthSeverity.Warning,
            result.Severity);

        Assert.False(
            result.SuspectsPhysicalDevice);
    }

    [Fact]
    public async Task InspectFailureAsync_DiagnosticStoreFailure_DoesNotHideStorageResult()
    {
        var service =
            new StorageHealthService(
                new ThrowingDiagnosticEventStore());

        var path =
            Path.Combine(
                Path.GetTempPath(),
                "elbwald-storage-health-diagnostic-failure.jpg");

        var result =
            await service.InspectFailureAsync(
                path,
                new SourceReadIOException(
                    path,
                    "Testlesen",
                    new IOException(
                        "Input/output error")));

        Assert.Equal(
            StorageFailureKind.SuspectedDeviceIoFailure,
            result.FailureKind);

        Assert.Equal(
            StorageHealthSeverity.Critical,
            result.Severity);
    }

    private sealed class FakeDiagnosticEventStore
        : IDiagnosticEventStore
    {
        public List<DiagnosticEventWrite> Events { get; } =
            new();

        public ValueTask<bool> TryWriteAsync(
            DiagnosticEventWrite diagnosticEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Add(diagnosticEvent);
            return ValueTask.FromResult(true);
        }

        public Task<IReadOnlyList<DiagnosticEventEntry>> QueryAsync(
            DiagnosticEventQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DiagnosticEventEntry>>(
                Array.Empty<DiagnosticEventEntry>());
        }
    }
    private sealed class ThrowingDiagnosticEventStore
        : IDiagnosticEventStore
    {
        public ValueTask<bool> TryWriteAsync(
            DiagnosticEventWrite diagnosticEvent,
            CancellationToken cancellationToken = default)
        {
            throw new IOException(
                "Test: Diagnose-DB nicht verfügbar.");
        }

        public Task<IReadOnlyList<DiagnosticEventEntry>> QueryAsync(
            DiagnosticEventQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new IOException(
                "Test: Diagnose-DB nicht verfügbar.");
        }
    }

}
