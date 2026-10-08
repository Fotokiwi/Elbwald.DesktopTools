namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaSourcePicker
{
    Task<string?> PickFolderAsync(
        CancellationToken cancellationToken = default);

    Task<string?> PickFileAsync(
        CancellationToken cancellationToken = default);
}
