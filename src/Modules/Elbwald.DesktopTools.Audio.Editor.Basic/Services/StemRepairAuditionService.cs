using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed record StemRepairAuditionClip(
    string FilePath,
    int SampleRate,
    double DurationSeconds,
    double ProjectStartSeconds,
    double MonitorGainDb);

/// <summary>
/// Renders a short dry-vs-wet difference signal for a single repair proposal.
/// The project and the track's current effect state are never mutated.
/// </summary>
public sealed class StemRepairAuditionService
{
    private const double DefaultDurationSeconds = 8d;
    private const double MaximumMonitorBoostDb = 18d;
    private readonly WavSampleDecoder _decoder;

    public StemRepairAuditionService(WavSampleDecoder decoder)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public async Task<StemRepairAuditionClip> CreateRemovedSignalAsync(
        AudioTrackViewModel track,
        StemRepairFinding finding,
        double fallbackProjectPositionSeconds,
        int projectSampleRate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(finding);

        var sampleRate = Math.Clamp(projectSampleRate, 8_000, 192_000);
        var trackDuration = Math.Max(0d, track.ProjectEndSeconds);
        if (trackDuration <= 0d)
        {
            throw new InvalidOperationException("Die Spur enthält keine abspielbare Audiodauer.");
        }

        var focus = finding.SuggestedAuditionTimeSeconds ?? fallbackProjectPositionSeconds;
        var start = Math.Clamp(focus - 2d, 0d, Math.Max(0d, trackDuration - 0.25d));
        var duration = Math.Min(DefaultDurationSeconds, trackDuration - start);
        if (duration <= 0.1d)
        {
            throw new InvalidOperationException("An dieser Stelle ist kein ausreichend langer Abhörbereich verfügbar.");
        }

        var dry = await RenderTrackStereoAsync(
            track,
            start,
            duration,
            sampleRate,
            cancellationToken);
        if (dry.Length < 2)
        {
            throw new InvalidOperationException("Für den Abhörbereich konnte kein Audiosignal gerendert werden.");
        }

        var wet = dry.ToArray();
        var processor = new TrackBasicFxProcessor();
        var settings = CreateSettings(finding);
        processor.ProcessPreEq(wet, sampleRate, settings);
        processor.ProcessPostEq(wet, sampleRate, settings);

        var removed = new float[dry.Length];
        var peak = 0d;
        for (var index = 0; index < dry.Length; index++)
        {
            var sample = dry[index] - wet[index];
            removed[index] = sample;
            peak = Math.Max(peak, Math.Abs(sample));
        }

        var peakDb = LinearToDb(peak);
        if (peakDb < -85d)
        {
            throw new InvalidOperationException(
                "Der vorgeschlagene Filter nimmt in diesem Abhörbereich praktisch kein Signal weg. Position ändern oder Analyse erneut prüfen.");
        }

        // Removed signals are often intentionally quiet. Lift only the monitor copy,
        // never the project signal, and cap the boost so the audition stays honest.
        var monitorGainDb = Math.Clamp(-12d - peakDb, 0d, MaximumMonitorBoostDb);
        var monitorGain = Math.Pow(10d, monitorGainDb / 20d);
        if (monitorGain > 1.0001d)
        {
            for (var index = 0; index < removed.Length; index++)
            {
                removed[index] = (float)Math.Clamp(removed[index] * monitorGain, -1d, 1d);
            }
        }

        var directory = Path.Combine(Path.GetTempPath(), "ElbwaldDesktopTools", "StemRepairAudition");
        Directory.CreateDirectory(directory);
        CleanupOldAuditions(directory);

        var filePath = Path.Combine(directory, $"removed-{Guid.NewGuid():N}.wav");
        WritePcm16Stereo(filePath, removed, sampleRate);

        return new StemRepairAuditionClip(
            filePath,
            sampleRate,
            removed.Length / 2d / sampleRate,
            start,
            monitorGainDb);
    }

    public void TryDelete(StemRepairAuditionClip? clip)
    {
        if (clip is null)
        {
            return;
        }

        try
        {
            if (File.Exists(clip.FilePath))
            {
                File.Delete(clip.FilePath);
            }
        }
        catch (IOException)
        {
            // Temporary audition cleanup is best effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Temporary audition cleanup is best effort only.
        }
    }

    private static TrackBasicFxSettings CreateSettings(StemRepairFinding finding)
    {
        return finding.Type switch
        {
            StemRepairFindingType.NoiseFloor => new TrackBasicFxSettings(
                true,
                finding.SuggestedThresholdDb ?? -45d,
                finding.SuggestedReductionDb ?? 18d,
                false, 6_500d, 30d,
                false, 50d, 4, 18d,
                false, 6_500d, 35d),

            StemRepairFindingType.Hum => new TrackBasicFxSettings(
                false, -45d, 18d,
                false, 6_500d, 30d,
                true,
                finding.FrequencyHz ?? 50d,
                finding.SuggestedHarmonics ?? 4,
                finding.SuggestedStrength ?? 18d,
                false, 6_500d, 35d),

            StemRepairFindingType.NarrowbandInterference => new TrackBasicFxSettings(
                false, -45d, 18d,
                true,
                finding.FrequencyHz ?? 6_500d,
                finding.SuggestedWidthPercent ?? 15d,
                false, 50d, 4, 18d,
                false, 6_500d, 35d),

            StemRepairFindingType.Sibilance => new TrackBasicFxSettings(
                false, -45d, 18d,
                false, 6_500d, 30d,
                false, 50d, 4, 18d,
                true,
                finding.FrequencyHz ?? 7_000d,
                finding.SuggestedStrength ?? 35d),

            _ => new TrackBasicFxSettings(
                false, -45d, 18d,
                false, 6_500d, 30d,
                false, 50d, 4, 18d,
                false, 6_500d, 35d)
        };
    }

