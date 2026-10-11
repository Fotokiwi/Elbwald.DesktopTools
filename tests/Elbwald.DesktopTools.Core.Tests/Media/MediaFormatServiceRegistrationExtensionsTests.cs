using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class MediaFormatServiceRegistrationExtensionsTests
{
    [Fact]
    public void AddMediaFormatProvider_RegistersWellKnownCapabilityMetadata()
    {
        var services = new ServiceCollection();

        services.AddMediaFormatProvider<TestFormatProvider>("elbwald.test.format");

        var registration = Assert.Single(
            services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(ExtensionCapabilityProviderRegistration))
                .Select(descriptor =>
                    Assert.IsType<ExtensionCapabilityProviderRegistration>(
                        descriptor.ImplementationInstance)));

        Assert.Equal("elbwald.test.format", registration.ExtensionId);
        Assert.Equal(MediaCapabilityIds.FormatProvider, registration.CapabilityId);
        Assert.Equal(typeof(IMediaFormatProvider), registration.ContractType);
        Assert.Equal(typeof(TestFormatProvider), registration.ImplementationType);
    }

    private sealed class TestFormatProvider : IMediaFormatProvider
    {
        public string FormatId => "image.test";

        public MediaFileType MediaType => MediaFileType.Image;

        public IReadOnlyCollection<string> FileExtensions => [".test"];
    }
}
