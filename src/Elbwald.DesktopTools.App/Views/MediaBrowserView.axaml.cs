using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.ViewModels;

namespace Elbwald.DesktopTools.App.Views;

public partial class MediaBrowserView : UserControl
{
    public MediaBrowserView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MediaBrowserViewModel viewModel
            || sender is not ListBox listBox)
        {
            return;
        }

        var selected = listBox.SelectedItems?
            .OfType<MediaBrowserItemViewModel>()
            .ToArray()
            ?? Array.Empty<MediaBrowserItemViewModel>();

        viewModel.SetSelection(selected);
    }

    private void OnMediaDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MediaBrowserViewModel viewModel
            || sender is not Control control
            || control.DataContext is not MediaBrowserItemViewModel item
            || !item.IsImage)
        {
            return;
        }

        var selected = GetCurrentSelection(viewModel);

        OpenViewer(viewModel, item, selected);
        e.Handled = true;
    }

    private void OnOpenSelectedViewer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaBrowserViewModel viewModel
            || viewModel.SelectedMedia is not { IsImage: true } item)
        {
            return;
        }

        var selected = GetCurrentSelection(viewModel);

        OpenViewer(viewModel, item, selected);
    }

    private IReadOnlyList<MediaBrowserItemViewModel> GetCurrentSelection(MediaBrowserViewModel viewModel)
    {
        var controlName = viewModel.IsGridView ? "GridList" : "TableList";
        var list = this.FindControl<ListBox>(controlName);
        return list?.SelectedItems?
            .OfType<MediaBrowserItemViewModel>()
            .ToArray()
            ?? Array.Empty<MediaBrowserItemViewModel>();
    }

    private void OpenViewer(
        MediaBrowserViewModel browserViewModel,
        MediaBrowserItemViewModel item,
        IReadOnlyList<MediaBrowserItemViewModel> selected)
    {
        var viewer = new MediaViewerWindow
        {
            DataContext = browserViewModel.CreateViewer(item, selected)
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            viewer.Show(owner);
        }
        else
        {
            viewer.Show();
        }
    }
}
