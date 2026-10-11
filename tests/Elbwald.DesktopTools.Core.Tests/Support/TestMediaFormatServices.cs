using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Core.Modules;

namespace Elbwald.DesktopTools.Core.Tests.Support;

internal static class TestMediaFormatServices
{
    public static ExtensionMediaTypeDetector CreateMediaTypeDetector()
    {
        var extensionRegistry = new ExtensionRegistry(
            new EmptyServiceProvider(),
            [],
            []);
        var formatRegistry = new MediaFormatRegistry(
            extensionRegistry,
            [new CoreImageFormatProvider()]);

        return new ExtensionMediaTypeDetector(formatRegistry);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
