using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Conservative, explainable offline analyzer for individual stems.
/// It looks for repair-oriented technical patterns only: noise floor,
/// 50/60 Hz mains hum, very narrow spectral spikes and sibilance.
/// It intentionally does not make subjective mix/tone judgements.
/// </summary>
public sealed class StemRepairAnalysisService
{
    private const int TargetSampleRate = 24_000;
    private const int FftSize = 4096;
    private const int HopSize = 2048;
    private const double MaximumContinuousAnalysisSeconds = 360d;
    private const int MaximumLongTrackWindows = 12;
    private const double LongTrackWindowSeconds = 30d;

    private readonly WavSampleDecoder _decoder;

    public StemRepairAnalysisService(WavSampleDecoder decoder)
    {
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public async Task<StemRepairAnalysisResult> AnalyzeAsync(
        AudioTrackViewModel track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!track.HasClips)
        {
            throw new InvalidOperationException("Die Spur enthält keine Clips.");
        }

        if (track.Clips.All(clip => clip.IsMissing))
        {
            throw new InvalidOperationException("Für diese Spur sind keine Quelldateien verfügbar.");
        }

        var duration = Math.Max(0d, track.ProjectEndSeconds);
        if (duration <= 0)
        {
            throw new InvalidOperationException("Die Spur enthält keine analysierbare Audiodauer.");
        }

        var windows = BuildAnalysisWindows(duration);
        var renderedWindows = new List<RenderedAnalysisWindow>(windows.Count);
        var processedWindows = 0;

        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = await RenderTrackMonoAsync(
                track,
                window.StartSeconds,
                window.DurationSeconds,
                TargetSampleRate,
                cancellationToken);

            if (samples.Length > 0)
            {
                renderedWindows.Add(new RenderedAnalysisWindow(window.StartSeconds, samples));
            }

            processedWindows++;
            progress?.Report(processedWindows / (double)Math.Max(1, windows.Count) * 0.35d);
        }

        if (renderedWindows.Count == 0 || renderedWindows.Sum(item => item.Samples.Length) < FftSize)
        {
            throw new InvalidOperationException("Für eine belastbare Stem-Analyse ist nicht genug Audiomaterial vorhanden.");
        }

        var rmsBlocks = renderedWindows
            .SelectMany(window => CalculateRmsBlocks(window.Samples, TargetSampleRate))
            .ToArray();
        var validLevels = rmsBlocks.Where(value => value > -100d).OrderBy(value => value).ToArray();
        if (validLevels.Length == 0)
        {
            throw new InvalidOperationException("Die Spur enthält nur Stille oder extrem leises Material.");
        }

        var activeLevel = Percentile(validLevels, 0.80);
        var quietSubsetCount = Math.Max(1, (int)Math.Ceiling(validLevels.Length * 0.20));
        var noiseFloor = Median(validLevels.Take(quietSubsetCount).ToArray());

        var averagePower = new double[FftSize / 2 + 1];
        var spectralFrames = 0;
        var sibilanceFrames = new List<TimedValue>();
        var hum50Frames = new List<TimedHumEvidence>();
        var hum60Frames = new List<TimedHumEvidence>();

        foreach (var window in renderedWindows)
        {
            for (var offset = 0; offset + FftSize <= window.Samples.Length; offset += HopSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = new float[FftSize];
                Array.Copy(window.Samples, offset, frame, 0, FftSize);
                var frameRms = RmsDb(frame);
                if (frameRms <= Math.Max(-85d, noiseFloor + 4d))
                {
                    continue;
                }

                var spectrum = SpectrumAnalyzer.Analyze(frame, TargetSampleRate, FftSize, -120);
                if (!spectrum.HasData)
                {
                    continue;
                }

                for (var bin = 0; bin < averagePower.Length && bin < spectrum.MagnitudesDb.Length; bin++)
                {
                    averagePower[bin] += Math.Pow(10d, spectrum.MagnitudesDb[bin] / 10d);
                }

                var absoluteTime = window.StartSeconds + (offset + FftSize / 2d) / TargetSampleRate;
                var mid = BandPower(spectrum, 1_000, 4_000);
                var high = BandPower(spectrum, 5_000, 10_000);
                if (mid > 1e-12 && high > 1e-12)
                {
                    sibilanceFrames.Add(new TimedValue(
                        absoluteTime,
                        10d * Math.Log10(high / mid)));
                }

                hum50Frames.Add(EvaluateHumFrame(spectrum.MagnitudesDb, 50d, absoluteTime));
                hum60Frames.Add(EvaluateHumFrame(spectrum.MagnitudesDb, 60d, absoluteTime));
                spectralFrames++;
            }
        }

