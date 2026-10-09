using Elbwald.DesktopTools.Contracts.Diagnostics;
using Elbwald.DesktopTools.Core.Diagnostics;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Diagnostics;

public sealed class SqliteDiagnosticEventStoreTests
{
    [Fact]
    public async Task TryWriteAndQueryAsync_PersistsStructuredStorageEvent()
    {
        using var directory =
            new TestDirectory();

        var store =
            new SqliteDiagnosticEventStore(
                new DiagnosticEventStoreOptions
                {
                    DatabasePath =
                        directory.Resolve(
                            "diagnostics/events.db")
                });

        var written =
            await store.TryWriteAsync(
                new DiagnosticEventWrite(
                    new DateTimeOffset(
                        2026,
                        10,
                        9,
                        8,
                        30,
                        0,
                        TimeSpan.Zero),
                    DiagnosticEventSeverity.Critical,
                    DiagnosticEventCategory.Storage,
                    "StorageHealth",
                    "Möglicher Datenträgerfehler.",
                    "Input/output error",
                    FilePath: "/media/archive/photo.jpg",
                    MountPoint: "/media/archive",
                    FileSystemType: "ntfs",
                    VolumeDevicePath: "/dev/sdd1",
                    PhysicalDevicePath: "/dev/sdd",
                    DeviceModel: "SAMSUNG HD204UI",
                    DeviceSerialNumber: "TESTSERIAL",
                    IsRotational: true,
                    CapacityBytes: 2_000_398_934_016,
                    OperationKind: "SourceRead",
                    ErrorCode: "0x00000005"));

        Assert.True(written);

        var result =
            await store.QueryAsync(
                new DiagnosticEventQuery(
                    MinimumSeverity:
                        DiagnosticEventSeverity.Warning,
                    Category:
                        DiagnosticEventCategory.Storage,
                    SearchText:
                        "HD204UI"));

        var entry =
            Assert.Single(result);

        Assert.Equal(
            DiagnosticEventSeverity.Critical,
            entry.Severity);

        Assert.Equal(
            DiagnosticEventCategory.Storage,
            entry.Category);

        Assert.Equal(
            "/dev/sdd",
            entry.PhysicalDevicePath);

        Assert.Equal(
            "SAMSUNG HD204UI",
            entry.DeviceModel);

        Assert.Equal(
            "/media/archive",
            entry.MountPoint);

        Assert.Equal(
            "ntfs",
            entry.FileSystemType);

        Assert.True(
            entry.IsRotational == true);

        Assert.Equal(
            2_000_398_934_016L,
            entry.CapacityBytes.GetValueOrDefault());

        Assert.Equal(
            "/media/archive/photo.jpg",
            entry.FilePath);

        Assert.Equal(
            "SourceRead",
            entry.OperationKind);
    }

    [Fact]
    public async Task QueryAsync_AppliesMinimumSeverityAndCategory()
    {
        using var directory =
            new TestDirectory();

        var store =
            new SqliteDiagnosticEventStore(
                new DiagnosticEventStoreOptions
                {
                    DatabasePath =
                        directory.Resolve(
                            "events.db")
                });

        await store.TryWriteAsync(
            CreateEvent(
                DiagnosticEventSeverity.Information,
                DiagnosticEventCategory.Application,
                "Info"));

        await store.TryWriteAsync(
            CreateEvent(
                DiagnosticEventSeverity.Warning,
                DiagnosticEventCategory.Storage,
                "Warnung"));

        await store.TryWriteAsync(
            CreateEvent(
                DiagnosticEventSeverity.Critical,
                DiagnosticEventCategory.Storage,
                "Kritisch"));

        var result =
            await store.QueryAsync(
                new DiagnosticEventQuery(
                    MinimumSeverity:
                        DiagnosticEventSeverity.Critical,
                    Category:
                        DiagnosticEventCategory.Storage));

        var entry =
            Assert.Single(result);

        Assert.Equal(
            "Kritisch",
            entry.Message);
    }

    [Fact]
    public async Task TryWriteAsync_InvalidDatabaseLocation_FailsBestEffort()
    {
        using var directory =
            new TestDirectory();

        var blocker =
            directory.Resolve(
                "blocker");

        await File.WriteAllTextAsync(
            blocker,
            "not-a-directory");

        var store =
            new SqliteDiagnosticEventStore(
                new DiagnosticEventStoreOptions
                {
                    DatabasePath =
                        Path.Combine(
                            blocker,
                            "events.db")
                });

        var written =
            await store.TryWriteAsync(
                CreateEvent(
                    DiagnosticEventSeverity.Error,
                    DiagnosticEventCategory.Application,
                    "Nicht speicherbar"));

        Assert.False(written);
    }

    private static DiagnosticEventWrite CreateEvent(
        DiagnosticEventSeverity severity,
        DiagnosticEventCategory category,
        string message)
    {
        return new DiagnosticEventWrite(
            DateTimeOffset.UtcNow,
            severity,
            category,
            "Test",
            message);
    }

    private sealed class TestDirectory
        : IDisposable
    {
        private readonly string _root =
            Path.Combine(
                Path.GetTempPath(),
                "elbwald-diagnostic-tests-"
                + Guid.NewGuid().ToString("N"));

        public TestDirectory()
        {
            Directory.CreateDirectory(
                _root);
        }

        public string Resolve(
            string relativePath)
        {
            return Path.Combine(
                _root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(
                        _root,
                        recursive: true);
                }
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }
}
