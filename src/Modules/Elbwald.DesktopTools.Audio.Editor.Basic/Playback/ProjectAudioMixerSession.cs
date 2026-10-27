using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

/// <summary>
/// Playback snapshot of the structural project data with live mixer state.
/// Clip/source topology is immutable for one playback session while track
/// volume, pan, mute, solo and master volume can be changed during playback.
/// Rendering happens off the realtime device callback.
/// </summary>
internal readonly record struct PeakContributionBlockTrack(Guid TrackId, double PeakLinear);

internal sealed record PeakContributionBlockSnapshot(
    double TimeSeconds,
    double ApproxTruePeakDbtp,
    IReadOnlyList<PeakContributionBlockTrack> Tracks);

internal readonly record struct MasterPreLimiterLevelBlockSnapshot(
    double LeftPeakDbfs,
    double RightPeakDbfs);

internal sealed class ProjectAudioMixerSession : IDisposable
{
    private const double MeterFloorDb = -60;

    private readonly WavSampleDecoder _decoder;
    private readonly List<PlaybackTrack> _tracks;
    private readonly Dictionary<Guid, PlaybackTrack> _tracksById;
    private readonly Dictionary<string, PlaybackSource> _sources;
    private readonly LiveMasterMixerState _masterMixer;
    private readonly LiveMasterEqualizerState _masterEqualizerState;
    private readonly TrackEqualizerProcessor _masterEqualizer = new();
    private readonly MasterTruePeakLimiter _masterLimiter = new();
    private readonly MasterLoudnessMeter? _masterPreLimiterLoudnessMeter;
    private readonly MasterLoudnessMeter _masterLoudnessMeter;
    private readonly LiveSpectrumCapture _spectrumCapture = new();
    private PeakContributionBlockSnapshot? _lastPeakContributionBlock;
    private MasterPreLimiterLevelBlockSnapshot _lastPreLimiterLevelBlock = new(-120d, -120d);

    private ProjectAudioMixerSession(
        int sampleRate,
        long durationFrames,
        WavSampleDecoder decoder,
        List<PlaybackTrack> tracks,
        Dictionary<string, PlaybackSource> sources,
        double masterVolumeDb,
        bool masterLimiterEnabled,
        double masterLimiterCeilingDb,
        AudioMasterEqualizerDocument masterEqualizer,
        bool capturePreLimiterAnalysis)
    {
        SampleRate = sampleRate;
        DurationFrames = durationFrames;
        _decoder = decoder;
        _tracks = tracks;
        _tracksById = tracks.ToDictionary(track => track.Id);
        _sources = sources;
        _masterMixer = new LiveMasterMixerState(masterVolumeDb, masterLimiterEnabled, masterLimiterCeilingDb);
        _masterEqualizerState = new LiveMasterEqualizerState(masterEqualizer);
        _masterPreLimiterLoudnessMeter = capturePreLimiterAnalysis ? new MasterLoudnessMeter(sampleRate) : null;
        _masterLoudnessMeter = new MasterLoudnessMeter(sampleRate);
    }

    public int SampleRate { get; }

    public long DurationFrames { get; }

    public double DurationSeconds => DurationFrames / (double)SampleRate;

    public bool HasPlayableClips => _tracks.Any(track => track.Clips.Any(clip => clip.Source is not null));

    public static async Task<ProjectAudioMixerSession> CreateAsync(
        AudioProjectDocument document,
        WavMetadataReader metadataReader,
        WavSampleDecoder decoder,
        CancellationToken cancellationToken = default,
        bool capturePreLimiterAnalysis = false,
        double additionalTailSeconds = 0d)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(decoder);

        if (document.SampleRate is < 8_000 or > 384_000)
        {
            throw new InvalidDataException($"Ungültige Projekt-Samplerate: {document.SampleRate} Hz.");
        }

        var sourcePaths = document.Tracks
            .SelectMany(track => track.Clips)
            .Select(clip => clip.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sources = new Dictionary<string, PlaybackSource>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = await metadataReader.ReadAsync(sourcePath, cancellationToken);
                sources[sourcePath] = new PlaybackSource(info);
            }

            var tracks = new List<PlaybackTrack>(document.Tracks.Count);
            foreach (var track in document.Tracks)
            {
                var clips = new List<PlaybackClip>(track.Clips.Count);
                foreach (var clip in track.Clips)
                {
                    PlaybackSource? source = null;
                    if (!string.IsNullOrWhiteSpace(clip.SourcePath))
                    {
                        sources.TryGetValue(Path.GetFullPath(clip.SourcePath), out source);
                    }

                    clips.Add(new PlaybackClip(
                        clip.TimelineStartSeconds,
                        clip.SourceStartSeconds,
                        clip.DurationSeconds,
                        clip.GainDb,
                        clip.FadeInSeconds,
                        clip.FadeOutSeconds,
                        source));
                }

                tracks.Add(new PlaybackTrack(
                    track.Id,
                    track.IsMuted,
                    track.IsSolo,
                    track.VolumeDb,
                    track.Pan,
                    track.Effects,
                    clips,
                    document.SampleRate,
                    capturePreLimiterAnalysis));
            }

