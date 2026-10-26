using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Elbwald.DesktopTools.Audio.Editor.Basic.Controls;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Views;

public partial class AudioEditorView : UserControl
{
    public AudioEditorView()
    {
        InitializeComponent();
    }

    private async void OnProjectWaveformPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not AudioWaveformView waveform ||
            DataContext is not AudioEditorViewModel viewModel ||
            !e.GetCurrentPoint(waveform).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (waveform.DataContext is AudioTrackViewModel track)
        {
            viewModel.SelectedTrack = track;
        }
        else
        {
            viewModel.SelectMasterInspector();
        }

        var width = Math.Max(1d, waveform.Bounds.Width);
        var normalized = Math.Clamp(e.GetPosition(waveform).X / width, 0d, 1d);
        await viewModel.SeekToPositionAsync(normalized * viewModel.ProjectDurationSeconds);
        e.Handled = true;
    }


    private void OnMasterRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not AudioEditorViewModel viewModel)
        {
            return;
        }

        var sourceControl = sender as Control;
        if (sourceControl is not null && !e.GetCurrentPoint(sourceControl).Properties.IsLeftButtonPressed)
        {
            return;
        }

        viewModel.SelectMasterInspector();
    }

    private async void OnSelectedClipWaveformPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not AudioWaveformView waveform ||
            DataContext is not AudioEditorViewModel viewModel ||
            viewModel.SelectedClip is not { } clip ||
            !e.GetCurrentPoint(waveform).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var width = Math.Max(1d, waveform.Bounds.Width);
        var normalized = Math.Clamp(e.GetPosition(waveform).X / width, 0d, 1d);
        var targetSeconds = clip.TimelineStartSeconds + normalized * clip.DurationSeconds;
        await viewModel.SeekToPositionAsync(targetSeconds);
        e.Handled = true;
    }

    private void OnPeakEventJumpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MasteringPeakEventResult peak } ||
            DataContext is not AudioEditorViewModel viewModel)
        {
            return;
        }

        viewModel.JumpToPeakEventCommand.Execute(peak);
        e.Handled = true;
    }

    private void OnPeakEventListenClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MasteringPeakEventResult peak } ||
            DataContext is not AudioEditorViewModel viewModel)
        {
            return;
        }

        viewModel.ListenPeakEventCommand.Execute(peak);
        e.Handled = true;
    }

    private void OnStemRepairClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AudioTrackViewModel track } ||
            DataContext is not AudioEditorViewModel viewModel)
        {
            return;
        }

        viewModel.OpenStemRepairAnalysis(track);
        e.Handled = true;
    }

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AudioEditorViewModel viewModel)
        {
            viewModel.BeginTimelineSeek();
        }
    }

    private async void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is AudioEditorViewModel viewModel)
        {
            await viewModel.CommitTimelineSeekAsync();
        }
    }
}
