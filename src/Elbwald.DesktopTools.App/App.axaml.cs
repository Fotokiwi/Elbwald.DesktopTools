using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.App.ViewModels;
using Elbwald.DesktopTools.App.Views;
using Elbwald.DesktopTools.Contracts.Recovery;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace Elbwald.DesktopTools.App;

public partial class App : Application
{
    private static readonly Version HostVersion =
        typeof(App).Assembly.GetName().Version
        ?? new Version(0, 1, 0, 0);

    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _startupRecoveryCancellation;
    private int _servicesDisposed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddDesktopTools();

        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var desktop =
            ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        if (desktop is not null)
        {
            // Solange es noch keinen expliziten Tray-/Hintergrundmodus gibt,
            // bedeutet das Schließen des Hauptfensters immer Prozessende.
            desktop.ShutdownMode =
                Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            desktop.Exit += OnDesktopExit;
        }

        LoadModules(_serviceProvider);

        if (desktop is not null)
        {
            var window =
                _serviceProvider.GetRequiredService<MainWindow>();

            window.DataContext =
                _serviceProvider.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow =
                window;
        }

        base.OnFrameworkInitializationCompleted();

        if (desktop is not null)
        {
            StartStartupRecoveryInBackground(
                _serviceProvider);
        }
    }

    private void OnDesktopExit(
        object? sender,
        ControlledApplicationLifetimeExitEventArgs args)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        CancelStartupRecovery();
        DisposeServices();
    }

    private void DisposeServices()
    {
        if (Interlocked.Exchange(
                ref _servicesDisposed,
                1) != 0)
        {
            return;
        }

        var serviceProvider =
            _serviceProvider;

        _serviceProvider = null;

        serviceProvider?.Dispose();
    }

    private static void LoadModules(IServiceProvider serviceProvider)
    {
        var loader = serviceProvider.GetRequiredService<IModuleLoader>();
        var report = serviceProvider.GetRequiredService<IModuleLoadReport>();

        var modulesPath = Path.Combine(
            AppContext.BaseDirectory,
            "Modules");

        var results = loader.LoadModules(
            modulesPath,
            HostVersion);

        report.Replace(results);

        foreach (var result in report.Failures)
        {
            Console.Error.WriteLine(
                $"Modul konnte nicht geladen werden: "
                + $"{result.ModuleId ?? result.ModuleDirectory} – {result.Message}");
        }
    }

    private void StartStartupRecoveryInBackground(
        IServiceProvider serviceProvider)
    {
        var recoveryService =
            serviceProvider.GetRequiredService<IStartupRecoveryService>();

        var cancellation =
            new CancellationTokenSource();

        _startupRecoveryCancellation =
            cancellation;

        _ = Task.Run(
                async () =>
                {
                    try
                    {
                        var snapshot =
                            await recoveryService
                                .ScanAsync(
                                    cancellation.Token)
                                .ConfigureAwait(false);

                        LogStartupRecoverySnapshot(
                            snapshot);
                    }
                    catch (OperationCanceledException)
                        when (cancellation.IsCancellationRequested)
                    {
                        // Normaler Shutdown während der Startup-Prüfung.
                    }
                    catch (Exception exception)
                    {
                        // Startup darf die UI niemals wieder blockieren oder beenden.
                        // Dateioperationen bleiben trotzdem fail-closed, weil deren
                        // Recovery-Prüfung unabhängig davon erneut ausgeführt wird.
                        Console.Error.WriteLine(
                            "Recovery-Prüfung im Hintergrund fehlgeschlagen: "
                            + exception.Message);
                    }
                    finally
                    {
                        Interlocked.CompareExchange(
                            ref _startupRecoveryCancellation,
                            null,
                            cancellation);

                        cancellation.Dispose();
                    }
                },
                cancellation.Token);
    }

    private void CancelStartupRecovery()
    {
        var cancellation =
            Interlocked.Exchange(
                ref _startupRecoveryCancellation,
                null);

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown bleibt idempotent.
        }

    }

    private static void LogStartupRecoverySnapshot(
        StartupRecoverySnapshot snapshot)
    {
        if (snapshot.State == StartupRecoveryState.AttentionRequired)
        {
            Console.Error.WriteLine(
                "Recovery-Prüfung: "
                + $"{snapshot.Candidates.Count} unvollständige Transaktion(en), "
                + $"{snapshot.CorruptJournalLineCount} beschädigte Journalzeile(n).");
        }
        else if (snapshot.State == StartupRecoveryState.ScanFailed)
        {
            Console.Error.WriteLine(
                "Recovery-Prüfung fehlgeschlagen: "
                + $"{snapshot.ErrorMessage ?? "Unbekannter Fehler"}");
        }
    }

}
