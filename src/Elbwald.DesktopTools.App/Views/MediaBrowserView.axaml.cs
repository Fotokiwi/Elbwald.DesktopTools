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


    private void OnBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MediaBrowserViewModel viewModel)
        {
            return;
        }

        if (e.Source is TextBox or ComboBox)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            SelectAllLoaded(viewModel);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.E)
        {
            if (viewModel.ExportSelectionCommand.CanExecute(null))
            {
                viewModel.ExportSelectionCommand.Execute(null);
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            ClearCurrentSelection(viewModel);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var selected = GetCurrentSelection(viewModel);
            if (selected.Count == 1 && selected[0].IsImage)
            {
                OpenViewer(viewModel, selected[0], selected);
                e.Handled = true;
            }
            return;
        }

        var rating = e.Key switch
        {
            Key.D0 or Key.NumPad0 => "0",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            _ => null
        };

        if (rating is not null && viewModel.SetRatingCommand.CanExecute(rating))
        {
            viewModel.SetRatingCommand.Execute(rating);
            e.Handled = true;
        }
    }

    private void SelectAllLoaded(MediaBrowserViewModel viewModel)
    {
        var list = GetCurrentList(viewModel);
        if (list?.SelectedItems is null) return;

        list.SelectedItems.Clear();
        foreach (var item in viewModel.Items)
        {
            list.SelectedItems.Add(item);
        }
    }

    private void ClearCurrentSelection(MediaBrowserViewModel viewModel)
    {
        var list = GetCurrentList(viewModel);
        list?.SelectedItems?.Clear();
    }

    private ListBox? GetCurrentList(MediaBrowserViewModel viewModel)
    {
        var controlName = viewModel.IsGridView ? "GridList" : "TableList";
        return this.FindControl<ListBox>(controlName);
    }

    private IReadOnlyList<MediaBrowserItemViewModel> GetCurrentSelection(MediaBrowserViewModel viewModel)
    {
        var list = GetCurrentList(viewModel);
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
