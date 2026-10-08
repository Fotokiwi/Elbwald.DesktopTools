using Avalonia.Controls;
using Elbwald.DesktopTools.App.ViewModels;
using Elbwald.DesktopTools.App.Views;
using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.App.Services;

public sealed class NavigationContentFactory : INavigationContentFactory
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationContentFactory(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    public object CreateHomePage()
    {
        var viewModel = ActivatorUtilities.CreateInstance<HomeViewModel>(_serviceProvider);
        var view = ActivatorUtilities.CreateInstance<HomeView>(_serviceProvider);

        view.DataContext = viewModel;

        return view;
    }

    public object CreateSectionPage(string sectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        if (string.Equals(
                sectionId,
                "settings",
                StringComparison.OrdinalIgnoreCase))
        {
            var settingsViewModel =
                ActivatorUtilities.CreateInstance<SettingsViewModel>(
                    _serviceProvider);

            var settingsView =
                ActivatorUtilities.CreateInstance<SettingsView>(
                    _serviceProvider);

            settingsView.DataContext =
                settingsViewModel;

            return settingsView;
        }

        var viewModel =
            ActivatorUtilities.CreateInstance<SectionHubViewModel>(
                _serviceProvider,
                sectionId);

        var view =
            ActivatorUtilities.CreateInstance<SectionHubView>(
                _serviceProvider);

        view.DataContext =
            viewModel;

        return view;
    }

    public object CreateAboutPage()
    {
        var viewModel =
            ActivatorUtilities.CreateInstance<AboutViewModel>(
                _serviceProvider);

        var view =
            ActivatorUtilities.CreateInstance<AboutView>(
                _serviceProvider);

        view.DataContext =
            viewModel;

        return view;
    }

    public object CreateModulePage(ToolModuleDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        try
        {
            var viewModel = ActivatorUtilities.CreateInstance(
                _serviceProvider,
                descriptor.ViewModelType);

            var viewObject = ActivatorUtilities.CreateInstance(
                _serviceProvider,
                descriptor.ViewType);

            if (viewObject is not Control view)
            {
                throw new InvalidOperationException(
                    $"Die View des Moduls '{descriptor.Id}' muss von Avalonia.Controls.Control erben.");
            }

            view.DataContext = viewModel;

            return view;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Modul '{descriptor.Id}' konnte nicht geöffnet werden: {exception}");

            return CreateModuleErrorPage(
                descriptor.Name,
                exception.GetBaseException().Message);
        }
    }

    private object CreateModuleErrorPage(
        string moduleName,
        string technicalMessage)
    {
        var viewModel = new NavigationErrorViewModel(
            moduleName,
            technicalMessage);

        var view = ActivatorUtilities.CreateInstance<NavigationErrorView>(
            _serviceProvider);

        view.DataContext = viewModel;

        return view;
    }
}