            var durationSeconds = document.Tracks
                .SelectMany(track => track.Clips)
                .Select(clip => Math.Max(0, clip.TimelineStartSeconds) + Math.Max(0, clip.DurationSeconds))
                .DefaultIfEmpty(0)
                .Max();
            durationSeconds += Math.Clamp(additionalTailSeconds, 0d, 30d);
            var durationFrames = checked((long)Math.Ceiling(durationSeconds * document.SampleRate));

            return new ProjectAudioMixerSession(
                document.SampleRate,
                durationFrames,
                decoder,
                tracks,
                sources,
                document.Master.VolumeDb,
                document.Master.LimiterEnabled,
                document.Master.LimiterCeilingDb,
                document.Master.Equalizer,
                capturePreLimiterAnalysis);
        }
        catch
        {
            foreach (var source in sources.Values)
            {
                source.Dispose();
            }

            throw;
        }
    }

    public void UpdateTrackMixer(Guid trackId, bool isMuted, bool isSolo, double volumeDb, double pan)
    {
        if (_tracksById.TryGetValue(trackId, out var track))
        {
            track.Mixer.Update(isMuted, isSolo, volumeDb, pan);
        }
    }

    public void UpdateTrackEffects(Guid trackId, AudioTrackEffectsDocument effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        if (_tracksById.TryGetValue(trackId, out var track))
        {
            track.Effects.Update(effects);
        }
    }

    public void UpdateMasterMixer(double volumeDb, bool limiterEnabled, double limiterCeilingDb) =>
        _masterMixer.Update(volumeDb, limiterEnabled, limiterCeilingDb);

    public void UpdateMasterEqualizer(AudioMasterEqualizerDocument equalizer) =>
        _masterEqualizerState.Update(equalizer);

    public MasterLoudnessSnapshot GetMasterLoudnessSnapshot() =>
        _masterLoudnessMeter.GetSnapshot();

    public void ResetMasterLoudnessAnalysis()
    {
        _masterPreLimiterLoudnessMeter?.Reset();
        _masterLoudnessMeter.Reset();
    }

    public MasterLoudnessSnapshot GetPreLimiterLoudnessSnapshot() =>
        _masterPreLimiterLoudnessMeter?.GetSnapshot() ?? MasterLoudnessSnapshot.Empty;

    public MasterLimiterReductionSnapshot GetMasterLimiterReductionSnapshot() =>
        _masterLimiter.GetReductionSnapshot();

    public PeakContributionBlockSnapshot? GetLastPeakContributionBlock() =>
        _lastPeakContributionBlock;

    public MasterPreLimiterLevelBlockSnapshot GetLastPreLimiterLevelBlock() =>
        _lastPreLimiterLevelBlock;

    public IReadOnlyDictionary<Guid, MasterLoudnessSnapshot> GetTrackLoudnessSnapshots()
    {
        var snapshots = new Dictionary<Guid, MasterLoudnessSnapshot>(_tracks.Count);
        foreach (var track in _tracks)
        {
            if (track.AnalysisMeter is not null)
            {
                snapshots[track.Id] = track.AnalysisMeter.GetSnapshot();
            }
        }

        return snapshots;
    }

    public void CompleteMasterAnalysis()
    {
        foreach (var track in _tracks)
        {
            track.AnalysisMeter?.Complete();
        }

        _masterPreLimiterLoudnessMeter?.Complete();
        _masterLoudnessMeter.Complete();
    }

    public void SetSpectrumTarget(Guid? trackId) =>
        _spectrumCapture.SetTarget(trackId, SampleRate);

    public AudioSpectrumSampleSnapshot GetSpectrumSampleSnapshot() =>
        _spectrumCapture.GetSnapshot();

    public void ResetProcessingState()
    {
        foreach (var track in _tracks)
        {
            track.NoiseReducer.Reset();
            track.BasicFx.Reset();
            track.Equalizer.Reset();
            track.MusicalFx.Reset();
            track.Dynamics.Reset();
            track.AnalysisMeter?.Reset();
        }

        _masterEqualizer.Reset();
        _masterLimiter.Reset();
        _masterPreLimiterLoudnessMeter?.Reset();
        _masterLoudnessMeter.Reset();
    }

    public AudioMeterSnapshot GetMeterSnapshot()
    {
        var meters = new Dictionary<Guid, AudioStereoMeter>(_tracks.Count);
        foreach (var track in _tracks)
        {
            meters[track.Id] = track.Mixer.GetMeter();
        }

        return new AudioMeterSnapshot(_masterMixer.GetMeter(), meters);
    }

    public float[] Render(long projectStartFrame, int frameCount, CancellationToken cancellationToken)
    {
        if (projectStartFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(projectStartFrame));
        }

        if (frameCount <= 0)
        {
            return [];
        }

        var remainingFrames = Math.Max(0, DurationFrames - projectStartFrame);
        var actualFrames = checked((int)Math.Min(frameCount, remainingFrames));
        if (actualFrames <= 0)
        {
            return [];
        }

        var output = new float[checked(actualFrames * 2)];
        _lastPeakContributionBlock = null;
        var trackBuffer = new float[output.Length];
        var mixerSnapshots = new TrackMixerSnapshot[_tracks.Count];
        var hasSolo = false;

        for (var index = 0; index < _tracks.Count; index++)
        {
            var snapshot = _tracks[index].Mixer.GetSnapshot();
            mixerSnapshots[index] = snapshot;
            hasSolo |= snapshot.IsSolo;
        }

        for (var trackIndex = 0; trackIndex < _tracks.Count; trackIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var track = _tracks[trackIndex];
            var mixer = mixerSnapshots[trackIndex];
            if (mixer.IsMuted || (hasSolo && !mixer.IsSolo))
            {
                track.NoiseReducer.Reset();
                track.BasicFx.Reset();
                track.Equalizer.Reset();
                track.MusicalFx.Reset();
                track.Dynamics.Reset();
                track.Mixer.SetMeter(AudioStereoMeter.Silence);
                track.CaptureAnalysisBlock(null, output.Length);
                _spectrumCapture.CaptureSilence(track.Id, actualFrames);
                continue;
            }

            Array.Clear(trackBuffer, 0, trackBuffer.Length);
            foreach (var clip in track.Clips)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clip.Source is null || clip.DurationSeconds <= 0)
                {
                    continue;
                }

                MixClip(trackBuffer, actualFrames, projectStartFrame, clip, cancellationToken);
            }

            track.NoiseReducer.Process(trackBuffer, SampleRate, track.Effects.GetNoiseReductionSnapshot());
            track.BasicFx.ProcessPreEq(trackBuffer, SampleRate, track.Effects.GetBasicFxSnapshot());

            if (_spectrumCapture.IsTarget(track.Id))
            {
                _spectrumCapture.CapturePreEq(track.Id, trackBuffer);
            }

            track.Equalizer.Process(trackBuffer, SampleRate, track.Effects.GetEqualizerSnapshot());

            if (_spectrumCapture.IsTarget(track.Id))
            {
                _spectrumCapture.CapturePostEq(track.Id, trackBuffer);
            }

            track.BasicFx.ProcessPostEq(trackBuffer, SampleRate, track.Effects.GetBasicFxSnapshot());
            var musicalFxSettings = track.Effects.GetMusicalFxSnapshot();
            track.MusicalFx.ProcessChannelTools(trackBuffer, musicalFxSettings);
            track.MusicalFx.ProcessSaturation(trackBuffer, musicalFxSettings);
            var dynamicsSettings = track.Effects.GetDynamicsSnapshot();
            track.Dynamics.ProcessCompressor(trackBuffer, SampleRate, dynamicsSettings);
            track.MusicalFx.ProcessChorus(trackBuffer, SampleRate, musicalFxSettings);
            track.MusicalFx.ProcessStereoWidth(trackBuffer, musicalFxSettings);
            track.MusicalFx.ProcessDelay(trackBuffer, SampleRate, musicalFxSettings);
            track.MusicalFx.ProcessReverb(trackBuffer, SampleRate, musicalFxSettings);
            track.Dynamics.ProcessLimiter(trackBuffer, dynamicsSettings);
            ApplyTrackMixer(output, trackBuffer, mixer, track);
            track.CaptureAnalysisBlock(trackBuffer, trackBuffer.Length);
        }

        ApplyMasterMixer(output, projectStartFrame);
        return output;
    }

    private static void ApplyTrackMixer(
        float[] output,
        float[] trackBuffer,
        TrackMixerSnapshot mixer,
        PlaybackTrack track)
    {
        var volume = DbToLinear(mixer.VolumeDb);
        var normalizedPan = Math.Clamp(mixer.Pan / 100d, -1d, 1d);

        // Balance-style panning keeps stereo material at unity in the centre:
        // the opposite side is attenuated while the target side remains at 1.0.
        var leftPan = normalizedPan > 0 ? 1d - normalizedPan : 1d;
        var rightPan = normalizedPan < 0 ? 1d + normalizedPan : 1d;
        var leftGain = (float)(volume * leftPan);
        var rightGain = (float)(volume * rightPan);

        var peakLeft = 0f;
        var peakRight = 0f;

        for (var index = 0; index < trackBuffer.Length; index += 2)
        {
            var left = trackBuffer[index] * leftGain;
            var right = trackBuffer[index + 1] * rightGain;

            // Keep the post-track-mixer signal in the temporary buffer too.
            // The offline analysis meter can then inspect the exact signal
            // that contributes to the master sum without another render pass.
            trackBuffer[index] = left;
            trackBuffer[index + 1] = right;

            output[index] += left;
            output[index + 1] += right;

            peakLeft = Math.Max(peakLeft, Math.Abs(left));
            peakRight = Math.Max(peakRight, Math.Abs(right));
        }

        track.Mixer.SetMeter(new AudioStereoMeter(LinearToDb(peakLeft), LinearToDb(peakRight)));
        track.AnalysisMeter?.Process(trackBuffer);
    }

    private void ApplyMasterMixer(float[] output, long projectStartFrame)
    {
        var master = _masterMixer.GetSnapshot();
        var masterVolume = DbToLinear(master.VolumeDb);

        for (var index = 0; index < output.Length; index++)
        {
            output[index] *= masterVolume;
        }

        if (_spectrumCapture.IsTarget(Guid.Empty))
        {
            _spectrumCapture.CapturePreEq(Guid.Empty, output);
        }

        _masterEqualizer.Process(output, SampleRate, _masterEqualizerState.GetSnapshot());

        if (_spectrumCapture.IsTarget(Guid.Empty))
        {
            _spectrumCapture.CapturePostEq(Guid.Empty, output);
        }

        var preLimiterPeakLeft = 0f;
        var preLimiterPeakRight = 0f;
        for (var index = 0; index < output.Length; index += 2)
        {
            preLimiterPeakLeft = Math.Max(preLimiterPeakLeft, Math.Abs(output[index]));
            preLimiterPeakRight = Math.Max(preLimiterPeakRight, Math.Abs(output[index + 1]));
        }

        _lastPreLimiterLevelBlock = new MasterPreLimiterLevelBlockSnapshot(
            LinearToAnalysisDb(preLimiterPeakLeft),
            LinearToAnalysisDb(preLimiterPeakRight));

        _masterPreLimiterLoudnessMeter?.Process(output);
        CapturePeakContributionBlock(output, projectStartFrame, masterVolume);

        _masterLimiter.Process(
            output,
            SampleRate,
            master.LimiterEnabled,
            master.LimiterCeilingDb);

        var peakLeft = 0f;
        var peakRight = 0f;
        for (var index = 0; index < output.Length; index += 2)
        {
            peakLeft = Math.Max(peakLeft, Math.Abs(output[index]));
            peakRight = Math.Max(peakRight, Math.Abs(output[index + 1]));
        }

        _masterMixer.SetMeter(new AudioStereoMeter(LinearToDb(peakLeft), LinearToDb(peakRight)));
        _masterLoudnessMeter.Process(output);
    }

    private void CapturePeakContributionBlock(
        float[] preLimiterOutput,
        long projectStartFrame,
        float masterVolume)
    {
        if (_masterPreLimiterLoudnessMeter is null || preLimiterOutput.Length < 2)
        {
            return;
        }

        var peakFrame = 0;
        var samplePeak = 0f;
        for (var index = 0; index < preLimiterOutput.Length; index += 2)
        {
            var framePeak = Math.Max(Math.Abs(preLimiterOutput[index]), Math.Abs(preLimiterOutput[index + 1]));
            if (framePeak > samplePeak)
            {
                samplePeak = framePeak;
                peakFrame = index / 2;
            }
        }

        if (samplePeak <= 1e-7f)
        {
            return;
        }

        // Use the same 4x true-peak reconstruction as the loudness meter for
        // ranking the block. The timestamp itself is anchored to the nearest
        // real sample; this keeps contribution lookup deterministic.
        var detector = new TruePeakDetector();
        detector.Process(preLimiterOutput);
        detector.Complete();
        var approxTruePeak = LinearToAnalysisDb(Math.Max(detector.MaxLeftLinear, detector.MaxRightLinear));

        const int contributionRadiusFrames = 64;
        var firstFrame = Math.Max(0, peakFrame - contributionRadiusFrames);
        var lastFrame = Math.Min(preLimiterOutput.Length / 2 - 1, peakFrame + contributionRadiusFrames);
        var contributions = new List<PeakContributionBlockTrack>(_tracks.Count);

        foreach (var track in _tracks)
        {
            var buffer = track.AnalysisBlockBuffer;
            if (buffer is null || buffer.Length < preLimiterOutput.Length)
            {
                continue;
            }

            var trackPeak = 0f;
            for (var frame = firstFrame; frame <= lastFrame; frame++)
            {
                var sampleIndex = frame * 2;
                trackPeak = Math.Max(
                    trackPeak,
                    Math.Max(Math.Abs(buffer[sampleIndex]), Math.Abs(buffer[sampleIndex + 1])));
            }

            trackPeak *= Math.Abs(masterVolume);
            if (trackPeak > 1e-7f)
            {
                contributions.Add(new PeakContributionBlockTrack(track.Id, trackPeak));
            }
        }

        _lastPeakContributionBlock = new PeakContributionBlockSnapshot(
            (projectStartFrame + peakFrame) / (double)SampleRate,
            approxTruePeak,
            contributions);
    }

    private static double LinearToAnalysisDb(double linear)
    {
        if (linear <= 1e-6)
        {
            return -120;
        }

        return Math.Max(-120, 20.0 * Math.Log10(linear));
    }

    private void MixClip(
        float[] output,
        int outputFrames,
        long projectStartFrame,
        PlaybackClip clip,
        CancellationToken cancellationToken)
    {
        var projectEndFrame = projectStartFrame + outputFrames;
        var clipStartFrame = SecondsToFrame(clip.TimelineStartSeconds, SampleRate);
        var clipEndFrame = SecondsToFrame(clip.TimelineStartSeconds + clip.DurationSeconds, SampleRate);

        var overlapStart = Math.Max(projectStartFrame, clipStartFrame);
        var overlapEnd = Math.Min(projectEndFrame, clipEndFrame);
        if (overlapEnd <= overlapStart)
        {
            return;
        }

        var source = clip.Source!;
        var firstProjectOffset = overlapStart - clipStartFrame;
        var lastProjectOffset = overlapEnd - clipStartFrame;
        var firstSourcePosition = clip.SourceStartSeconds * source.Info.SampleRate +
                                  firstProjectOffset * (source.Info.SampleRate / (double)SampleRate);
        var lastSourcePosition = clip.SourceStartSeconds * source.Info.SampleRate +
                                 lastProjectOffset * (source.Info.SampleRate / (double)SampleRate);

        var sourceFrameStart = Math.Max(0, (long)Math.Floor(firstSourcePosition));
        var sourceFrameEnd = Math.Min(
            source.Info.SampleFrames,
            (long)Math.Ceiling(lastSourcePosition) + 2);
        var sourceFramesNeeded = checked((int)Math.Max(0, sourceFrameEnd - sourceFrameStart));
        if (sourceFramesNeeded <= 0)
        {
            return;
        }

        var decoded = _decoder.ReadFrames(
            source.Info,
            source.Stream,
            sourceFrameStart,
            sourceFramesNeeded,
            cancellationToken);
        if (decoded.FrameCount <= 0)
        {
            return;
        }

        var clipGain = DbToLinear(clip.GainDb);
        var outputStartIndex = checked((int)(overlapStart - projectStartFrame));
        var framesToMix = checked((int)(overlapEnd - overlapStart));

        for (var frame = 0; frame < framesToMix; frame++)
        {
            if ((frame & 0x0FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var absoluteProjectFrame = overlapStart + frame;
            var clipProjectFrame = absoluteProjectFrame - clipStartFrame;
            var clipSeconds = clipProjectFrame / (double)SampleRate;
            var sourcePosition = clip.SourceStartSeconds * source.Info.SampleRate +
                                 clipProjectFrame * (source.Info.SampleRate / (double)SampleRate);
            var relativeSource = sourcePosition - sourceFrameStart;
            var index0 = (int)Math.Floor(relativeSource);
            if (index0 < 0 || index0 >= decoded.FrameCount)
            {
                continue;
            }

            var index1 = Math.Min(index0 + 1, decoded.FrameCount - 1);
            var fraction = (float)(relativeSource - index0);
            var fade = CalculateFade(clip, clipSeconds);
            var gain = clipGain * fade;

            float left;
            float right;
            if (decoded.Channels == 1)
            {
                var sample0 = decoded.InterleavedSamples[index0];
                var sample1 = decoded.InterleavedSamples[index1];
                var sample = Lerp(sample0, sample1, fraction) * gain;
                left = sample;
                right = sample;
            }
            else
            {
                var sample0Index = index0 * 2;
                var sample1Index = index1 * 2;
                left = Lerp(
                    decoded.InterleavedSamples[sample0Index],
                    decoded.InterleavedSamples[sample1Index],
                    fraction) * gain;
                right = Lerp(
                    decoded.InterleavedSamples[sample0Index + 1],
                    decoded.InterleavedSamples[sample1Index + 1],
                    fraction) * gain;
            }

            var outputIndex = (outputStartIndex + frame) * 2;
            output[outputIndex] += left;
            output[outputIndex + 1] += right;
        }
    }

    private static float CalculateFade(PlaybackClip clip, double localSeconds)
    {
        var fade = 1d;
        if (clip.FadeInSeconds > 0 && localSeconds < clip.FadeInSeconds)
        {
            fade *= Math.Clamp(localSeconds / clip.FadeInSeconds, 0, 1);
        }

        if (clip.FadeOutSeconds > 0)
        {
            var remaining = clip.DurationSeconds - localSeconds;
            if (remaining < clip.FadeOutSeconds)
            {
                fade *= Math.Clamp(remaining / clip.FadeOutSeconds, 0, 1);
            }
        }

        return (float)fade;
    }

    private static long SecondsToFrame(double seconds, int sampleRate) =>
        checked((long)Math.Round(Math.Max(0, seconds) * sampleRate, MidpointRounding.AwayFromZero));

    private static float DbToLinear(double db) =>
        db <= -59.9 ? 0f : (float)Math.Pow(10, db / 20d);

    private static double LinearToDb(float linear)
    {
        if (linear <= 0.000_001f)
        {
            return MeterFloorDb;
        }

        return Math.Max(MeterFloorDb, 20d * Math.Log10(linear));
    }

    private static float Lerp(float a, float b, float amount) => a + (b - a) * amount;

    public void Dispose()
    {
        foreach (var source in _sources.Values)
        {
            source.Dispose();
        }

        _sources.Clear();
    }

    private sealed class PlaybackTrack
    {
        public PlaybackTrack(
            Guid id,
            bool isMuted,
            bool isSolo,
            double volumeDb,
            double pan,
            AudioTrackEffectsDocument effects,
            IReadOnlyList<PlaybackClip> clips,
            int sampleRate,
            bool captureAnalysis)
        {
            Id = id;
            Clips = clips;
            Mixer = new LiveTrackMixerState(isMuted, isSolo, volumeDb, pan);
            Effects = new LiveTrackEffectsState(effects);
            NoiseReducer = new SpectralNoiseReducer();
            BasicFx = new TrackBasicFxProcessor();
            Equalizer = new TrackEqualizerProcessor();
            MusicalFx = new TrackMusicalFxProcessor();
            Dynamics = new TrackDynamicsProcessor();
            AnalysisMeter = captureAnalysis ? new MasterLoudnessMeter(sampleRate) : null;
        }

        public Guid Id { get; }

        public IReadOnlyList<PlaybackClip> Clips { get; }

        public LiveTrackMixerState Mixer { get; }

        public LiveTrackEffectsState Effects { get; }

        public SpectralNoiseReducer NoiseReducer { get; }

        public TrackBasicFxProcessor BasicFx { get; }

        public TrackEqualizerProcessor Equalizer { get; }

        public TrackMusicalFxProcessor MusicalFx { get; }

        public TrackDynamicsProcessor Dynamics { get; }

        public MasterLoudnessMeter? AnalysisMeter { get; }

        public float[]? AnalysisBlockBuffer { get; private set; }

        public void CaptureAnalysisBlock(float[]? source, int requiredLength)
        {
            if (AnalysisMeter is null)
            {
                return;
            }

            if (AnalysisBlockBuffer is null || AnalysisBlockBuffer.Length < requiredLength)
            {
                AnalysisBlockBuffer = new float[requiredLength];
            }

            if (source is null)
            {
                Array.Clear(AnalysisBlockBuffer, 0, requiredLength);
                return;
            }

            Array.Copy(source, AnalysisBlockBuffer, requiredLength);
        }
    }

    private sealed record PlaybackClip(
        double TimelineStartSeconds,
        double SourceStartSeconds,
        double DurationSeconds,
        double GainDb,
        double FadeInSeconds,
        double FadeOutSeconds,
        PlaybackSource? Source);

    private readonly record struct TrackMixerSnapshot(
        bool IsMuted,
        bool IsSolo,
        double VolumeDb,
        double Pan);

    private sealed class LiveTrackMixerState
    {
        private readonly object _gate = new();
        private bool _isMuted;
        private bool _isSolo;
        private double _volumeDb;
        private double _pan;
        private AudioStereoMeter _meter = AudioStereoMeter.Silence;

        public LiveTrackMixerState(bool isMuted, bool isSolo, double volumeDb, double pan)
        {
            _isMuted = isMuted;
            _isSolo = isSolo;
            _volumeDb = Math.Clamp(volumeDb, -60, 12);
            _pan = Math.Clamp(pan, -100, 100);
        }

        public void Update(bool isMuted, bool isSolo, double volumeDb, double pan)
        {
            lock (_gate)
            {
                _isMuted = isMuted;
                _isSolo = isSolo;
                _volumeDb = Math.Clamp(volumeDb, -60, 12);
                _pan = Math.Clamp(pan, -100, 100);
            }
        }

        public TrackMixerSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new TrackMixerSnapshot(_isMuted, _isSolo, _volumeDb, _pan);
            }
        }

        public void SetMeter(AudioStereoMeter meter)
        {
            lock (_gate)
            {
                _meter = meter;
            }
        }

        public AudioStereoMeter GetMeter()
        {
            lock (_gate)
            {
                return _meter;
            }
        }
    }

    private sealed class LiveTrackEffectsState
    {
        private readonly object _gate = new();
        private TrackNoiseReductionSettings _noiseReductionSettings;
        private TrackBasicFxSettings _basicFxSettings;
        private TrackEqualizerSettings _equalizerSettings;
        private TrackMusicalFxSettings _musicalFxSettings;
        private TrackDynamicsSettings _dynamicsSettings;

        public LiveTrackEffectsState(AudioTrackEffectsDocument effects)
        {
            (_noiseReductionSettings, _basicFxSettings, _equalizerSettings, _musicalFxSettings, _dynamicsSettings) = CreateSnapshots(effects);
        }

        public void Update(AudioTrackEffectsDocument effects)
        {
            lock (_gate)
            {
                (_noiseReductionSettings, _basicFxSettings, _equalizerSettings, _musicalFxSettings, _dynamicsSettings) = CreateSnapshots(effects);
            }
        }

        public TrackNoiseReductionSettings GetNoiseReductionSnapshot()
        {
            lock (_gate)
            {
                return _noiseReductionSettings;
            }
        }

        public TrackBasicFxSettings GetBasicFxSnapshot()
        {
            lock (_gate)
            {
                return _basicFxSettings;
            }
        }

        public TrackEqualizerSettings GetEqualizerSnapshot()
        {
            lock (_gate)
            {
                return _equalizerSettings;
            }
        }

        public TrackMusicalFxSettings GetMusicalFxSnapshot()
        {
            lock (_gate)
            {
                return _musicalFxSettings;
            }
        }

        public TrackDynamicsSettings GetDynamicsSnapshot()
        {
            lock (_gate)
            {
                return _dynamicsSettings;
            }
        }

        private static (TrackNoiseReductionSettings NoiseReduction, TrackBasicFxSettings BasicFx, TrackEqualizerSettings Equalizer, TrackMusicalFxSettings MusicalFx, TrackDynamicsSettings Dynamics) CreateSnapshots(
            AudioTrackEffectsDocument effects)
        {
            var noiseReduction = new TrackNoiseReductionSettings(
                effects.NoiseReductionEnabled,
                Math.Clamp(effects.NoiseReductionStrength, 0, 100),
                effects.NoiseProfileSampleRate,
                effects.NoiseProfileFftSize,
                effects.NoiseProfileDb?.ToArray() ?? [],
                effects.NoiseReductionListenRemoved);

            var basicFx = new TrackBasicFxSettings(
                effects.ExpanderEnabled,
                Math.Clamp(effects.ExpanderThresholdDb, -80, -10),
                Math.Clamp(effects.ExpanderReductionDb, 0, 60),
                effects.NotchEnabled,
                Math.Clamp(effects.NotchFrequencyHz, 20, 20_000),
                Math.Clamp(effects.NotchWidthPercent, 0, 100),
                effects.HumRemovalEnabled,
                effects.HumFrequencyHz < 55 ? 50 : 60,
                Math.Clamp(effects.HumHarmonics, 1, 8),
                Math.Clamp(effects.HumReductionDb, 0, 30),
                effects.DeEsserEnabled,
                Math.Clamp(effects.DeEsserFrequencyHz, 3_000, 12_000),
                Math.Clamp(effects.DeEsserStrength, 0, 100));

            var equalizer = new TrackEqualizerSettings(
                effects.EqualizerEnabled,
                Math.Clamp(effects.BassDb, -12, 12),
                Math.Clamp(effects.MidDb, -12, 12),
                Math.Clamp(effects.MidFrequencyHz, 120, 8_000),
                Math.Clamp(effects.TrebleDb, -12, 12),
                effects.HighPassEnabled,
                Math.Clamp(effects.HighPassFrequencyHz, 20, 600),
                effects.LowPassEnabled,
                Math.Clamp(effects.LowPassFrequencyHz, 2_000, 20_000));

            var musicalFx = new TrackMusicalFxSettings(
                Math.Clamp(effects.ChannelMode, 0, 4),
                effects.PhaseInvertLeft,
                effects.PhaseInvertRight,
                effects.SaturationEnabled,
                Math.Clamp(effects.SaturationDriveDb, 0, 18),
                Math.Clamp(effects.SaturationMixPercent, 0, 100),
                effects.ChorusEnabled,
                Math.Clamp(effects.ChorusRateHz, 0.05, 3),
                Math.Clamp(effects.ChorusDepthPercent, 0, 100),
                Math.Clamp(effects.ChorusMixPercent, 0, 100),
                effects.StereoWidthEnabled,
                Math.Clamp(effects.StereoWidthPercent, 0, 200),
                effects.DelayEnabled,
                Math.Clamp(effects.DelayTimeMs, 20, 1_200),
                Math.Clamp(effects.DelayFeedbackPercent, 0, 75),
                Math.Clamp(effects.DelayMixPercent, 0, 100),
                effects.DelayPingPong,
                effects.ReverbEnabled,
                Math.Clamp(effects.ReverbMixPercent, 0, 100),
                Math.Clamp(effects.ReverbDecayPercent, 0, 100),
                Math.Clamp(effects.ReverbDampingPercent, 0, 100),
                Math.Clamp(effects.ReverbPreDelayMs, 0, 120));

            var dynamics = new TrackDynamicsSettings(
                effects.CompressorEnabled,
                Math.Clamp(effects.CompressorStrength, 0, 100),
                effects.LimiterEnabled,
                Math.Clamp(effects.LimiterCeilingDb, -12, 0));

            return (noiseReduction, basicFx, equalizer, musicalFx, dynamics);
        }
    }


    private sealed class LiveMasterEqualizerState
    {
        private readonly object _gate = new();
        private TrackEqualizerSettings _settings;

        public LiveMasterEqualizerState(AudioMasterEqualizerDocument equalizer)
        {
            _settings = CreateSnapshot(equalizer);
        }

        public void Update(AudioMasterEqualizerDocument equalizer)
        {
            lock (_gate)
            {
                _settings = CreateSnapshot(equalizer);
            }
        }

        public TrackEqualizerSettings GetSnapshot()
        {
            lock (_gate)
            {
                return _settings;
            }
        }

        private static TrackEqualizerSettings CreateSnapshot(AudioMasterEqualizerDocument equalizer) =>
            new(
                equalizer.EqualizerEnabled,
                Math.Clamp(equalizer.BassDb, -12, 12),
                Math.Clamp(equalizer.MidDb, -12, 12),
                Math.Clamp(equalizer.MidFrequencyHz, 120, 8_000),
                Math.Clamp(equalizer.TrebleDb, -12, 12),
                equalizer.HighPassEnabled,
                Math.Clamp(equalizer.HighPassFrequencyHz, 20, 600),
                equalizer.LowPassEnabled,
                Math.Clamp(equalizer.LowPassFrequencyHz, 2_000, 20_000));
    }

    private readonly record struct MasterMixerSnapshot(
        double VolumeDb,
        bool LimiterEnabled,
        double LimiterCeilingDb);

    private sealed class LiveMasterMixerState
    {
        private readonly object _gate = new();
        private double _volumeDb;
        private bool _limiterEnabled;
        private double _limiterCeilingDb;
        private AudioStereoMeter _meter = AudioStereoMeter.Silence;

        public LiveMasterMixerState(double volumeDb, bool limiterEnabled, double limiterCeilingDb)
        {
            _volumeDb = Math.Clamp(volumeDb, -60, 12);
            _limiterEnabled = limiterEnabled;
            _limiterCeilingDb = Math.Clamp(limiterCeilingDb, -12, 0);
        }

        public void Update(double volumeDb, bool limiterEnabled, double limiterCeilingDb)
        {
            lock (_gate)
            {
                _volumeDb = Math.Clamp(volumeDb, -60, 12);
                _limiterEnabled = limiterEnabled;
                _limiterCeilingDb = Math.Clamp(limiterCeilingDb, -12, 0);
            }
        }

        public MasterMixerSnapshot GetSnapshot()
        {
            lock (_gate)
            {
                return new MasterMixerSnapshot(_volumeDb, _limiterEnabled, _limiterCeilingDb);
            }
        }

        public void SetMeter(AudioStereoMeter meter)
        {
            lock (_gate)
            {
                _meter = meter;
            }
        }

        public AudioStereoMeter GetMeter()
        {
            lock (_gate)
            {
                return _meter;
            }
        }
    }

    private sealed class PlaybackSource : IDisposable
    {
        public PlaybackSource(WavAudioInfo info)
        {
            Info = info;
            Stream = new FileStream(
                info.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                256 * 1024,
                FileOptions.RandomAccess);
        }

        public WavAudioInfo Info { get; }

        public FileStream Stream { get; }

        public void Dispose() => Stream.Dispose();
    }
}
