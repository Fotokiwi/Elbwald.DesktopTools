namespace Elbwald.DesktopTools.Contracts.Media.Audio;

public static class AudioProjectLimits
{
    /// <summary>
    /// Product limit for the basic multi-track editor. The processing engine is
    /// expected to honour the same limit so the UI and render graph cannot drift.
    /// </summary>
    public const int MaxTrackCount = 16;
}
