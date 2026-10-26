using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;
using Elbwald.DesktopTools.Audio.Editor.Basic.Views;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Owns the single detachable EQ tool window for the currently active audio workspace.
/// </summary>
public sealed class EqAnalysisWindowService
{
    private readonly EqAnalysisWindowPlacementStore _placementStore;
    private readonly RestorationAnalysisService _restorationAnalysisService;
    private EqAnalysisWindow? _window;
    private AudioEditorViewModel? _editor;

    public EqAnalysisWindowService(
        EqAnalysisWindowPlacementStore placementStore,
        RestorationAnalysisService restorationAnalysisService)
    {
        ArgumentNullException.ThrowIfNull(placementStore);
        ArgumentNullException.ThrowIfNull(restorationAnalysisService);
        _placementStore = placementStore;
        _restorationAnalysisService = restorationAnalysisService;
    }

    public void Show(AudioEditorViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (_window is not null && ReferenceEquals(_editor, editor))
        {
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        if (_window is not null)
        {
            _window.Close();
        }

        var viewModel = new EqAnalysisWindowViewModel(editor, _restorationAnalysisService);
        var window = new EqAnalysisWindow(_placementStore)
        {
            DataContext = viewModel
        };

        _window = window;
        _editor = editor;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _editor = null;
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
