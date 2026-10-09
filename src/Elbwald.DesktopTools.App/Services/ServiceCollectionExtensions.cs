using Elbwald.DesktopTools.Contracts.Diagnostics;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.LibraryHealth;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Importing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Companions;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Media.Sorting.Execution;
using Elbwald.DesktopTools.Contracts.Paths;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Core.Diagnostics;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.LibraryHealth;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Media.Importing;
using Elbwald.DesktopTools.Core.MediaIndex;
using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Paths;
using Elbwald.DesktopTools.Core.Projects;
using Elbwald.DesktopTools.Core.Recovery;
using Elbwald.DesktopTools.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopTools(
        this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();

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

        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IModuleLoader, ModuleLoader>();
        services.AddSingleton<IModuleLoadReport, ModuleLoadReport>();

        services.AddSingleton<IMediaTypeDetector, ExtensionMediaTypeDetector>();
        services.AddSingleton<IMediaScanner, FileSystemMediaScanner>();

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

        services.AddSingleton<IImageMetadataReader>(
            serviceProvider =>
                new CachedImageMetadataReader(
                    serviceProvider.GetRequiredService<MetadataExtractorImageMetadataReader>(),
                    serviceProvider.GetRequiredService<IMediaAnalysisCache>()));

        services.AddSingleton<IMediaAnalyzer, MediaAnalyzerService>();
        services.AddSingleton<IMediaImportPlanner, MediaImportPlanner>();
        services.AddSingleton<IMediaImportExecutor, MediaImportExecutor>();
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

                return new MediaThumbnailOptions
                {
                    CacheDirectory =
                        Path.Combine(
                            appPaths.CacheDirectory,
                            "Thumbnails")
                };
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

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<INavigationContentFactory, NavigationContentFactory>();

        return services;
    }
}
