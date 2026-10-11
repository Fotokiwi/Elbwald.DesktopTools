using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.ViewModels;

namespace Elbwald.DesktopTools.App.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        AvaloniaXamlLoader.Load(this);

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is HomeViewModel viewModel)
            {
                await viewModel.RefreshDashboardAsync();
            }
        };
    }
}
