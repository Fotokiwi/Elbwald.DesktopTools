namespace Elbwald.DesktopTools.Audio.Editor.Basic.Models;

/// <summary>
/// Compact min/max representation of an audio source. The envelope keeps only
/// enough information for UI rendering and never replaces the original samples.
/// </summary>
public sealed class WaveformEnvelope
{
    public WaveformEnvelope(
        int sampleRate,
        int channels,
        long sampleFrames,
        IReadOnlyList<WaveformPeak> left,
        IReadOnlyList<WaveformPeak>? right = null)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        ArgumentNullException.ThrowIfNull(left);

        if (channels == 2 && right is null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        SampleRate = sampleRate;
        Channels = channels;
        SampleFrames = Math.Max(0, sampleFrames);
        Left = left;
        Right = right;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public long SampleFrames { get; }

    public double DurationSeconds => SampleFrames / (double)SampleRate;

    public IReadOnlyList<WaveformPeak> Left { get; }

    public IReadOnlyList<WaveformPeak>? Right { get; }

    public int PeakCount => Left.Count;
}

public readonly record struct WaveformPeak(float Minimum, float Maximum)
{
    public static WaveformPeak Empty => new(0, 0);
}
