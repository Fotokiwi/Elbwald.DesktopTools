using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.App.Services;

public sealed class AvaloniaMediaSourcePicker
    : IMediaSourcePicker
{
    public async Task<string?> PickFolderAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider =
            GetStorageProvider();

        if (storageProvider is null
            || !storageProvider.CanPickFolder)
        {
            return null;
        }

        var folders =
            await storageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = "Medienordner auswählen"
                });

        cancellationToken.ThrowIfCancellationRequested();

        return folders.Count == 0
            ? null
            : folders[0].TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageProvider =
            GetStorageProvider();

        if (storageProvider is null
            || !storageProvider.CanOpen)
        {
            return null;
        }

        var files =
            await storageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = "Mediendatei auswählen"
                });

        cancellationToken.ThrowIfCancellationRequested();

        return files.Count == 0
            ? null
            : files[0].TryGetLocalPath();
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
}
