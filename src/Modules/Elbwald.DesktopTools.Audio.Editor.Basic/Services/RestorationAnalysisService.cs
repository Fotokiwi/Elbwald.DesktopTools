using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Offline/inspection-side restoration analysis. None of this work runs in the realtime
/// playback callback. It renders only the requested track/time range to mono and derives
/// spectrogram/noise-profile data from those samples.
/// </summary>
public sealed class RestorationAnalysisService
{
    public const double MaximumSpectrogramDurationSeconds = 45;
    public const double MaximumNoiseProfileDurationSeconds = 20;
    public const double MinimumNoiseProfileDurationSeconds = 0.15;

    private readonly WavSampleDecoder _decoder;

    public RestorationAnalysisService(WavSampleDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        _decoder = decoder;
    }

    public async Task<SpectrogramData> BuildSpectrogramAsync(
        AudioTrackViewModel track,
        double startSeconds,
        double durationSeconds,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ValidateSampleRate(sampleRate);

        var duration = Math.Clamp(durationSeconds, 0.05, MaximumSpectrogramDurationSeconds);
        var start = Math.Max(0, startSeconds);
        var samples = await RenderTrackMonoAsync(track, start, duration, sampleRate, cancellationToken);

        const int fftSize = 4096;
        const int columns = 320;
        const int rows = 112;
        const double minimumFrequency = 20;
        var maximumFrequency = Math.Min(20_000, sampleRate / 2d);
        var output = new float[columns * rows];
        Array.Fill(output, -120f);

        if (samples.Length == 0 || maximumFrequency <= minimumFrequency)
        {
            return new SpectrogramData(
                columns,
                rows,
                sampleRate,
                fftSize,
                start,
                duration,
                minimumFrequency,
                maximumFrequency,
                output);
        }

        var frame = new float[fftSize];
        for (var column = 0; column < columns; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var center = columns <= 1
                ? samples.Length / 2
                : (int)Math.Round(column / (double)(columns - 1) * Math.Max(0, samples.Length - 1));
            var sourceStart = center - fftSize / 2;
            Array.Clear(frame);

            var copyStart = Math.Max(0, sourceStart);
            var copyEnd = Math.Min(samples.Length, sourceStart + fftSize);
            if (copyEnd > copyStart)
            {
                Array.Copy(samples, copyStart, frame, copyStart - sourceStart, copyEnd - copyStart);
            }

            var spectrum = SpectrumAnalyzer.Analyze(frame, sampleRate, fftSize, -120);
            if (!spectrum.HasData)
            {
                continue;
            }

            for (var row = 0; row < rows; row++)
            {
                // row 0 is the highest frequency so the display reads naturally top-to-bottom.
                var lowToHigh = 1d - row / (double)Math.Max(1, rows - 1);
                var frequency = minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, lowToHigh);
                var bin = (int)Math.Round(frequency * spectrum.FftSize / spectrum.SampleRate);
                bin = Math.Clamp(bin, 0, spectrum.MagnitudesDb.Length - 1);
                output[column * rows + row] = (float)spectrum.MagnitudesDb[bin];
            }
        }