    private async Task<float[]> RenderTrackStereoAsync(
        AudioTrackViewModel track,
        double windowStartSeconds,
        double durationSeconds,
        int outputSampleRate,
        CancellationToken cancellationToken)
    {
        var outputFrameCount = checked((int)Math.Ceiling(durationSeconds * outputSampleRate));
        var output = new float[checked(outputFrameCount * 2)];
        var windowEndSeconds = windowStartSeconds + durationSeconds;

        foreach (var clip in track.Clips)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (clip.IsMissing || clip.DurationSeconds <= 0 || clip.SourceSampleRate <= 0)
            {
                continue;
            }

            var overlapStart = Math.Max(windowStartSeconds, clip.TimelineStartSeconds);
            var overlapEnd = Math.Min(windowEndSeconds, clip.TimelineEndSeconds);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            var sourceStartSeconds = clip.SourceStartSeconds + (overlapStart - clip.TimelineStartSeconds);
            var sourceEndSeconds = clip.SourceStartSeconds + (overlapEnd - clip.TimelineStartSeconds);
            var sourceStartFrame = Math.Max(0L, (long)Math.Floor(sourceStartSeconds * clip.SourceSampleRate) - 2);
            var sourceEndFrame = Math.Max(
                sourceStartFrame + 2,
                (long)Math.Ceiling(sourceEndSeconds * clip.SourceSampleRate) + 3);
            var requestedFrames = checked((int)Math.Min(int.MaxValue, sourceEndFrame - sourceStartFrame));

            var block = await _decoder.ReadFramesAsync(
                clip.SourcePath,
                sourceStartFrame,
                requestedFrames,
                cancellationToken);
            if (block.FrameCount <= 0)
            {
                continue;
            }

            var outputStart = Math.Max(
                0,
                (int)Math.Floor((overlapStart - windowStartSeconds) * outputSampleRate));
            var outputEnd = Math.Min(
                outputFrameCount,
                (int)Math.Ceiling((overlapEnd - windowStartSeconds) * outputSampleRate));
            var clipGain = DbToLinear(clip.GainDb);

            for (var outputFrame = outputStart; outputFrame < outputEnd; outputFrame++)
            {
                var timelineTime = windowStartSeconds + outputFrame / (double)outputSampleRate;
                var clipLocalTime = timelineTime - clip.TimelineStartSeconds;
                if (clipLocalTime < 0 || clipLocalTime > clip.DurationSeconds)
                {
                    continue;
                }

                var sourceTime = clip.SourceStartSeconds + clipLocalTime;
                var absoluteSourceFrame = sourceTime * clip.SourceSampleRate;
                var localSourceFrame = absoluteSourceFrame - block.StartFrame;
                var lower = (int)Math.Floor(localSourceFrame);
                var fraction = localSourceFrame - lower;
                if (lower < 0 || lower >= block.FrameCount)
                {
                    continue;
                }

                var upper = Math.Min(block.FrameCount - 1, lower + 1);
                var (lowerLeft, lowerRight) = ReadStereoSample(block, lower);
                var (upperLeft, upperRight) = ReadStereoSample(block, upper);
                var left = lowerLeft + (upperLeft - lowerLeft) * fraction;
                var right = lowerRight + (upperRight - lowerRight) * fraction;
                var fade = CalculateFadeGain(clip, clipLocalTime);
                var gain = clipGain * fade;
                var outputIndex = outputFrame * 2;
                output[outputIndex] += (float)(left * gain);
                output[outputIndex + 1] += (float)(right * gain);
            }
        }

        return output;
    }

    private static (double Left, double Right) ReadStereoSample(DecodedAudioBlock block, int frame)
    {
        var index = frame * block.Channels;
        if (block.Channels == 1)
        {
            var mono = block.InterleavedSamples[index];
            return (mono, mono);
        }

        return (block.InterleavedSamples[index], block.InterleavedSamples[index + 1]);
    }

    private static double CalculateFadeGain(AudioClipViewModel clip, double clipLocalTime)
    {
        if (clip.IsMutedAtLocalSeconds(clipLocalTime))
        {
            return 0d;
        }

        var gain = 1d;
        if (clip.FadeInSeconds > 0 && clipLocalTime < clip.FadeInSeconds)
        {
            gain *= Math.Clamp(clipLocalTime / clip.FadeInSeconds, 0d, 1d);
        }

        if (clip.FadeOutSeconds > 0)
        {
            var fadeOutStart = clip.DurationSeconds - clip.FadeOutSeconds;
            if (clipLocalTime > fadeOutStart)
            {
                gain *= Math.Clamp((clip.DurationSeconds - clipLocalTime) / clip.FadeOutSeconds, 0d, 1d);
            }
        }

        return gain;
    }

    private static void WritePcm16Stereo(string filePath, float[] samples, int sampleRate)
    {
        const short channels = 2;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var dataSize = checked(samples.Length * sizeof(short));

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        foreach (var sample in samples)
        {
            var clamped = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)Math.Round(clamped * short.MaxValue));
        }
    }

    private static void CleanupOldAuditions(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "removed-*.wav"))
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddHours(-6))
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static double DbToLinear(double db) => Math.Pow(10d, db / 20d);

    private static double LinearToDb(double value) =>
        value <= 1e-12 ? -120d : 20d * Math.Log10(value);
}
