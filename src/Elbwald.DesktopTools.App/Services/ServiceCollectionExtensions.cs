using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Journaling;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.FileOperations;
using Elbwald.DesktopTools.Core.Journaling;
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

        services.AddSingleton(FileOperationSafetyOptions.Default);
        services.AddSingleton<IFileOperationSafetyChecker, FileOperationSafetyChecker>();
        services.AddSingleton<IFileOperationPlanner, FileOperationPlanner>();

        var recoveryRoot = GetRecoveryRoot();

        var recoveryOptions = RecoveryOptions.CreateDefault(
            Path.Combine(
                recoveryRoot,
                "files"));

        services.AddSingleton(recoveryOptions);
        services.AddSingleton<MemoryRecoveryStore>();
        services.AddSingleton<FileRecoveryStore>();

        services.AddSingleton<IPersistentRecoveryStore>(
            serviceProvider =>
                serviceProvider.GetRequiredService<FileRecoveryStore>());

        services.AddSingleton<IRecoveryStore, HybridRecoveryStore>();

        services.AddSingleton(
            _ => new JsonLinesOperationJournal(
                Path.Combine(
                    recoveryRoot,
                    "journal",
                    "operations.jsonl")));

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
            "DesktopTools",
            "Recovery");
    }
}