        return new SpectrogramData(
            columns,
            rows,
            sampleRate,
            fftSize,
            start,
            duration,
            minimumFrequency,
            maximumFrequency,
            output);
    }

    public async Task<NoiseProfileData> LearnNoiseProfileAsync(
        AudioTrackViewModel track,
        double selectionStartSeconds,
        double selectionDurationSeconds,
        int sampleRate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);
        ValidateSampleRate(sampleRate);

        if (selectionDurationSeconds < MinimumNoiseProfileDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Das Rauschprofil braucht mindestens {MinimumNoiseProfileDurationSeconds:0.00} s Auswahl.");
        }

        if (selectionDurationSeconds > MaximumNoiseProfileDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Für ein Rauschprofil bitte höchstens {MaximumNoiseProfileDurationSeconds:0} s auswählen.");
        }

        var start = Math.Max(0, selectionStartSeconds);
        var samples = await RenderTrackMonoAsync(
            track,
            start,
            selectionDurationSeconds,
            sampleRate,
            cancellationToken);

        const int fftSize = 4096;
        const int hopSize = 2048;
        var binCount = fftSize / 2 + 1;
        var accumulatedPower = new double[binCount];
        var frameCount = 0;
        var frameBuffer = new float[fftSize];

        if (samples.Length == 0)
        {
            return new NoiseProfileData(sampleRate, fftSize, start, selectionDurationSeconds, []);
        }

        for (var offset = 0; offset < samples.Length; offset += hopSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(frameBuffer);
            var count = Math.Min(fftSize, samples.Length - offset);
            Array.Copy(samples, offset, frameBuffer, 0, count);

            var spectrum = SpectrumAnalyzer.Analyze(frameBuffer, sampleRate, fftSize, -120);
            if (!spectrum.HasData)
            {
                continue;
            }

            for (var bin = 0; bin < Math.Min(binCount, spectrum.MagnitudesDb.Length); bin++)
            {
                // Average in the power domain, not directly in decibels.
                accumulatedPower[bin] += Math.Pow(10d, spectrum.MagnitudesDb[bin] / 10d);
            }

            frameCount++;
            if (offset + fftSize >= samples.Length)
            {
                break;
            }
        }

        if (frameCount == 0)
        {
            return new NoiseProfileData(sampleRate, fftSize, start, selectionDurationSeconds, []);
        }

        var averagedDb = new double[binCount];
        for (var bin = 0; bin < binCount; bin++)
        {
            var power = accumulatedPower[bin] / frameCount;
            averagedDb[bin] = Math.Clamp(10d * Math.Log10(Math.Max(1e-12, power)), -120, 0);
        }

        return new NoiseProfileData(sampleRate, fftSize, start, selectionDurationSeconds, averagedDb);
    }

    private async Task<float[]> RenderTrackMonoAsync(
        AudioTrackViewModel track,
        double windowStartSeconds,
        double durationSeconds,
        int outputSampleRate,
        CancellationToken cancellationToken)
    {
        var outputFrameCount = checked((int)Math.Ceiling(durationSeconds * outputSampleRate));
        if (outputFrameCount <= 0)
        {
            return [];
        }

        var output = new float[outputFrameCount];
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
            var requestedFramesLong = sourceEndFrame - sourceStartFrame;
            var requestedFrames = checked((int)Math.Min(int.MaxValue, requestedFramesLong));

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
                output.Length,
                (int)Math.Ceiling((overlapEnd - windowStartSeconds) * outputSampleRate));
            var clipGain = DbToLinear(clip.GainDb);

            for (var outputIndex = outputStart; outputIndex < outputEnd; outputIndex++)
            {
                var timelineTime = windowStartSeconds + outputIndex / (double)outputSampleRate;
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
                var lowerSample = ReadMonoSample(block, lower);
                var upperSample = ReadMonoSample(block, upper);
                var sample = lowerSample + (upperSample - lowerSample) * fraction;
                var fade = CalculateFadeGain(clip, clipLocalTime);
                output[outputIndex] += (float)(sample * clipGain * fade);
            }
        }

        return output;
    }

    private static float ReadMonoSample(DecodedAudioBlock block, int frame)
    {
        var index = frame * block.Channels;
        if (block.Channels == 1)
        {
            return block.InterleavedSamples[index];
        }

        return (block.InterleavedSamples[index] + block.InterleavedSamples[index + 1]) * 0.5f;
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
            gain *= Math.Clamp(clipLocalTime / clip.FadeInSeconds, 0, 1);
        }

        if (clip.FadeOutSeconds > 0)
        {
            var fadeOutStart = clip.DurationSeconds - clip.FadeOutSeconds;
            if (clipLocalTime > fadeOutStart)
            {
                gain *= Math.Clamp((clip.DurationSeconds - clipLocalTime) / clip.FadeOutSeconds, 0, 1);
            }
        }

        return gain;
    }

    private static double DbToLinear(double db) =>
        db <= -60 ? 0 : Math.Pow(10, db / 20d);

    private static void ValidateSampleRate(int sampleRate)
    {
        if (sampleRate is < 8_000 or > 384_000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
    }
}
