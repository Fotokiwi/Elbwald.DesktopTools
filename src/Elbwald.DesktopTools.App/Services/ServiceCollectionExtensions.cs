using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
using Elbwald.DesktopTools.Core.Media;
using Elbwald.DesktopTools.Core.Modules;
using Elbwald.DesktopTools.Core.Recovery;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.App.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDesktopTools(
        this IServiceCollection services)
    {
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IModuleLoader, ModuleLoader>();
        services.AddSingleton<IModuleLoadReport, ModuleLoadReport>();

        services.AddSingleton<IMediaTypeDetector, ExtensionMediaTypeDetector>();
        services.AddSingleton<IMediaScanner, FileSystemMediaScanner>();

        var cacheRoot = GetCacheRoot();

        services.AddSingleton(
            new MediaAnalysisCacheOptions
            {
                DatabasePath =
                    Path.Combine(
                        cacheRoot,
                        "media-analysis.db"),
                AnalysisVersion = 2
            });

        services.AddSingleton<IMediaAnalysisCache, SqliteMediaAnalysisCache>();
        services.AddSingleton<MetadataExtractorImageMetadataReader>();

        services.AddSingleton<IImageMetadataReader>(
            serviceProvider =>
                new CachedImageMetadataReader(
                    serviceProvider.GetRequiredService<MetadataExtractorImageMetadataReader>(),
                    serviceProvider.GetRequiredService<IMediaAnalysisCache>()));

        services.AddSingleton<IMediaAnalyzer, MediaAnalyzerService>();
        services.AddSingleton<IMediaDateResolver, MediaDateResolver>();
        services.AddSingleton<IMediaSourcePicker, AvaloniaMediaSourcePicker>();

        services.AddSingleton(
            new MediaThumbnailOptions
            {
                CacheDirectory =
                    Path.Combine(
                        cacheRoot,
                        "Thumbnails")
            });

        services.AddSingleton<IRawPreviewExtractor, TiffEmbeddedJpegPreviewExtractor>();
        services.AddSingleton<IMediaThumbnailService, AvaloniaMediaThumbnailService>();

        services.AddSingleton(FileOperationSafetyOptions.Default);
        services.AddSingleton<IFileOperationSafetyChecker, FileOperationSafetyChecker>();
        services.AddSingleton<IFileOperationPlanner, FileOperationPlanner>();
        services.AddSingleton<IMediaSortPlanner, MediaSortPlanner>();

        var recoveryRoot = GetRecoveryRoot();

        var recoveryOptions = RecoveryOptions.CreateDefault(
            Path.Combine(
                recoveryRoot,
                "files"));

        services.AddSingleton<IFileOperationProcessLock>(
            _ => new FileSystemOperationProcessLock(
                Path.Combine(
                    recoveryRoot,
                    "desktop-tools.process.lock")));

        services.AddSingleton(recoveryOptions);
        services.AddSingleton<MemoryRecoveryStore>();
        services.AddSingleton<FileRecoveryStore>();

        services.AddSingleton<IPersistentRecoveryStore>(
            serviceProvider =>
                serviceProvider.GetRequiredService<FileRecoveryStore>());

        services.AddSingleton<IRecoveryStore, HybridRecoveryStore>();

        services.AddSingleton(
            serviceProvider => new JsonLinesOperationJournal(
                Path.Combine(
                    recoveryRoot,
                    "journal",
                    "operations.jsonl"),
                serviceProvider.GetRequiredService<IFileOperationProcessLock>()));

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

    private static string GetRecoveryRoot()
    {
        return Path.Combine(
            GetApplicationDataRoot(),
            "Recovery");
    }

    private static string GetCacheRoot()
    {
        return Path.Combine(
            GetApplicationDataRoot(),
            "Cache");
    }

    private static string GetApplicationDataRoot()
    {
        var localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localData))
        {
            var userProfile = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);

            localData = Path.Combine(
                userProfile,
                ".local",
                "share");
        }

        return Path.Combine(
            localData,
            "ElbwaldDigital",
            "DesktopTools");
    }
}
