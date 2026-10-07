using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.App.ViewModels;
using Elbwald.DesktopTools.App.Views;
using Elbwald.DesktopTools.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.App;

public partial class App : Application
{
    private static readonly Version HostVersion = new(0, 0, 1);

    private ServiceProvider? _serviceProvider;

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

        LoadModules(_serviceProvider);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = _serviceProvider.GetRequiredService<MainWindow>();

            window.DataContext =
                _serviceProvider.GetRequiredService<MainWindowViewModel>();

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
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
}
