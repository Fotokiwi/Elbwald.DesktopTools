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

            Assert.True(service.IsEnabled("elbwald.image.webp"));
            Assert.True(service.SetEnabled("elbwald.image.webp", false));
            Assert.False(service.IsEnabled("elbwald.image.webp"));

            var reloaded = new JsonExtensionActivationService(paths);

            Assert.False(reloaded.IsEnabled("ELBWALD.IMAGE.WEBP"));
            Assert.True(reloaded.SetEnabled("elbwald.image.webp", true));
            Assert.True(reloaded.IsEnabled("elbwald.image.webp"));
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
