using Elbwald.DesktopTools.Contracts.Diagnostics;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Media.Naming;
using Elbwald.DesktopTools.Contracts.OperationHistory;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Companions;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Contracts.Settings;
using Elbwald.DesktopTools.Core.Diagnostics;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.LibraryHealth;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Media.Importing;
using Elbwald.DesktopTools.Core.Media.Formats;
using Elbwald.DesktopTools.Core.Media.Exporting;
using Elbwald.DesktopTools.Core.Media.Metadata;
using Elbwald.DesktopTools.Core.Media.Thumbnailing;
using Elbwald.DesktopTools.Core.Media.MetadataEditing;
using Elbwald.DesktopTools.Core.Media.MetadataExporting;
using Elbwald.DesktopTools.Core.MediaIndex;
using Elbwald.DesktopTools.Core.Media.Naming;
using Elbwald.DesktopTools.Core.OperationHistory;
using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Navigation;
using Elbwald.DesktopTools.Core.Paths;
using Elbwald.DesktopTools.Core.Projects;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Navigation;

namespace Elbwald.DesktopTools.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopTools(
        this IServiceCollection services,
        IModuleRegistry? moduleRegistry = null,
        IModuleLoader? moduleLoader = null,
        IModuleLoadReport? moduleLoadReport = null,
        IAppPaths? appPaths = null,
        IExtensionActivationService? extensionActivationService = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        appPaths ??= new AppPaths();
        extensionActivationService ??= new JsonExtensionActivationService(appPaths);
        moduleRegistry ??= new ModuleRegistry(extensionActivationService.IsEnabled);
        moduleLoader ??= new ModuleLoader(moduleRegistry);
        moduleLoadReport ??= new ModuleLoadReport();

        // Host registrations are added after extension registration so critical
        // platform services remain authoritative for single-service resolution.
        services.AddSingleton<IModuleRegistry>(moduleRegistry);
        services.AddSingleton<IModuleLoader>(moduleLoader);
        services.AddSingleton<IModuleLoadReport>(moduleLoadReport);
        services.AddSingleton<IExtensionActivationService>(extensionActivationService);
        services.AddSingleton<IExtensionRegistry, ExtensionRegistry>();
        services.AddSingleton<INavigationContributionRegistry, NavigationContributionRegistry>();
        services.AddSingleton<ISettingsContributionRegistry, SettingsContributionRegistry>();
        services.AddSingleton<ISettingsContributionContentFactory, SettingsContributionContentFactory>();

        services.AddSingleton<IAppPaths>(appPaths);
        services.AddSingleton<WindowPlacementStore>();

        services.AddSingleton<IStorageVolumeProvider>(
            _ =>
            {
                if (OperatingSystem.IsLinux())
                {
                    return new LinuxStorageVolumeProvider();
                }

                if (OperatingSystem.IsWindows())
                {
                    return new WindowsStorageVolumeProvider();
                }

                return new UnsupportedStorageVolumeProvider();
            });

        services.AddSingleton<IStorageLocationResolver, StorageLocationResolver>();
        services.AddSingleton<IStorageSettingsStore, JsonStorageSettingsStore>();

        services.AddSingleton<CoreImageFormatProvider>();
        services.AddSingleton<IMediaFormatRegistry>(
            serviceProvider =>
                new MediaFormatRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [serviceProvider.GetRequiredService<CoreImageFormatProvider>()]));
        services.AddSingleton<IMediaTypeDetector, ExtensionMediaTypeDetector>();
        services.AddSingleton<IMediaScanner, FileSystemMediaScanner>();

        services.AddSingleton<AvaloniaJpegImageExportProvider>();
        services.AddSingleton<AvaloniaPngImageExportProvider>();
        services.AddSingleton<IImageExportRegistry>(
            serviceProvider =>
                new ImageExportRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [
                        serviceProvider.GetRequiredService<AvaloniaJpegImageExportProvider>(),
                        serviceProvider.GetRequiredService<AvaloniaPngImageExportProvider>()
                    ]));

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new MediaAnalysisCacheOptions
                {
                    DatabasePath =
                        Path.Combine(
                            appPaths.CacheDirectory,
                            "media-analysis.db"),
                    AnalysisVersion = 2
                };
            });

        services.AddSingleton<IMediaAnalysisCache, SqliteMediaAnalysisCache>();
        services.AddSingleton<MetadataExtractorImageMetadataReader>();
        services.AddSingleton(
            serviceProvider =>
                new CachedImageMetadataReader(
                    serviceProvider.GetRequiredService<MetadataExtractorImageMetadataReader>(),
                    serviceProvider.GetRequiredService<IMediaAnalysisCache>()));
        services.AddSingleton(
            serviceProvider =>
                new CoreImageMetadataProvider(
                    serviceProvider.GetRequiredService<CachedImageMetadataReader>(),
                    serviceProvider.GetRequiredService<CoreImageFormatProvider>()));
        services.AddSingleton<IImageMetadataRegistry>(
            serviceProvider =>
                new ImageMetadataRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [serviceProvider.GetRequiredService<CoreImageMetadataProvider>()]));
        services.AddSingleton<RegistryImageMetadataReader>();
        services.AddSingleton<IImageMetadataReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<RegistryImageMetadataReader>());

        services.AddSingleton<AvaloniaBaseImageThumbnailProvider>();
        services.AddSingleton<IImageThumbnailRegistry>(
            serviceProvider =>
                new ImageThumbnailRegistry(
                    serviceProvider.GetRequiredService<IExtensionRegistry>(),
                    [serviceProvider.GetRequiredService<AvaloniaBaseImageThumbnailProvider>()]));

        services.AddSingleton<IMediaAnalyzer, MediaAnalyzerService>();
        services.AddSingleton<IMediaAnalysisHistoryStore, JsonMediaAnalysisHistoryStore>();
        services.AddSingleton<IMetadataSidecarService, XmpMetadataSidecarService>();
        services.AddSingleton<IMetadataExportService, MetadataExportService>();
        services.AddSingleton<IMediaImportPlanner, MediaImportPlanner>();
        services.AddSingleton<IMediaImportExecutor, MediaImportExecutor>();
        services.AddSingleton<IMediaImportHistoryStore, JsonMediaImportHistoryStore>();
        services.AddSingleton<IMediaImportCleanupPlanner, MediaImportCleanupPlanner>();
        services.AddSingleton<IMediaImportCleanupExecutor, MediaImportCleanupExecutor>();
        services.AddSingleton<ILibraryHealthService, LibraryHealthService>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new MediaIndexOptions
                {
                    DatabasePath =
                        Path.Combine(
                            appPaths.DatabaseDirectory,
                            "media-index.db")
                };
            });

        services.AddSingleton<IMediaIndexService, MediaIndexService>();
        services.AddSingleton<IMediaNamingTemplateEngine, MediaNamingTemplateEngine>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();
                return new OperationHistoryOptions
                {
                    DatabasePath = Path.Combine(appPaths.DatabaseDirectory, "operation-history.db")
                };
            });
        services.AddSingleton<IOperationHistoryStore, SqliteOperationHistoryStore>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new ProjectOptions
                {
                    DatabasePath =
                        Path.Combine(
                            appPaths.DatabaseDirectory,
                            "projects.db")
                };
            });

        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IMediaDateResolver, MediaDateResolver>();
        services.AddSingleton<IMediaDateContextAnalyzer, MediaDateContextAnalyzer>();
        services.AddSingleton<IMediaCompanionPlanner, MediaCompanionPlanner>();
        services.AddSingleton<IMediaSourcePicker, AvaloniaMediaSourcePicker>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return MediaThumbnailOptions.Load(
                    Path.Combine(
                        appPaths.CacheDirectory,
                        "Thumbnails"),
                    Path.Combine(
                        appPaths.ConfigDirectory,
                        "thumbnail-settings.json"));
            });

        services.AddSingleton<IRawPreviewExtractor, TiffEmbeddedJpegPreviewExtractor>();
        services.AddSingleton<IMediaThumbnailService, AvaloniaMediaThumbnailService>();

        services.AddSingleton(FileOperationSafetyOptions.Default);
        services.AddSingleton<IFileOperationSafetyChecker, FileOperationSafetyChecker>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new DiagnosticEventStoreOptions
                {
                    DatabasePath =
                        Path.Combine(
                            appPaths.DiagnosticsDirectory,
                            "events.db")
                };
            });

        services.AddSingleton<IDiagnosticEventStore, SqliteDiagnosticEventStore>();
        services.AddSingleton<IStorageHealthService, StorageHealthService>();
        services.AddSingleton<IFileOperationPlanner, FileOperationPlanner>();
        services.AddSingleton<IMediaSortPlanner, MediaSortPlanner>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return RecoveryOptions.CreateDefault(
                    Path.Combine(
                        appPaths.RecoveryDirectory,
                        "files"));
            });

        services.AddSingleton<IFileOperationProcessLock>(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new FileSystemOperationProcessLock(
                    Path.Combine(
                        appPaths.RecoveryDirectory,
                        "desktop-tools.process.lock"));
            });

        services.AddSingleton<IMediaSortExecutionPlanner, MediaSortExecutionPlanner>();
        services.AddSingleton<IMediaSortExecutionExecutor, MediaSortExecutionExecutor>();
        services.AddSingleton<MemoryRecoveryStore>();
        services.AddSingleton<FileRecoveryStore>();

        services.AddSingleton<IPersistentRecoveryStore>(
            serviceProvider =>
                serviceProvider.GetRequiredService<FileRecoveryStore>());

        services.AddSingleton<IRecoveryStore, HybridRecoveryStore>();

        services.AddSingleton(
            serviceProvider =>
            {
                var appPaths = serviceProvider.GetRequiredService<IAppPaths>();

                return new JsonLinesOperationJournal(
                    Path.Combine(
                        appPaths.RecoveryDirectory,
                        "journal",
                        "operations.jsonl"),
                    serviceProvider.GetRequiredService<IFileOperationProcessLock>());
            });

        services.AddSingleton<IOperationJournal>(
            serviceProvider =>
                serviceProvider.GetRequiredService<JsonLinesOperationJournal>());

        services.AddSingleton<IOperationJournalMaintenance>(
            serviceProvider =>
                serviceProvider.GetRequiredService<JsonLinesOperationJournal>());

        services.AddSingleton<IFileOperationRecoveryInspector, FileOperationRecoveryInspector>();
        services.AddSingleton<IFileOperationRecoveryCoordinator, FileOperationRecoveryCoordinator>();
        services.AddSingleton<IStartupRecoveryService, StartupRecoveryService>();
        services.AddSingleton<IFileOperationExecutor, FileOperationExecutor>();

        services.AddSingleton<MediaExportSelectionContext>();

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IApplicationNavigation>(
            serviceProvider => serviceProvider.GetRequiredService<INavigationService>());
        services.AddSingleton<INavigationContentFactory, NavigationContentFactory>();

        return services;
    }
}
