using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class MasteringAnalysisService
{
    private const int RenderBlockFrames = 2048;

    private readonly WavMetadataReader _metadataReader;
    private readonly WavSampleDecoder _decoder;

    public MasteringAnalysisService(WavMetadataReader metadataReader, WavSampleDecoder decoder)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    public async Task<MasteringAnalysisResult> AnalyzeAsync(
        AudioProjectDocument document,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        // The "removed noise only" switch is a monitoring aid, never part of a
        // project-wide loudness/mastering analysis.
        foreach (var track in document.Tracks)
        {
            track.Effects.NoiseReductionListenRemoved = false;
        }

        using var mixer = await ProjectAudioMixerSession.CreateAsync(
            document,
            _metadataReader,
            _decoder,
            cancellationToken,
            capturePreLimiterAnalysis: true);

        if (!mixer.HasPlayableClips || mixer.DurationFrames <= 0)
        {
            throw new InvalidOperationException("Das Projekt enthält keine abspielbaren WAV-Clips.");
        }

        return await Task.Run(
            () => AnalyzeCore(document, mixer, progress, cancellationToken),
            cancellationToken);
    }

    private static MasteringAnalysisResult AnalyzeCore(
        AudioProjectDocument document,
        ProjectAudioMixerSession mixer,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var renderFrame = 0L;
        var peakEventCandidates = new List<PeakContributionBlockSnapshot>();
        var preLimiterLevelHistory = new List<MasteringLevelHistoryPoint>();
        var levelHistory = new List<MasteringLevelHistoryPoint>();
        progress?.Report(0);

        while (renderFrame < mixer.DurationFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frameCount = checked((int)Math.Min(RenderBlockFrames, mixer.DurationFrames - renderFrame));
            var samples = mixer.Render(renderFrame, frameCount, cancellationToken);
            var peakBlock = mixer.GetLastPeakContributionBlock();
            if (peakBlock is not null)
            {
                peakEventCandidates.Add(peakBlock);
            }

            if (samples.Length == 0)
            {
                break;
            }

            var peakLeft = 0d;
            var peakRight = 0d;
            for (var sampleIndex = 0; sampleIndex + 1 < samples.Length; sampleIndex += 2)
            {
                peakLeft = Math.Max(peakLeft, Math.Abs(samples[sampleIndex]));
                peakRight = Math.Max(peakRight, Math.Abs(samples[sampleIndex + 1]));
            }

            var blockFrames = samples.Length / 2;
            var blockCenterSeconds = (renderFrame + blockFrames / 2d) / mixer.SampleRate;
            var preLimiterBlock = mixer.GetLastPreLimiterLevelBlock();
            preLimiterLevelHistory.Add(new MasteringLevelHistoryPoint(
                blockCenterSeconds,
                preLimiterBlock.LeftPeakDbfs,
                preLimiterBlock.RightPeakDbfs));
            levelHistory.Add(new MasteringLevelHistoryPoint(
                blockCenterSeconds,
                LinearPeakToDbfs(peakLeft),
                LinearPeakToDbfs(peakRight)));

            renderFrame += blockFrames;
            progress?.Report(Math.Clamp(renderFrame / (double)mixer.DurationFrames, 0, 1));
        }

        mixer.CompleteMasterAnalysis();
        var snapshot = mixer.GetMasterLoudnessSnapshot();
        var preLimiter = mixer.GetPreLimiterLoudnessSnapshot();
        if (!snapshot.HasData || snapshot.IntegratedLufs is null ||
            !preLimiter.HasData || preLimiter.IntegratedLufs is null)
        {
            throw new InvalidOperationException("Für den Mix konnte keine belastbare Integrated-Loudness bestimmt werden.");
        }

        var trackSnapshots = mixer.GetTrackLoudnessSnapshots();
        var trackAnalyses = document.Tracks
            .Select(track =>
            {
                if (!trackSnapshots.TryGetValue(track.Id, out var trackSnapshot) || !trackSnapshot.HasData)
                {
                    return null;
                }

                return new MasteringTrackAnalysisResult(
                    track.Id,
                    track.Name,
                    trackSnapshot.IntegratedLufs,
                    trackSnapshot.TruePeakLeftDbtp,
                    trackSnapshot.TruePeakRightDbtp);
            })
            .Where(result => result is not null)
            .Cast<MasteringTrackAnalysisResult>()
            .OrderByDescending(result => result.TruePeakMaxDbtp)
            .ToArray();

        var peakEvents = BuildPeakEvents(document, peakEventCandidates);
        var peakWarningEvents = BuildPeakWarningEvents(peakEventCandidates, mixer.SampleRate);
        var limiterReduction = mixer.GetMasterLimiterReductionSnapshot();

        progress?.Report(1);
        return new MasteringAnalysisResult(
            snapshot.IntegratedLufs.Value,
            snapshot.MaxShortTermLufs,
            snapshot.MaxMomentaryLufs,
            snapshot.TruePeakLeftDbtp,
            snapshot.TruePeakRightDbtp,
            preLimiter.IntegratedLufs.Value,
            preLimiter.TruePeakLeftDbtp,
            preLimiter.TruePeakRightDbtp,
            mixer.DurationSeconds,
            limiterReduction,
            trackAnalyses,
            peakEvents,
            peakWarningEvents,
            preLimiterLevelHistory,
            levelHistory);
    }

    private static double LinearPeakToDbfs(double linear)
    {
        return linear <= 1e-12 ? -120d : Math.Max(-120d, 20d * Math.Log10(linear));
    }

    private static IReadOnlyList<MasteringPeakWarningEventResult> BuildPeakWarningEvents(
        IReadOnlyList<PeakContributionBlockSnapshot> candidates,
        int sampleRate)
    {
        if (candidates.Count == 0 || sampleRate <= 0)
        {
            return [];
        }

        const double warningThresholdDbtp = -3.0;
        const double criticalThresholdDbtp = 0.0;
        const double sustainedThresholdSeconds = 0.30;
        var blockDurationSeconds = RenderBlockFrames / (double)sampleRate;
        var maximumGapSeconds = Math.Max(0.10, blockDurationSeconds * 2.75);

        var ordered = candidates
            .Where(item => item.ApproxTruePeakDbtp >= warningThresholdDbtp)
            .OrderBy(item => item.TimeSeconds)
            .ToArray();

        if (ordered.Length == 0)
        {
            return [];
        }

        var groups = new List<List<PeakContributionBlockSnapshot>>();
        var current = new List<PeakContributionBlockSnapshot> { ordered[0] };

        for (var index = 1; index < ordered.Length; index++)
        {
            var candidate = ordered[index];
            var previous = current[^1];
            if (candidate.TimeSeconds - previous.TimeSeconds <= maximumGapSeconds)
            {
                current.Add(candidate);
                continue;
            }

            groups.Add(current);
            current = new List<PeakContributionBlockSnapshot> { candidate };
        }

        groups.Add(current);

        return groups
            .Select(group =>
            {
                var start = Math.Max(0, group[0].TimeSeconds - blockDurationSeconds / 2.0);
                var end = group[^1].TimeSeconds + blockDurationSeconds / 2.0;
                var maxPeak = group.Max(item => item.ApproxTruePeakDbtp);
                var critical = maxPeak >= criticalThresholdDbtp;
                var duration = Math.Max(blockDurationSeconds, end - start);
                var sustained = critical && duration >= sustainedThresholdSeconds;

                return new MasteringPeakWarningEventResult(
                    start,
                    end,
                    maxPeak,
                    critical,
                    sustained);
            })
            .ToArray();
    }

    private static IReadOnlyList<MasteringPeakEventResult> BuildPeakEvents(
        AudioProjectDocument document,
        IReadOnlyList<PeakContributionBlockSnapshot> candidates)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var trackNames = document.Tracks.ToDictionary(track => track.Id, track => track.Name);
        const double minimumEventDistanceSeconds = 0.50;
        const int maximumEvents = 6;

        var selected = new List<PeakContributionBlockSnapshot>(maximumEvents);
        foreach (var candidate in candidates.OrderByDescending(item => item.ApproxTruePeakDbtp))
        {
            if (selected.Any(existing =>
                    Math.Abs(existing.TimeSeconds - candidate.TimeSeconds) < minimumEventDistanceSeconds))
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count >= maximumEvents)
            {
                break;
            }
        }

        return selected
            .OrderBy(item => item.TimeSeconds)
            .Select(candidate =>
            {
                var orderedTracks = candidate.Tracks
                    .Where(track => track.PeakLinear > 1e-7)
                    .OrderByDescending(track => track.PeakLinear)
                    .ToArray();
                var contributionSum = orderedTracks.Sum(track => track.PeakLinear);

                var contributions = orderedTracks
                    .Select(track => new MasteringPeakEventContribution(
                        track.TrackId,
                        trackNames.TryGetValue(track.TrackId, out var name) ? name : "Unbekannte Spur",
                        LinearToDb(track.PeakLinear),
                        contributionSum <= 1e-12 ? 0 : track.PeakLinear / contributionSum * 100.0))
                    .ToArray();

                return new MasteringPeakEventResult(
                    candidate.TimeSeconds,
                    candidate.ApproxTruePeakDbtp,
                    contributions);
            })
            .ToArray();
    }

    private static double LinearToDb(double linear)
    {
        if (linear <= 1e-6)
        {
            return -120;
        }

        return Math.Max(-120, 20.0 * Math.Log10(linear));
    }

}
