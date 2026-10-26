namespace Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

public readonly record struct AudioStereoMeter(double LeftDb, double RightDb)
{
    public static AudioStereoMeter Silence { get; } = new(-60, -60);

    public double PeakDb => Math.Max(LeftDb, RightDb);
}

public sealed record AudioMeterSnapshot(
    AudioStereoMeter Master,
    IReadOnlyDictionary<Guid, AudioStereoMeter> Tracks)
{
    public static AudioMeterSnapshot Empty { get; } =
        new(AudioStereoMeter.Silence, new Dictionary<Guid, AudioStereoMeter>());
}
