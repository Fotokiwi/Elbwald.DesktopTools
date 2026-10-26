using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;
using Elbwald.DesktopTools.Audio.Editor.Basic.Views;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class StemRepairWindowService
{
    private readonly StemRepairWindowPlacementStore _placementStore;
    private readonly StemRepairAnalysisService _analysisService;
    private StemRepairWindow? _window;
    private StemRepairWindowViewModel? _viewModel;
    private AudioEditorViewModel? _editor;

    public StemRepairWindowService(
        StemRepairWindowPlacementStore placementStore,
        StemRepairAnalysisService analysisService)
    {
        _placementStore = placementStore ?? throw new ArgumentNullException(nameof(placementStore));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
    }

    public void Show(AudioEditorViewModel editor, AudioTrackViewModel track)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(track);

        if (_window is not null && _viewModel is not null && ReferenceEquals(_editor, editor))
        {
            _viewModel.SetTrack(track);
            if (_window.WindowState == WindowState.Minimized)
            {
                _window.WindowState = WindowState.Normal;
            }

            _window.Activate();
            return;
        }

        _window?.Close();

        var viewModel = new StemRepairWindowViewModel(editor, track, _analysisService);
        var window = new StemRepairWindow(_placementStore)
        {
            DataContext = viewModel
        };

        _window = window;
        _viewModel = viewModel;
        _editor = editor;

        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
                _viewModel = null;
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
