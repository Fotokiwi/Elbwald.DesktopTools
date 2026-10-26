namespace Elbwald.DesktopTools.Contracts.Media.Audio;

/// <summary>
/// Channel layouts currently supported by the Elbwald audio workspace.
/// The editor deliberately starts with mono and stereo; wider layouts can be
/// added without changing the track model used by the UI.
/// </summary>
public enum AudioChannelLayout
{
    Mono = 1,
    Stereo = 2
}
