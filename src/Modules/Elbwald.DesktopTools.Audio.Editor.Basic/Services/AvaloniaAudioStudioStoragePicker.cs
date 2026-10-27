using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class AvaloniaAudioStudioStoragePicker : IAudioStudioStoragePicker
{
    private static readonly FilePickerFileType WavFileType = new("WAV-Audio")
    {
        Patterns = ["*.wav", "*.wave"]
    };

    private static readonly FilePickerFileType ProjectFileType = new("Elbwald Audio-Projekt")
    {
        Patterns = ["*.ewaudio"]
    };

    private readonly AudioStudioPickerStateStore _pickerStateStore;

    public AvaloniaAudioStudioStoragePicker(AudioStudioPickerStateStore pickerStateStore)
    {
        ArgumentNullException.ThrowIfNull(pickerStateStore);
        _pickerStateStore = pickerStateStore;
    }

    public async Task<IReadOnlyList<string>> PickWavFilesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider = GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanOpen)
        {
            return [];
        }

        var startLocation = await ResolveStartLocationAsync(storageProvider, cancellationToken);
        var files = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "WAV-Dateien importieren",
                FileTypeFilter = [WavFileType],
                SuggestedStartLocation = startLocation
            });

        cancellationToken.ThrowIfCancellationRequested();

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        if (paths.Length > 0)
        {
            _pickerStateStore.SaveDirectoryFromPath(paths[0]);
        }

        return paths;
    }

    public async Task<string?> PickProjectToOpenAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider = GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanOpen)
        {
            return null;
        }

        var startLocation = await ResolveStartLocationAsync(storageProvider, cancellationToken);
        var files = await storageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "Audio-Projekt öffnen",
                FileTypeFilter = [ProjectFileType],
                SuggestedStartLocation = startLocation
            });

        cancellationToken.ThrowIfCancellationRequested();

        var path = files.Count == 0
            ? null
            : files[0].TryGetLocalPath();

        _pickerStateStore.SaveDirectoryFromPath(path);
        return path;
    }

    public async Task<string?> PickProjectToSaveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider = GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanSave)
        {
            return null;
        }

        var startLocation = await ResolveStartLocationAsync(storageProvider, cancellationToken);
        var file = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Audio-Projekt speichern",
                SuggestedFileName = NormalizeSuggestedFileName(suggestedFileName),
                DefaultExtension = "ewaudio",
                FileTypeChoices = [ProjectFileType],
                ShowOverwritePrompt = true,
                SuggestedStartLocation = startLocation
            });

        cancellationToken.ThrowIfCancellationRequested();

        var path = file?.TryGetLocalPath();
        _pickerStateStore.SaveDirectoryFromPath(path);
        return path;
    }

    public async Task<string?> PickWavToSaveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider = GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanSave)
        {
            return null;
        }

        var startLocation = await ResolveStartLocationAsync(storageProvider, cancellationToken);
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "Mixdown.wav"
            : $"{Path.GetFileNameWithoutExtension(suggestedFileName.Trim())}.wav";

        var file = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "WAV-Mixdown exportieren",
                SuggestedFileName = fileName,
                DefaultExtension = "wav",
                FileTypeChoices = [WavFileType],
                ShowOverwritePrompt = true,
                SuggestedStartLocation = startLocation
            });

        cancellationToken.ThrowIfCancellationRequested();

        var path = file?.TryGetLocalPath();
        _pickerStateStore.SaveDirectoryFromPath(path);
        return path;
    }

    private async Task<IStorageFolder?> ResolveStartLocationAsync(
        IStorageProvider storageProvider,
        CancellationToken cancellationToken)
    {
        var lastDirectory = _pickerStateStore.LoadLastDirectory();
        if (string.IsNullOrWhiteSpace(lastDirectory))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await storageProvider.TryGetFolderFromPathAsync(lastDirectory);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime
            is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.MainWindow?.StorageProvider;
    }

    private static string NormalizeSuggestedFileName(string suggestedFileName)
    {
        var fileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "Unbenannt"
            : Path.GetFileNameWithoutExtension(suggestedFileName.Trim());

        return $"{fileName}.ewaudio";
    }
}
