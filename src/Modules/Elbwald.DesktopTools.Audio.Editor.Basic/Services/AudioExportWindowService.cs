using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;
using Elbwald.DesktopTools.Audio.Editor.Basic.Views;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class AudioExportWindowService
{
    private readonly AudioWavExportService _exportService;
    private readonly AudioExportCheckService _exportCheckService;
    private readonly IAudioStudioStoragePicker _storagePicker;

    public AudioExportWindowService(
        AudioWavExportService exportService,
        AudioExportCheckService exportCheckService,
        IAudioStudioStoragePicker storagePicker)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _exportCheckService = exportCheckService ?? throw new ArgumentNullException(nameof(exportCheckService));
        _storagePicker = storagePicker ?? throw new ArgumentNullException(nameof(storagePicker));
    }

    public async Task<string?> ShowAsync(
        AudioProjectDocument document,
        string suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(document);

        var viewModel = new AudioExportWindowViewModel(
            document,
            suggestedFileName,
            _exportService,
            _exportCheckService,
            _storagePicker);
        var window = new AudioExportWindow
        {
            DataContext = viewModel
        };

        viewModel.CloseRequested += (_, _) => window.Close();

        var owner =
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        try
        {
            if (owner is not null)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                window.Show();
                return null;
            }

            return viewModel.ExportedFilePath;
        }
        finally
        {
            viewModel.Dispose();
        }
    }
}
