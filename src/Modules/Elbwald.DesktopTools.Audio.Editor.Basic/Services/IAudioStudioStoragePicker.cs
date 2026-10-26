namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public interface IAudioStudioStoragePicker
{
    Task<IReadOnlyList<string>> PickWavFilesAsync(
        CancellationToken cancellationToken = default);

    Task<string?> PickProjectToOpenAsync(
        CancellationToken cancellationToken = default);

    Task<string?> PickProjectToSaveAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}
