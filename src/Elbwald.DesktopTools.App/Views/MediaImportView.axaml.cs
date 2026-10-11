using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.ViewModels;

namespace Elbwald.DesktopTools.App.Views;

public partial class MediaImportView : UserControl
{
    public MediaImportView()
    {
        AvaloniaXamlLoader.Load(this);

        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is MediaImportViewModel viewModel)
            {
                await viewModel.RefreshDefaultsAsync();
            }
        };
    }
}
