using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Elbwald.DesktopTools.App.ViewModels;

namespace Elbwald.DesktopTools.App.Views;

public partial class MediaViewerWindow : Window
{
    private WindowState _stateBeforeFullscreen = WindowState.Normal;

    public MediaViewerWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private MediaViewerViewModel? ViewModel => DataContext as MediaViewerViewModel;

    private async void OnPreviousClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.PreviousAsync();
    }

    private async void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.NextAsync();
    }

    private void OnPresentationClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (!ViewModel.IsPresentationRunning)
        {
            ViewModel.StartPresentation();
            EnterFullscreen();
        }
        else
        {
            ViewModel.StopPresentation();
        }
    }

    private void OnFullscreenClick(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null) return;

        if (e.Source is TextBox or ComboBox)
        {
            return;
        }

        var rating = e.Key switch
        {
            Key.D0 or Key.NumPad0 => 0,
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            _ => -1
        };

        if (rating >= 0)
        {
            await ViewModel.ApplyRatingShortcutAsync(rating);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                await ViewModel.PreviousAsync();
                e.Handled = true;
                break;
            case Key.Right:
                await ViewModel.NextAsync();
                e.Handled = true;
                break;
            case Key.Space:
                if (ViewModel.IsPresentationRunning)
                {
                    ViewModel.StopPresentation();
                }
                else
                {
                    ViewModel.StartPresentation();
                    EnterFullscreen();
                }
                e.Handled = true;
                break;
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
                if (WindowState == WindowState.FullScreen)
                {
                    ExitFullscreen();
                }
                else
                {
                    Close();
                }
                e.Handled = true;
                break;
        }
    }

    private void OnImageAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.IsPresentationRunning == true)
        {
            ViewModel.StopPresentation();
        }
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen) ExitFullscreen();
        else EnterFullscreen();
    }

    private void EnterFullscreen()
    {
        if (WindowState != WindowState.FullScreen)
        {
            _stateBeforeFullscreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
    }

    private void ExitFullscreen()
    {
        WindowState = _stateBeforeFullscreen == WindowState.FullScreen
            ? WindowState.Normal
            : _stateBeforeFullscreen;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ViewModel?.Dispose();
    }
}
