using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Core.Media.Importing;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class JsonMediaImportHistoryStoreTests
{
    [Fact]
    public async Task SaveAndLoadPreservesLastImport()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonMediaImportHistoryStore(new FakeAppPaths(temp.RootPath));
        var expected = new MediaImportHistoryEntry(
            SourcePath: Path.Combine(temp.RootPath, "Kamera"),
            DestinationRootPath: Path.Combine(temp.RootPath, "Import"),
            DestinationPath: Path.Combine(temp.RootPath, "Import", "2026-10-11 - Kamera"),
            DestinationSubfolderName: "2026-10-11 - Kamera",
            CompletedUtc: new DateTimeOffset(2026, 10, 11, 8, 30, 0, TimeSpan.Zero),
            CopiedCount: 125,
            AlreadyImportedCount: 4,
            ConflictCount: 1);

        await store.SaveLastAsync(expected);
        var actual = await store.LoadLastAsync();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task LoadReturnsNullForCorruptHistory()
    {
        using var temp = new TemporaryDirectory();
        var appPaths = new FakeAppPaths(temp.RootPath);
        Directory.CreateDirectory(appPaths.ConfigDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(appPaths.ConfigDirectory, "media-import-history.json"),
            "{broken-json");

        var store = new JsonMediaImportHistoryStore(appPaths);
        var actual = await store.LoadLastAsync();

        Assert.Null(actual);
    }

    private sealed class FakeAppPaths : IAppPaths
    {
        public FakeAppPaths(string root)
        {
            ApplicationBaseDirectory = root;
            ApplicationDataDirectory = Path.Combine(root, "data");
            ConfigDirectory = Path.Combine(ApplicationDataDirectory, "Config");
            DatabaseDirectory = Path.Combine(ApplicationDataDirectory, "Databases");
            CacheDirectory = Path.Combine(ApplicationDataDirectory, "Cache");
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
