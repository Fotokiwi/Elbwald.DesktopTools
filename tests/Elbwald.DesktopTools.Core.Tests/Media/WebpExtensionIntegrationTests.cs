using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class WebpExtensionIntegrationTests
{
    private static readonly Version HostVersion = new(0, 0, 1);

    [Fact]
    public async Task LoadWebpExtension_EnablesWebpDetectionWithoutToolModule()
    {
        using var environment = WebpModuleEnvironment.Create();
        var toolRegistry = new ModuleRegistry();
        var loader = new ModuleLoader(toolRegistry);
        var services = new ServiceCollection();

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                services));

        Assert.True(result.Success, result.Message);
        Assert.Equal("elbwald.image.webp", result.ModuleId);
        Assert.Empty(toolRegistry.Modules);

        AddMediaFormatHostServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var extensionRegistry = serviceProvider.GetRequiredService<IExtensionRegistry>();
        var formatRegistry = serviceProvider.GetRequiredService<IMediaFormatRegistry>();
        var detector = serviceProvider.GetRequiredService<IMediaTypeDetector>();
        var scanner = serviceProvider.GetRequiredService<IMediaScanner>();

        Assert.True(extensionRegistry.HasCapability(MediaCapabilityIds.FormatProvider));
        Assert.True(extensionRegistry.HasCapability("media.image.webp.detect"));

        var webpExtension = Assert.Single(
            extensionRegistry.FindExtensionsByCapability("media.image.webp.detect"));
        Assert.Equal("elbwald.image.webp", webpExtension.Id);

        var provider = Assert.Single(formatRegistry.FindByFilePath("holiday.WEBP"));
        Assert.Equal("image.webp", provider.FormatId);
        Assert.Equal(MediaFileType.Image, provider.MediaType);
        Assert.Equal(MediaFileType.Image, detector.Detect("holiday.webp"));

        var mediaPath = environment.CreateMediaFile("holiday.webp");
        var scanResult = await scanner.ScanAsync(mediaPath);
        var scannedFile = Assert.Single(scanResult.Files);

        Assert.Equal(MediaFileType.Image, scannedFile.MediaType);
        Assert.Equal(1, scanResult.ImageCount);
    }

    private static void AddMediaFormatHostServices(IServiceCollection services)
    {
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<CoreImageFormatProvider>();
        services.AddSingleton<IMediaFormatRegistry>(
            serviceProvider =>
                new MediaFormatRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [serviceProvider.GetRequiredService<CoreImageFormatProvider>()]));
        services.AddSingleton<IMediaTypeDetector, ExtensionMediaTypeDetector>();
        services.AddSingleton<IMediaScanner, FileSystemMediaScanner>();
    }

    private sealed class WebpModuleEnvironment : IDisposable
    {
        private readonly string _rootDirectory;

        private WebpModuleEnvironment(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            ModulesRoot = Path.Combine(rootDirectory, "Modules");
        }

        public string ModulesRoot { get; }

        public string CreateMediaFile(string fileName)
        {
            var mediaDirectory = Path.Combine(_rootDirectory, "Media");
            Directory.CreateDirectory(mediaDirectory);

            var filePath = Path.Combine(mediaDirectory, fileName);
            File.WriteAllText(filePath, "webp-test-data");
            return filePath;
        }

        public static WebpModuleEnvironment Create()
        {
            var sourceDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "TestModules",
                "Image.WebP");

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"WebP-Testmodul wurde nicht in den Test-Ausgabeordner kopiert: {sourceDirectory}");
            }

            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Elbwald.DesktopTools.Tests",
                Guid.NewGuid().ToString("N"));
            var environment = new WebpModuleEnvironment(rootDirectory);
            var destination = Path.Combine(environment.ModulesRoot, "Image.WebP");

            CopyDirectory(sourceDirectory, destination);

            return environment;
        }

        public void Dispose()
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return;
            }

            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Test cleanup must not hide the actual test result.
            }
            catch (UnauthorizedAccessException)
            {
                // Windows may briefly keep module assembly handles open.
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                File.Copy(
                    file,
                    Path.Combine(destinationDirectory, Path.GetFileName(file)),
                    overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            {
                CopyDirectory(
                    directory,
                    Path.Combine(destinationDirectory, Path.GetFileName(directory)));
            }
        }
    }
}
