using System.Text.Json;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Storage;
using Elbwald.DesktopTools.Core.Tests.Support;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Storage;

public sealed class JsonStorageSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoadPreservesGenericEndpoints()
    {
        using var temp = new TemporaryDirectory();
        var appPaths = new FakeAppPaths(temp.RootPath);
        var store = new JsonStorageSettingsStore(appPaths);
        var expected = new StorageSettings()
            .WithEndpoint(new StorageEndpoint(
                StorageEndpoint.DefaultImportId,
                "Import-Wartehalle",
                StorageEndpointKind.Import,
                new StorageLocation("import-volume", "Incoming/Photos")))
            .WithEndpoint(new StorageEndpoint(
                "archive.2020",
                "Fotoarchiv 2020",
                StorageEndpointKind.Archive,
                new StorageLocation("archive-volume", "Photos/2020")));

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.Endpoints.Count, actual.Endpoints.Count);
        Assert.Equal(expected.Endpoints[0], actual.Endpoints[0]);
        Assert.Equal(expected.Endpoints[1], actual.Endpoints[1]);
    }

    [Fact]
    public async Task LoadReturnsEmptyEndpointCollectionWhenFileDoesNotExist()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonStorageSettingsStore(new FakeAppPaths(temp.RootPath));

        var settings = await store.LoadAsync();

        Assert.Empty(settings.Endpoints);
    }

    [Fact]
    public async Task LoadMigratesVersion1LocationsToStandardEndpoints()
    {
        using var temp = new TemporaryDirectory();
        var appPaths = new FakeAppPaths(temp.RootPath);
        Directory.CreateDirectory(appPaths.ConfigDirectory);
        var path = Path.Combine(appPaths.ConfigDirectory, "storage.json");
        await File.WriteAllTextAsync(path, """
            {
              "Version": 1,
              "ImportHoldingArea": {
                "VolumeId": "import-volume",
                "RelativePath": "Incoming"
              },
              "MediaLibrary": {
                "VolumeId": "library-volume",
                "RelativePath": "Media"
              }
            }
            """);

        var store = new JsonStorageSettingsStore(appPaths);
        var settings = await store.LoadAsync();

        Assert.Equal(2, settings.Endpoints.Count);
        Assert.Equal("import-volume", settings.Find(StorageEndpoint.DefaultImportId)!.Location.VolumeId);
        Assert.Equal("library-volume", settings.Find(StorageEndpoint.DefaultLibraryId)!.Location.VolumeId);
    }

    [Fact]
    public async Task SaveRejectsDuplicateEndpointIds()
    {
        using var temp = new TemporaryDirectory();
        var store = new JsonStorageSettingsStore(new FakeAppPaths(temp.RootPath));
        var settings = new StorageSettings(new[]
        {
            new StorageEndpoint("same", "A", StorageEndpointKind.Custom, new StorageLocation("v1", "A")),
            new StorageEndpoint("same", "B", StorageEndpointKind.Custom, new StorageLocation("v2", "B"))
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(settings));
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
