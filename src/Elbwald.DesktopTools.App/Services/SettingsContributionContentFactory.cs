using Avalonia.Controls;
using Elbwald.DesktopTools.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.App.Services;

public sealed class SettingsContributionContentFactory : ISettingsContributionContentFactory
{
    private readonly IServiceProvider _serviceProvider;

    public SettingsContributionContentFactory(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    public object? CreateContent(SettingsContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);

        if (contribution.ViewModelType is null || contribution.ViewType is null)
        {
            return null;
        }

        try
        {
            var viewModel = ActivatorUtilities.CreateInstance(
                _serviceProvider,
                contribution.ViewModelType);

            var viewObject = ActivatorUtilities.CreateInstance(
                _serviceProvider,
                contribution.ViewType);

            if (viewObject is not Control view)
            {
                throw new InvalidOperationException(
                    $"Settings-View '{contribution.ViewType.FullName}' muss von Avalonia.Controls.Control erben.");
            }

            view.DataContext = viewModel;
            return view;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Settings-Contribution '{contribution.Id}' konnte nicht geöffnet werden: {exception}");

            return new TextBlock
            {
                Text = "Moduleinstellungen konnten nicht geladen werden: "
                    + exception.GetBaseException().Message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
        }
    }
}
