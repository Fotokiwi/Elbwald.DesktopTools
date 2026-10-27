using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Elbwald.DesktopTools.Audio.Editor.Basic.Views;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Owns the single non-modal effect guide window of the audio studio.
/// </summary>
public sealed class EffectsHelpWindowService
{
    private EffectsHelpWindow? _window;

    public void Show()
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        var window = new EffectsHelpWindow();
        _window = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };

        var owner =
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
