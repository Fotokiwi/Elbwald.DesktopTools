using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Core.Maintenance;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Maintenance;

public sealed class ApplicationDataResetServiceTests
{
    [Fact]
    public async Task ResetClearsCachesAndDatabasesButPreservesConfigurationAndRecovery()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "elbwald-reset-tests",
            Guid.NewGuid().ToString("N"));
        var paths = new TestAppPaths(root);

        try
        {
            Directory.CreateDirectory(paths.CacheDirectory);
            Directory.CreateDirectory(paths.DatabaseDirectory);
            Directory.CreateDirectory(paths.DiagnosticsDirectory);
            Directory.CreateDirectory(paths.ConfigDirectory);
            Directory.CreateDirectory(paths.RecoveryDirectory);
            Directory.CreateDirectory(Path.Combine(paths.DiagnosticsDirectory, "import-cleanup"));

            await File.WriteAllTextAsync(Path.Combine(paths.CacheDirectory, "media-analysis.db"), "cache");
            await File.WriteAllTextAsync(Path.Combine(paths.DatabaseDirectory, "media-index.db"), "index");
            await File.WriteAllTextAsync(Path.Combine(paths.DatabaseDirectory, "projects.db"), "projects");
            await File.WriteAllTextAsync(Path.Combine(paths.DiagnosticsDirectory, "events.db"), "events");
            await File.WriteAllTextAsync(Path.Combine(paths.DiagnosticsDirectory, "import-cleanup", "keep.txt"), "keep");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "media-analysis-history.json"), "history");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "media-import-history.json"), "history");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "storage.json"), "settings");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "extensions.json"), "extensions");
            await File.WriteAllTextAsync(Path.Combine(paths.RecoveryDirectory, "keep.bin"), "recovery");

            var service = new ApplicationDataResetService(paths);

            var result = await service.ResetVolatileDataAsync();

            Assert.Empty(Directory.EnumerateFileSystemEntries(paths.CacheDirectory));
            Assert.Empty(Directory.EnumerateFileSystemEntries(paths.DatabaseDirectory));
            Assert.False(File.Exists(Path.Combine(paths.DiagnosticsDirectory, "events.db")));
            Assert.True(File.Exists(Path.Combine(paths.DiagnosticsDirectory, "import-cleanup", "keep.txt")));
            Assert.False(File.Exists(Path.Combine(paths.ConfigDirectory, "media-analysis-history.json")));
            Assert.False(File.Exists(Path.Combine(paths.ConfigDirectory, "media-import-history.json")));
            Assert.True(File.Exists(Path.Combine(paths.ConfigDirectory, "storage.json")));
            Assert.True(File.Exists(Path.Combine(paths.ConfigDirectory, "extensions.json")));
            Assert.True(File.Exists(Path.Combine(paths.RecoveryDirectory, "keep.bin")));
            Assert.True(result.DeletedFileCount >= 6);
            Assert.Contains("Cache", result.ClearedTargets);
            Assert.Contains("Datenbanken", result.ClearedTargets);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [Fact]
    public async Task ResetRefusesApplicationDataRootAsDeletionTarget()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "elbwald-reset-tests",
            Guid.NewGuid().ToString("N"));
        var paths = new TestAppPaths(root, unsafeCacheDirectory: true);

        try
        {
            Directory.CreateDirectory(paths.ApplicationDataDirectory);
            var sentinel = Path.Combine(paths.ApplicationDataDirectory, "must-stay.txt");
            await File.WriteAllTextAsync(sentinel, "keep");

            var service = new ApplicationDataResetService(paths);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ResetVolatileDataAsync());

            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TestAppPaths : IAppPaths
    {
        public TestAppPaths(string root, bool unsafeCacheDirectory = false)
        {
            ApplicationBaseDirectory = root;
            ApplicationDataDirectory = Path.Combine(root, "data");
            ConfigDirectory = Path.Combine(ApplicationDataDirectory, "Config");
            DatabaseDirectory = Path.Combine(ApplicationDataDirectory, "Databases");
            CacheDirectory = unsafeCacheDirectory
                ? ApplicationDataDirectory
                : Path.Combine(ApplicationDataDirectory, "Cache");
            DiagnosticsDirectory = Path.Combine(ApplicationDataDirectory, "Diagnostics");
            RecoveryDirectory = Path.Combine(ApplicationDataDirectory, "Recovery");
            PortableModeMarkerPath = Path.Combine(root, "portable.flag");
        }

        public bool IsPortable => true;
        public string ApplicationBaseDirectory { get; }
        public string ApplicationDataDirectory { get; }
        public string ConfigDirectory { get; }
        public string DatabaseDirectory { get; }
        public string CacheDirectory { get; }
        public string DiagnosticsDirectory { get; }
        public string RecoveryDirectory { get; }
        public string PortableModeMarkerPath { get; }
    }
}
