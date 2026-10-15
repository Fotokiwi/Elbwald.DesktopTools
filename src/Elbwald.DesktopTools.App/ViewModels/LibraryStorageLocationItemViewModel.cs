using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class LibraryStorageLocationItemViewModel
{
    public LibraryStorageLocationItemViewModel(
        StorageEndpoint endpoint,
        StorageLocationResolution resolution,
        Func<string, Task> removeAsync)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(removeAsync);

        EndpointId = endpoint.Id;
        Name = endpoint.Name;
        VolumeId = endpoint.Location.VolumeId;
        IsAvailable = resolution.IsAvailable;
        Status = resolution.Status switch
        {
            StorageLocationStatus.Available => "Verfügbar · Kennung bestätigt",
            StorageLocationStatus.VolumeUnavailable => "Laufwerk nicht verbunden",
            StorageLocationStatus.AmbiguousVolume => "Laufwerk nicht eindeutig",
            StorageLocationStatus.PathMissing => "Ordner nicht erreichbar",
            _ => "Nicht verfügbar"
        };

        Path = resolution.ResolvedPath
            ?? endpoint.Location.LastKnownAbsolutePath
            ?? endpoint.Location.RelativePath
            ?? "Pfad nicht verfügbar";

        Details = resolution.IsAvailable
            ? Path
            : string.IsNullOrWhiteSpace(resolution.Message)
                ? Path
                : $"{Path} · {resolution.Message}";

        RemoveCommand = new AsyncRelayCommand(
            () => removeAsync(EndpointId));
    }

    public string EndpointId { get; }

    public string Name { get; }

    public string Status { get; }

    public string Path { get; }

    public string Details { get; }

    public string VolumeId { get; }

    public bool IsAvailable { get; }

    public IAsyncRelayCommand RemoveCommand { get; }
}