        progress?.Report(0.68d);

        if (spectralFrames == 0)
        {
            throw new InvalidOperationException("Aus der Spur konnte kein belastbares Frequenzprofil erstellt werden.");
        }

        var averageSpectrumDb = averagePower
            .Select(power => 10d * Math.Log10(Math.Max(1e-12, power / spectralFrames)))
            .ToArray();

        var findings = new List<StemRepairFinding>();
        AddNoiseFinding(findings, noiseFloor, activeLevel);
        AddHumFinding(findings, hum50Frames, hum60Frames);
        AddNarrowbandFinding(findings, averageSpectrumDb, renderedWindows, noiseFloor, cancellationToken);
        AddSibilanceFinding(findings, track.Name, sibilanceFrames);

        progress?.Report(1d);

        return new StemRepairAnalysisResult(
            track.Id,
            track.Name,
            duration,
            windows.Sum(window => window.DurationSeconds),
            TargetSampleRate,
            activeLevel,
            noiseFloor,
            findings
                .OrderByDescending(finding => finding.Severity)
                .ThenByDescending(finding => finding.Confidence)
                .ToArray());
    }

    private static void AddNoiseFinding(
        ICollection<StemRepairFinding> findings,
        double noiseFloor,
        double activeLevel)
    {
        var separation = activeLevel - noiseFloor;
        if (noiseFloor < -68d || separation < 9d)
        {
            return;
        }

        var confidence = Math.Clamp((noiseFloor + 68d) / 18d + separation / 60d, 0.45d, 0.96d);
        var severity = noiseFloor switch
        {
            > -42d => StemRepairFindingSeverity.High,
            > -52d => StemRepairFindingSeverity.Medium,
            _ => StemRepairFindingSeverity.Low
        };

        var threshold = Math.Clamp(noiseFloor + 5d, -70d, -20d);
        var reduction = Math.Clamp(10d + (noiseFloor + 68d) * 0.55d, 10d, 28d);

        findings.Add(new StemRepairFinding(
            StemRepairFindingType.NoiseFloor,
            severity,
            confidence,
            "Erhöhter Rauschboden in leisen Passagen",
            $"Leise Bereiche liegen ungefähr bei {noiseFloor:0.0} dBFS; aktive Passagen bei etwa {activeLevel:0.0} dBFS.",
            $"Expander als sanfte erste Reparatur: Schwelle ca. {threshold:0} dBFS, Absenkung ca. {reduction:0} dB.",
            SuggestedThresholdDb: threshold,
            SuggestedReductionDb: reduction));
    }

    private static void AddHumFinding(
        ICollection<StemRepairFinding> findings,
        IReadOnlyList<TimedHumEvidence> fiftyFrames,
        IReadOnlyList<TimedHumEvidence> sixtyFrames)
    {
        var fifty = SummarizeHumPersistence(fiftyFrames);
        var sixty = SummarizeHumPersistence(sixtyFrames);
        var bestFrequency = fifty.Score >= sixty.Score ? 50d : 60d;
        var best = fifty.Score >= sixty.Score ? fifty : sixty;

        // A true mains hum should recur across time and carry at least two harmonics.
        // This is deliberately stricter than the original average-spectrum test.
        if (best.Persistence < 0.24d || best.Score < 5d || best.Harmonics < 2)
        {
            return;
        }

        var confidence = Math.Clamp(
            0.28d + best.Persistence * 0.72d + Math.Clamp((best.Score - 5d) / 20d, 0d, 0.18d),
            0.45d,
            0.98d);
        var severity = best.Score switch
        {
            >= 12d when best.Persistence >= 0.55d => StemRepairFindingSeverity.High,
            >= 7d => StemRepairFindingSeverity.Medium,
            _ => StemRepairFindingSeverity.Low
        };
        var reduction = Math.Clamp(10d + best.Score * 0.75d, 12d, 24d);

        findings.Add(new StemRepairFinding(
            StemRepairFindingType.Hum,
            severity,
            confidence,
            $"{bestFrequency:0} Hz Netzbrummen wahrscheinlich",
            $"Das Muster erscheint in {best.Persistence * 100d:0}% der aktiven Analysefenster; dabei sind typischerweise {best.Harmonics} passende Oberwellen etwa {best.Score:0.0} dB über ihrer lokalen Umgebung.",
            $"Brummfilter auf {bestFrequency:0} Hz mit {best.Harmonics} Oberwellen und etwa {reduction:0} dB Stärke. Vorher das entfernte Signal anhören.",
            FrequencyHz: bestFrequency,
            SuggestedStrength: reduction,
            SuggestedHarmonics: best.Harmonics,
            Persistence: best.Persistence,
            SuggestedAuditionTimeSeconds: best.StrongestTimeSeconds));
    }

    private static void AddNarrowbandFinding(
        ICollection<StemRepairFinding> findings,
        IReadOnlyList<double> spectrumDb,
        IReadOnlyList<RenderedAnalysisWindow> renderedWindows,
        double noiseFloorDbfs,
        CancellationToken cancellationToken)
    {
        var binWidth = TargetSampleRate / (double)FftSize;
        var startBin = Math.Max(2, (int)Math.Ceiling(700d / binWidth));
        var endBin = Math.Min(spectrumDb.Count - 3, (int)Math.Floor(11_500d / binWidth));
        var bestBin = -1;
        var bestExcess = double.MinValue;

        for (var bin = startBin; bin <= endBin; bin++)
        {
            var frequency = bin * binWidth;
            if (IsNearHarmonic(frequency, 50d, 8d) || IsNearHarmonic(frequency, 60d, 8d))
            {
                continue;
            }

            var excess = LocalPeakExcess(spectrumDb, bin, 12, 2);
            if (excess > bestExcess)
            {
                bestExcess = excess;
                bestBin = bin;
            }
        }

        if (bestBin < 0 || bestExcess < 12d)
        {
            return;
        }

        var frequencyHz = bestBin * binWidth;
        var evidence = MeasureNarrowbandPersistence(
            renderedWindows,
            bestBin,
            noiseFloorDbfs,
            cancellationToken);

        // 700–2000 Hz is packed with vocal formants and instrument fundamentals.
        // Do not recommend a notch there unless the line is unusually sharp AND
        // persists at the exact same FFT bin for a large share of active frames.
        var sensitiveMidrange = frequencyHz is >= 700d and <= 2_000d;
        var minimumAverageExcess = sensitiveMidrange ? 18d : 12d;
        var minimumPersistence = sensitiveMidrange ? 0.55d : 0.30d;
        var minimumFrameExcess = sensitiveMidrange ? 14d : 10d;

        if (bestExcess < minimumAverageExcess ||
            evidence.Persistence < minimumPersistence ||
            evidence.MeanExcessDb < minimumFrameExcess)
        {
            return;
        }

        var confidence = Math.Clamp(
            0.32d + evidence.Persistence * 0.65d + Math.Clamp((bestExcess - minimumAverageExcess) / 30d, 0d, 0.12d),
            0.48d,
            0.96d);
        var severity = bestExcess >= 20d && evidence.Persistence >= 0.55d
            ? StemRepairFindingSeverity.Medium
            : StemRepairFindingSeverity.Low;
        var width = Math.Clamp(18d - (bestExcess - 12d) * 0.8d, 6d, 20d);

        findings.Add(new StemRepairFinding(
            StemRepairFindingType.NarrowbandInterference,
            severity,
            confidence,
            $"Stabile schmalbandige Spitze bei ca. {FormatFrequency(frequencyHz)}",
            $"Die Linie bleibt in {evidence.Persistence * 100d:0}% der aktiven Fenster am selben Frequenzpunkt und liegt dort im Mittel {evidence.MeanExcessDb:0.0} dB über der direkten Umgebung.{(sensitiveMidrange ? " Der musikalisch sensible Mittenbereich wurde extra streng bewertet." : string.Empty)}",
            $"Notch zunächst schmal bei {FormatFrequency(frequencyHz)} setzen. Vor dem Anwenden unbedingt „Störung vorhören“.",
            FrequencyHz: frequencyHz,
            SuggestedWidthPercent: width,
            Persistence: evidence.Persistence,
            SuggestedAuditionTimeSeconds: evidence.StrongestTimeSeconds));
    }

    private static void AddSibilanceFinding(
        ICollection<StemRepairFinding> findings,
        string trackName,
        IReadOnlyList<TimedValue> frames)
    {
        if (frames.Count < 6)
        {
            return;
        }

        var values = frames.Select(item => item.Value).OrderBy(value => value).ToArray();
        var highPercentile = Percentile(values, 0.88);
        var vocalHint =
            trackName.Contains("vocal", StringComparison.OrdinalIgnoreCase) ||
            trackName.Contains("vox", StringComparison.OrdinalIgnoreCase) ||
            trackName.Contains("stimme", StringComparison.OrdinalIgnoreCase) ||
            trackName.Contains("voice", StringComparison.OrdinalIgnoreCase);

        var threshold = vocalHint ? -3.5d : -1.5d;
        var active = frames.Where(item => item.Value >= threshold).ToArray();
        var persistence = active.Length / (double)frames.Count;
        if (highPercentile < threshold || persistence < (vocalHint ? 0.08d : 0.14d))
        {
            return;
        }

        var confidenceBase = vocalHint ? 0.62d : 0.42d;
        var confidence = Math.Clamp(
            confidenceBase + persistence * 0.55d + (highPercentile - threshold) / 20d,
            confidenceBase,
            0.94d);
        var severity = highPercentile > 3d && persistence >= 0.15d
            ? StemRepairFindingSeverity.Medium
            : StemRepairFindingSeverity.Low;
        var strength = Math.Clamp(25d + (highPercentile - threshold) * 4.5d, 25d, 58d);
        var strongest = frames.OrderByDescending(item => item.Value).First();

        findings.Add(new StemRepairFinding(
            StemRepairFindingType.Sibilance,
            severity,
            confidence,
            "Wiederkehrende Energie im Zischlautbereich",
            $"Auffällige 5–10-kHz-Momente treten in {persistence * 100d:0}% der aktiven Analysefenster auf{(vocalHint ? "; der Spurname deutet zusätzlich auf Gesang hin" : string.Empty)}.",
            $"De-Esser um ca. 7,0 kHz mit zunächst {strength:0}% Stärke ausprobieren und den entfernten Anteil vorhören.",
            FrequencyHz: 7_000d,
            SuggestedStrength: strength,
            Persistence: persistence,
            SuggestedAuditionTimeSeconds: strongest.TimeSeconds));
    }

    private static TimedHumEvidence EvaluateHumFrame(
        IReadOnlyList<double> spectrumDb,
        double fundamental,
        double timeSeconds)
    {
        var binWidth = TargetSampleRate / (double)FftSize;
        var excesses = new List<double>();
        var harmonics = 0;

        for (var harmonic = 1; harmonic <= 8; harmonic++)
        {
            var frequency = fundamental * harmonic;
            if (frequency >= 700d)
            {
                break;
            }

            var bin = (int)Math.Round(frequency / binWidth);
            if (bin < 0 || bin >= spectrumDb.Count)
            {
                continue;
            }

            var target = Enumerable.Range(Math.Max(0, bin - 1), Math.Min(spectrumDb.Count - Math.Max(0, bin - 1), 3))
                .Select(index => spectrumDb[index])
                .DefaultIfEmpty(-120d)
                .Max();

            var neighborhood = new List<double>();
            for (var offset = -10; offset <= 10; offset++)
            {
                if (Math.Abs(offset) <= 2)
                {
                    continue;
                }

                var candidate = bin + offset;
                if (candidate >= 0 && candidate < spectrumDb.Count)
                {
                    neighborhood.Add(spectrumDb[candidate]);
                }
            }

            if (neighborhood.Count == 0)
            {
                continue;
            }

            var excess = target - Median(neighborhood.ToArray());
            if (excess >= 4d)
            {
                harmonics++;
                excesses.Add(excess);
            }
        }

        return new TimedHumEvidence(
            timeSeconds,
            harmonics,
            excesses.Count == 0 ? 0d : excesses.Average());
    }

    private static HumPersistenceSummary SummarizeHumPersistence(
        IReadOnlyList<TimedHumEvidence> frames)
    {
        if (frames.Count == 0)
        {
            return HumPersistenceSummary.Empty;
        }

        var positive = frames.Where(item => item.Harmonics >= 2 && item.ScoreDb >= 4d).ToArray();
        if (positive.Length == 0)
        {
            return HumPersistenceSummary.Empty;
        }

        var persistence = positive.Length / (double)frames.Count;
        var score = positive.Average(item => item.ScoreDb);
        var harmonics = (int)Math.Round(positive.Average(item => item.Harmonics));
        var strongest = positive.OrderByDescending(item => item.ScoreDb).First();
        return new HumPersistenceSummary(
            score,
            persistence,
            Math.Clamp(harmonics, 2, 8),
            strongest.TimeSeconds);
    }

    private static NarrowbandPersistenceSummary MeasureNarrowbandPersistence(
        IReadOnlyList<RenderedAnalysisWindow> windows,
        int targetBin,
        double noiseFloorDbfs,
        CancellationToken cancellationToken)
    {
        var activeFrames = 0;
        var matchingFrames = 0;
        var matchingExcesses = new List<double>();
        var strongestTime = 0d;
        var strongestExcess = double.MinValue;

        foreach (var window in windows)
        {
            for (var offset = 0; offset + FftSize <= window.Samples.Length; offset += HopSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = new float[FftSize];
                Array.Copy(window.Samples, offset, frame, 0, FftSize);
                if (RmsDb(frame) <= Math.Max(-85d, noiseFloorDbfs + 4d))
                {
                    continue;
                }

                var spectrum = SpectrumAnalyzer.Analyze(frame, TargetSampleRate, FftSize, -120);
                if (!spectrum.HasData || targetBin >= spectrum.MagnitudesDb.Length)
                {
                    continue;
                }

                activeFrames++;
                var excess = LocalPeakExcess(spectrum.MagnitudesDb, targetBin, 10, 2);
                if (excess >= 10d)
                {
                    matchingFrames++;
                    matchingExcesses.Add(excess);
                    if (excess > strongestExcess)
                    {
                        strongestExcess = excess;
                        strongestTime = window.StartSeconds + (offset + FftSize / 2d) / TargetSampleRate;
                    }
                }
            }
        }

        return new NarrowbandPersistenceSummary(
            activeFrames == 0 ? 0d : matchingFrames / (double)activeFrames,
            matchingExcesses.Count == 0 ? 0d : matchingExcesses.Average(),
            strongestTime);
    }

    private static double LocalPeakExcess(
        IReadOnlyList<double> spectrumDb,
        int bin,
        int radius,
        int excludedRadius)
    {
        if (bin < 0 || bin >= spectrumDb.Count)
        {
            return double.MinValue;
        }

        var neighborhood = new List<double>();
        for (var offset = -radius; offset <= radius; offset++)
        {
            if (Math.Abs(offset) <= excludedRadius)
            {
                continue;
            }

            var candidate = bin + offset;
            if (candidate >= 0 && candidate < spectrumDb.Count)
            {
                neighborhood.Add(spectrumDb[candidate]);
            }
        }

        return neighborhood.Count == 0
            ? double.MinValue
            : spectrumDb[bin] - Median(neighborhood.ToArray());
    }

    private static bool IsNearHarmonic(double frequency, double fundamental, double toleranceHz)
    {
        var harmonic = Math.Round(frequency / fundamental);
        return harmonic >= 1 && harmonic <= 12 &&
               Math.Abs(frequency - harmonic * fundamental) <= toleranceHz;
    }

    private static double BandPower(SpectrumFrame spectrum, double minimumHz, double maximumHz)
    {
        var start = Math.Clamp(
            (int)Math.Ceiling(minimumHz * spectrum.FftSize / spectrum.SampleRate),
            0,
            spectrum.MagnitudesDb.Length - 1);
        var end = Math.Clamp(
            (int)Math.Floor(maximumHz * spectrum.FftSize / spectrum.SampleRate),
            start,
            spectrum.MagnitudesDb.Length - 1);

        var sum = 0d;
        for (var bin = start; bin <= end; bin++)
        {
            sum += Math.Pow(10d, spectrum.MagnitudesDb[bin] / 10d);
        }

        return sum / Math.Max(1, end - start + 1);
    }

    private static IReadOnlyList<(double StartSeconds, double DurationSeconds)> BuildAnalysisWindows(double durationSeconds)
    {
        var windowDuration = Math.Min(LongTrackWindowSeconds, durationSeconds);

        if (durationSeconds <= MaximumContinuousAnalysisSeconds)
        {
            var sequential = new List<(double StartSeconds, double DurationSeconds)>();
            for (var start = 0d; start < durationSeconds; start += LongTrackWindowSeconds)
            {
                sequential.Add(
                    (start, Math.Min(LongTrackWindowSeconds, durationSeconds - start)));
            }

            return sequential;
        }

        var count = MaximumLongTrackWindows;
        var windows = new List<(double StartSeconds, double DurationSeconds)>(count);
        for (var index = 0; index < count; index++)
        {
            var normalized = count == 1 ? 0d : index / (double)(count - 1);
            var start = normalized * Math.Max(0d, durationSeconds - windowDuration);
            windows.Add((start, windowDuration));
        }

        return windows;
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

    private static IReadOnlyList<double> CalculateRmsBlocks(float[] samples, int sampleRate)
    {
        var blockSize = Math.Max(64, sampleRate / 10);
        var values = new List<double>();

        for (var start = 0; start < samples.Length; start += blockSize)
        {
            var count = Math.Min(blockSize, samples.Length - start);
            double sum = 0;
            for (var index = 0; index < count; index++)
            {
                var sample = samples[start + index];
                sum += sample * sample;
            }

            var rms = Math.Sqrt(sum / Math.Max(1, count));
            values.Add(LinearToDb(rms));
        }

        return values;
    }

    private static double RmsDb(IReadOnlyList<float> samples)
    {
        double sum = 0;
        for (var index = 0; index < samples.Count; index++)
        {
            sum += samples[index] * samples[index];
        }

        return LinearToDb(Math.Sqrt(sum / Math.Max(1, samples.Count)));
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return -120d;
        }

        var position = Math.Clamp(percentile, 0d, 1d) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sortedValues.Count - 1, lower + 1);
        var fraction = position - lower;
        return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return -120d;
        }

        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) * 0.5d
            : values[middle];
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

    private sealed record RenderedAnalysisWindow(double StartSeconds, float[] Samples);

    private readonly record struct TimedValue(double TimeSeconds, double Value);

    private readonly record struct TimedHumEvidence(
        double TimeSeconds,
        int Harmonics,
        double ScoreDb);

    private readonly record struct HumPersistenceSummary(
        double Score,
        double Persistence,
        int Harmonics,
        double StrongestTimeSeconds)
    {
        public static HumPersistenceSummary Empty { get; } = new(0d, 0d, 0, 0d);
    }

    private readonly record struct NarrowbandPersistenceSummary(
        double Persistence,
        double MeanExcessDb,
        double StrongestTimeSeconds);

    private static double DbToLinear(double db) =>
        db <= -60 ? 0 : Math.Pow(10d, db / 20d);

    private static double LinearToDb(double value) =>
        value <= 1e-12 ? -120d : 20d * Math.Log10(value);

    private static string FormatFrequency(double frequencyHz) =>
        frequencyHz >= 1_000d
            ? $"{frequencyHz / 1_000d:0.##} kHz"
            : $"{frequencyHz:0} Hz";
}
