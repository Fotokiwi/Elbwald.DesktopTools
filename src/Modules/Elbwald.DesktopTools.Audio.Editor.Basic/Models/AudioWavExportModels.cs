namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

public enum AudioWavEncoding
{
    Pcm16,
    Pcm24,
    Float32
}

public sealed record AudioWavExportSettings(
    int SampleRate,
    AudioWavEncoding Encoding,
    bool Dither,
    bool IncludeEffectTail);

public sealed record AudioWavExportResult(
    string FilePath,
    long FramesWritten,
    int SampleRate,
    AudioWavEncoding Encoding,
    double DurationSeconds,
    double PeakDbfs,
    long SamplesAboveFullScale,
    double IncludedTailSeconds)
{
    public string FormatLabel => Encoding switch
    {
        AudioWavEncoding.Pcm16 => "16-Bit PCM",
        AudioWavEncoding.Pcm24 => "24-Bit PCM",
        AudioWavEncoding.Float32 => "32-Bit Float",
        _ => Encoding.ToString()
    };
}
