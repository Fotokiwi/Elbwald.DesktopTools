using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Media.Exporting;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Core.Media.Metadata;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elbwald.DesktopTools.Core.Tests.Media;

public sealed class ExtendedImageFormatsIntegrationTests
{
    private static readonly Version HostVersion = new(0, 0, 1);

    // 2 x 3 pixel lossless WebP. Keeping the fixture inline makes the module
    // integration test independent from image libraries in the test project.
    private const string ValidWebpBase64 =
        "UklGRhwAAABXRUJQVlA4TA8AAAAvAYAAAAcQ/Y/+ByKi/wEA";

    [Fact]
    public async Task LoadExtendedImageFormats_EnablesDeclaredFormatsAndWebpCapabilitiesWithoutToolModule()
    {
        using var environment = ExtendedImageFormatsModuleEnvironment.Create();
        var activation = new TestActivationService();
        var toolRegistry = new ModuleRegistry(activation.IsEnabled);
        var loader = new ModuleLoader(toolRegistry);
        var services = new ServiceCollection();

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                services));

        Assert.True(result.Success, result.Message);
        Assert.Equal("elbwald.image.formats.extended", result.ModuleId);
        Assert.Empty(toolRegistry.Modules);

        AddHostServices(services, activation);

        using var serviceProvider = services.BuildServiceProvider();
        var extensionRegistry = serviceProvider.GetRequiredService<IExtensionRegistry>();
        var formatRegistry = serviceProvider.GetRequiredService<IMediaFormatRegistry>();
        var detector = serviceProvider.GetRequiredService<IMediaTypeDetector>();
        var metadataRegistry = serviceProvider.GetRequiredService<IImageMetadataRegistry>();
        var metadataReader = serviceProvider.GetRequiredService<IImageMetadataReader>();
        var exportRegistry = serviceProvider.GetRequiredService<IImageExportRegistry>();
        var scanner = serviceProvider.GetRequiredService<IMediaScanner>();

        Assert.True(extensionRegistry.HasCapability(MediaCapabilityIds.FormatProvider));
        Assert.True(extensionRegistry.HasCapability("media.image.webp.detect"));
        Assert.True(extensionRegistry.HasCapability("media.image.tiff.detect"));
        Assert.True(extensionRegistry.HasCapability("media.image.heic.detect"));
        Assert.True(extensionRegistry.HasCapability("media.image.heif.detect"));
        Assert.True(extensionRegistry.HasCapability("media.image.avif.detect"));
        Assert.True(extensionRegistry.HasCapability("media.image.jpeg2000.detect"));
        Assert.True(extensionRegistry.HasCapability(MetadataCapabilityIds.ImageReaderProvider));
        Assert.True(extensionRegistry.HasCapability("media.image.webp.metadata.read"));
        Assert.True(extensionRegistry.HasCapability(ExportCapabilityIds.ImageExporterProvider));
        Assert.True(extensionRegistry.HasCapability("media.image.webp.export"));

        var webpExtension = Assert.Single(
            extensionRegistry.FindExtensionsByCapability("media.image.webp.metadata.read"));
        Assert.Equal("elbwald.image.formats.extended", webpExtension.Id);
        Assert.Contains("ReadMedia", webpExtension.Permissions);

        var formatProvider = Assert.Single(formatRegistry.FindByFilePath("holiday.WEBP"));
        Assert.Equal("image.webp", formatProvider.FormatId);
        Assert.Equal(MediaFileType.Image, formatProvider.MediaType);
        Assert.Equal(MediaFileType.Image, detector.Detect("holiday.webp"));

        Assert.Equal("image.tiff", Assert.Single(formatRegistry.FindByFilePath("scan.TIFF")).FormatId);
        Assert.Equal("image.tiff", Assert.Single(formatRegistry.FindByFilePath("scan.tif")).FormatId);
        Assert.Equal("image.heif", Assert.Single(formatRegistry.FindByFilePath("phone.HEIC")).FormatId);
        Assert.Equal("image.heif", Assert.Single(formatRegistry.FindByFilePath("phone.heif")).FormatId);
        Assert.Equal("image.avif", Assert.Single(formatRegistry.FindByFilePath("modern.avif")).FormatId);
        Assert.Equal("image.jpeg2000", Assert.Single(formatRegistry.FindByFilePath("archive.jp2")).FormatId);
        Assert.Equal("image.jpeg2000", Assert.Single(formatRegistry.FindByFilePath("archive.j2k")).FormatId);

        var mediaPath = environment.CreateWebpFile("holiday.webp");
        var scanResult = await scanner.ScanAsync(mediaPath);
        var scannedFile = Assert.Single(scanResult.Files);

        Assert.Equal(MediaFileType.Image, scannedFile.MediaType);
        Assert.Equal(1, scanResult.ImageCount);

        var readers = metadataRegistry.FindReaders(scannedFile);
        var metadataProvider = Assert.Single(readers);
        Assert.Equal("webp.metadata-extractor", metadataProvider.ProviderId);

        var metadataResult = await metadataReader.ReadAsync(scannedFile);

        Assert.True(metadataResult.IsSuccessful, metadataResult.ErrorMessage);
        var metadata = Assert.IsType<ImageMetadata>(metadataResult.Metadata);
        Assert.Equal(2, metadata.PixelWidth);
        Assert.Equal(3, metadata.PixelHeight);

        var exporter = Assert.IsAssignableFrom<IImageExportProvider>(
            exportRegistry.FindByFormatId("image/webp"));
        Assert.Equal("webp.magicknet", exporter.ProviderId);
        Assert.Equal(".webp", exporter.FileExtension);
        Assert.True(exporter.SupportsResize);
        Assert.True(exporter.SupportsQuality);

        var exportRequest = new ImageExportRequest(
            mediaPath,
            MediaFileType.Image,
            metadata.PixelWidth,
            metadata.PixelHeight,
            MaxEdge: null,
            Quality: 82);

        Assert.True(exporter.CheckSupport(exportRequest).IsSupported);

        await using var encoded = new MemoryStream();
        await exporter.ExportAsync(exportRequest, encoded);

        var exportedBytes = encoded.ToArray();
        Assert.True(exportedBytes.Length > 20);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(exportedBytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(exportedBytes, 8, 4));

        var exportedPath = environment.CreateFile("roundtrip.webp", exportedBytes);
        var exportedFile = CreateImageMediaFile(exportedPath);
        var exportedMetadata = await metadataReader.ReadAsync(exportedFile);

        Assert.True(exportedMetadata.IsSuccessful, exportedMetadata.ErrorMessage);
        var roundtripMetadata = Assert.IsType<ImageMetadata>(exportedMetadata.Metadata);
        Assert.Equal(2, roundtripMetadata.PixelWidth);
        Assert.Equal(3, roundtripMetadata.PixelHeight);
    }

    [Fact]
    public async Task ActivationToggle_RemovesAndRestoresExtendedFormatsAndWebpCapabilitiesImmediately()
    {
        using var environment = ExtendedImageFormatsModuleEnvironment.Create();
        var activation = new TestActivationService();
        var toolRegistry = new ModuleRegistry(activation.IsEnabled);
        var loader = new ModuleLoader(toolRegistry);
        var services = new ServiceCollection();

        var result = Assert.Single(
            loader.LoadModules(
                environment.ModulesRoot,
                HostVersion,
                services));

        Assert.True(result.Success, result.Message);
        AddHostServices(services, activation);

        using var serviceProvider = services.BuildServiceProvider();
        var extensionRegistry = serviceProvider.GetRequiredService<IExtensionRegistry>();
        var formatRegistry = serviceProvider.GetRequiredService<IMediaFormatRegistry>();
        var metadataRegistry = serviceProvider.GetRequiredService<IImageMetadataRegistry>();
        var metadataReader = serviceProvider.GetRequiredService<IImageMetadataReader>();
        var exportRegistry = serviceProvider.GetRequiredService<IImageExportRegistry>();

        var mediaPath = environment.CreateWebpFile("toggle.webp");
        var file = CreateImageMediaFile(mediaPath);

        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType(mediaPath));
        Assert.Single(metadataRegistry.FindReaders(file));
        Assert.NotNull(exportRegistry.FindByFormatId("image/webp"));
        Assert.True((await metadataReader.ReadAsync(file)).IsSuccessful);
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.tiff"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.heic"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.avif"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.jp2"));

        Assert.True(activation.SetEnabled("elbwald.image.formats.extended", false));

        Assert.False(extensionRegistry.HasCapability("media.image.webp.detect"));
        Assert.False(extensionRegistry.HasCapability("media.image.webp.metadata.read"));
        Assert.False(extensionRegistry.HasCapability("media.image.webp.export"));
        Assert.Equal(MediaFileType.Unknown, formatRegistry.DetectMediaType(mediaPath));
        Assert.Equal(MediaFileType.Unknown, formatRegistry.DetectMediaType("sample.tiff"));
        Assert.Equal(MediaFileType.Unknown, formatRegistry.DetectMediaType("sample.heic"));
        Assert.Equal(MediaFileType.Unknown, formatRegistry.DetectMediaType("sample.avif"));
        Assert.Equal(MediaFileType.Unknown, formatRegistry.DetectMediaType("sample.jp2"));
        Assert.Empty(metadataRegistry.FindReaders(file));
        Assert.Null(exportRegistry.FindByFormatId("image/webp"));

        var disabledResult = await metadataReader.ReadAsync(file);
        Assert.Equal(ImageMetadataReadState.UnsupportedFormat, disabledResult.State);

        Assert.True(activation.SetEnabled("elbwald.image.formats.extended", true));

        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType(mediaPath));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.tiff"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.heic"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.avif"));
        Assert.Equal(MediaFileType.Image, formatRegistry.DetectMediaType("sample.jp2"));
        Assert.Single(metadataRegistry.FindReaders(file));
        Assert.NotNull(exportRegistry.FindByFormatId("image/webp"));
        Assert.True((await metadataReader.ReadAsync(file)).IsSuccessful);
    }

    private static void AddHostServices(
        IServiceCollection services,
        IExtensionActivationService activation)
    {
        services.AddSingleton<IExtensionActivationService>(activation);
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<CoreImageFormatProvider>();
        services.AddSingleton<IMediaFormatRegistry>(
            serviceProvider =>
                new MediaFormatRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [serviceProvider.GetRequiredService<CoreImageFormatProvider>()]));
        services.AddSingleton<IMediaTypeDetector, ExtensionMediaTypeDetector>();
        services.AddSingleton<IMediaScanner, FileSystemMediaScanner>();
        services.AddSingleton<IImageMetadataRegistry>(
            serviceProvider =>
                new ImageMetadataRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    []));
        services.AddSingleton<RegistryImageMetadataReader>();
        services.AddSingleton<IImageMetadataReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<RegistryImageMetadataReader>());
        services.AddSingleton<IImageExportRegistry>(
            serviceProvider =>
                new ImageExportRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    []));
    }

    private static MediaFile CreateImageMediaFile(string path)
    {
        var info = new FileInfo(path);

        return new MediaFile(
            info.FullName,
            info.Name,
            info.Extension.ToLowerInvariant(),
            info.Length,
            new DateTimeOffset(info.CreationTimeUtc),
            new DateTimeOffset(info.LastWriteTimeUtc),
            MediaFileType.Image,
            IsSymbolicLink: false);
    }

    private sealed class TestActivationService : IExtensionActivationService
    {
        private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<ExtensionActivationChangedEventArgs>? ActivationChanged;

        public bool IsEnabled(string extensionId)
        {
            return !_disabled.Contains(extensionId);
        }

        public bool SetEnabled(string extensionId, bool enabled)
        {
            var changed = enabled
                ? _disabled.Remove(extensionId)
                : _disabled.Add(extensionId);

            if (changed)
            {
                ActivationChanged?.Invoke(
                    this,
                    new ExtensionActivationChangedEventArgs(extensionId, enabled));
            }

            return changed;
        }
    }

    private sealed class ExtendedImageFormatsModuleEnvironment : IDisposable
    {
        private readonly string _rootDirectory;

        private ExtendedImageFormatsModuleEnvironment(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            ModulesRoot = Path.Combine(rootDirectory, "Modules");
        }

        public string ModulesRoot { get; }

        public string CreateWebpFile(string fileName)
        {
            return CreateFile(
                fileName,
                Convert.FromBase64String(ValidWebpBase64));
        }

        public string CreateFile(string fileName, byte[] data)
        {
            var mediaDirectory = Path.Combine(_rootDirectory, "Media");
            Directory.CreateDirectory(mediaDirectory);

            var filePath = Path.Combine(mediaDirectory, fileName);
            File.WriteAllBytes(filePath, data);
            return filePath;
        }

        public static ExtendedImageFormatsModuleEnvironment Create()
        {
            var sourceDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "TestModules",
                "Image.Formats.Extended");

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Extended-Image-Formats-Testmodul wurde nicht in den Test-Ausgabeordner kopiert: {sourceDirectory}");
            }

            var rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Elbwald.DesktopTools.Tests",
                Guid.NewGuid().ToString("N"));
            var environment = new ExtendedImageFormatsModuleEnvironment(rootDirectory);
            var destination = Path.Combine(environment.ModulesRoot, "Image.Formats.Extended");

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
