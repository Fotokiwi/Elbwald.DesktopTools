using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.Core.Tests.Modules;

public sealed class JsonExtensionActivationServiceTests
{
    [Fact]
    public void SetEnabled_PersistsDisabledStateAcrossInstances()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "elbwald-extension-activation-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new TestAppPaths(root);
            var service = new JsonExtensionActivationService(paths);

            Assert.True(service.IsEnabled("elbwald.image.formats.extended"));
            Assert.True(service.SetEnabled("elbwald.image.formats.extended", false));
            Assert.False(service.IsEnabled("elbwald.image.formats.extended"));

            var reloaded = new JsonExtensionActivationService(paths);

            Assert.False(reloaded.IsEnabled("ELBWALD.IMAGE.FORMATS.EXTENDED"));
            Assert.True(reloaded.SetEnabled("elbwald.image.formats.extended", true));
            Assert.True(reloaded.IsEnabled("elbwald.image.formats.extended"));
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
    public void Constructor_LegacyDisabledWebp_PreservesDisabledStateForExtendedImageFormats()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "elbwald-extension-activation-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var paths = new TestAppPaths(root);
            Directory.CreateDirectory(paths.ConfigDirectory);
            File.WriteAllText(
                Path.Combine(paths.ConfigDirectory, "extensions.json"),
                """
                {
                  "disabledExtensionIds": [
                    "elbwald.image.webp"
                  ]
                }
                """);

            var service = new JsonExtensionActivationService(paths);

            Assert.False(service.IsEnabled("elbwald.image.formats.extended"));
            Assert.True(service.IsEnabled("elbwald.image.webp"));
            Assert.True(service.SetEnabled("elbwald.image.formats.extended", true));

            var reloaded = new JsonExtensionActivationService(paths);
            Assert.True(reloaded.IsEnabled("elbwald.image.formats.extended"));
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
        public TestAppPaths(string root)
        {
            ApplicationBaseDirectory = root;
            ApplicationDataDirectory = root;
            ConfigDirectory = Path.Combine(root, "Config");
            DatabaseDirectory = Path.Combine(root, "Databases");
            CacheDirectory = Path.Combine(root, "Cache");
            DiagnosticsDirectory = Path.Combine(root, "Diagnostics");
            RecoveryDirectory = Path.Combine(root, "Recovery");
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
