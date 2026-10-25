using Avalonia.Controls;
using Avalonia.Interactivity;
using Elbwald.DesktopTools.Image.Editor.Basic.Controls;
using Elbwald.DesktopTools.Image.Editor.Basic.ViewModels;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Views;

public partial class ImageEditorView : UserControl
{
    private bool _initialized;

    public ImageEditorView()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        var cropOverlay = this.FindControl<CropOverlayControl>("CropOverlay");
        if (cropOverlay is not null)
        {
            cropOverlay.CropRectangleChanged += OnCropRectangleChanged;
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_initialized || DataContext is not ImageEditorViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        if (viewModel.InitializeCommand.CanExecute(null))
        {
            viewModel.InitializeCommand.Execute(null);
        }
    }

    private void OnCropRectangleChanged(object? sender, CropRectangleChangedEventArgs e)
    {
        if (DataContext is not ImageEditorViewModel viewModel)
        {
            return;
        }

        viewModel.UpdateCropSelection(e.Left, e.Top, e.Width, e.Height);
    }
}
