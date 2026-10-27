using System.Text.Json;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Offline technical master check using the same project mixer and master DSP
/// path as playback/export. The check deliberately treats loudness as guidance,
/// not as a universal pass/fail target.
/// </summary>
public sealed class AudioExportCheckService
{
    private const int RenderBlockFrames = 4096;

    private readonly WavMetadataReader _metadataReader;
    private readonly WavSampleDecoder _decoder;
    private readonly AudioWavExportService _exportService;

    public AudioExportCheckService(
        WavMetadataReader metadataReader,
        WavSampleDecoder decoder,
        AudioWavExportService exportService)
    {
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    public async Task<AudioExportCheckResult> AnalyzeAsync(
        AudioProjectDocument document,
        AudioWavExportSettings settings,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var checkDocument = CloneDocument(document);
        checkDocument.SampleRate = settings.SampleRate;

        foreach (var track in checkDocument.Tracks)
        {
            // Monitoring mode is never part of a rendered master.
            track.Effects.NoiseReductionListenRemoved = false;
        }

        var tailSeconds = settings.IncludeEffectTail
            ? _exportService.EstimateEffectTailSeconds(checkDocument)
            : 0d;

        using var mixer = await ProjectAudioMixerSession.CreateAsync(
            checkDocument,
            _metadataReader,
            _decoder,
            cancellationToken,
            capturePreLimiterAnalysis: false,
            additionalTailSeconds: tailSeconds);

        if (!mixer.HasPlayableClips || mixer.DurationFrames <= 0)
        {
            throw new InvalidOperationException("Das Projekt enthält keine abspielbaren WAV-Clips.");
        }

        return await Task.Run(
            () => AnalyzeCore(checkDocument, mixer, progress, cancellationToken),
            cancellationToken);
    }

    private static AudioExportCheckResult AnalyzeCore(
        AudioProjectDocument document,
        ProjectAudioMixerSession mixer,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var frame = 0L;
        var samplesAboveFullScale = 0L;
        progress?.Report(0d);

        while (frame < mixer.DurationFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = checked((int)Math.Min(RenderBlockFrames, mixer.DurationFrames - frame));
            var samples = mixer.Render(frame, count, cancellationToken);
            if (samples.Length == 0)
            {
                break;
            }

            foreach (var sample in samples)
            {
                if (Math.Abs((double)sample) > 1d)
                {
                    samplesAboveFullScale++;
                }
            }

            frame += samples.Length / 2;
            progress?.Report(Math.Clamp(frame / (double)mixer.DurationFrames, 0d, 1d));
        }

        mixer.CompleteMasterAnalysis();
        var loudness = mixer.GetMasterLoudnessSnapshot();
        if (!loudness.HasData || loudness.IntegratedLufs is null)
        {
            throw new InvalidOperationException(
                "Für den Mix konnte keine belastbare Integrated-Loudness bestimmt werden.");
        }

        var limiterReduction = mixer.GetMasterLimiterReductionSnapshot();
        var truePeakMax = Math.Max(loudness.TruePeakLeftDbtp, loudness.TruePeakRightDbtp);
        var hints = BuildHints(
            loudness.IntegratedLufs.Value,
            truePeakMax,
            samplesAboveFullScale,
            document.Master.LimiterEnabled,
            limiterReduction);
        var readiness = DetermineReadiness(
            truePeakMax,
            samplesAboveFullScale,
            loudness.IntegratedLufs.Value,
            document.Master.LimiterEnabled,
            limiterReduction);

        progress?.Report(1d);
        return new AudioExportCheckResult(
            loudness.IntegratedLufs.Value,
            loudness.TruePeakLeftDbtp,
            loudness.TruePeakRightDbtp,
            samplesAboveFullScale,
            document.Master.LimiterEnabled,
            document.Master.LimiterCeilingDb,
            limiterReduction,
            frame / (double)mixer.SampleRate,
            readiness,
            hints);
    }

    private static AudioExportReadiness DetermineReadiness(
        double truePeakDbtp,
        long samplesAboveFullScale,
        double integratedLufs,
        bool limiterEnabled,
        MasterLimiterReductionSnapshot limiterReduction)
    {
        if (samplesAboveFullScale > 0 || truePeakDbtp >= 0d)
        {
            return AudioExportReadiness.NotReady;
        }

        var strongLimiter = limiterEnabled && limiterReduction.HasData &&
                            (limiterReduction.MaxReductionDb > 5d ||
                             limiterReduction.PercentAbove3Db > 5d);
        var truePeakCloseToCeiling = truePeakDbtp > -1d;
        var extremeLoudness = integratedLufs > -8d || integratedLufs < -24d;

        return strongLimiter || truePeakCloseToCeiling || extremeLoudness
            ? AudioExportReadiness.Review
            : AudioExportReadiness.Ready;
    }

    private static IReadOnlyList<string> BuildHints(
        double integratedLufs,
        double truePeakDbtp,
        long samplesAboveFullScale,
        bool limiterEnabled,
        MasterLimiterReductionSnapshot limiterReduction)
    {
        var hints = new List<string>();

        if (samplesAboveFullScale > 0 || truePeakDbtp >= 0d)
        {
            hints.Add(
                "Der fertige Master überschreitet die digitale Vollaussteuerung. Master-Pegel senken oder Limiter/Ceiling prüfen.");
        }
        else if (truePeakDbtp > -1d)
        {
            hints.Add(
                "Der True Peak liegt dicht an 0 dBTP. Für spätere AAC/MP3-/Streaming-Wandlung ist etwa −1 dBTP mehr Reserve sinnvoll.");
        }
        else
        {
            hints.Add("Der True-Peak-Headroom ist technisch unauffällig.");
        }

        if (integratedLufs > -8d)
        {
            hints.Add(
                "Der Mix ist sehr laut. Das ist kein technischer Fehler, kann aber Dynamik kosten und wird auf vielen Plattformen ohnehin heruntergeregelt.");
        }
        else if (integratedLufs < -24d)
        {
            hints.Add(
                "Der Mix ist sehr dynamisch/leise. Das kann bewusst so sein; für übliche Musikveröffentlichungen lohnt ein Pegelvergleich mit Referenzmaterial.");
        }
        else
        {
            hints.Add(
                "Die LUFS-Anzeige ist ein Orientierungswert. Es gibt keinen universellen Zielwert, der für jeden Musikstil und jedes Ausgabemedium richtig ist.");
        }

        if (!limiterEnabled)
        {
            hints.Add("Master-Limiter ist ausgeschaltet; die Peak-Sicherheit kommt vollständig aus dem Mixpegel.");
        }
        else if (!limiterReduction.HasData || limiterReduction.MaxReductionDb < 0.5d)
        {
            hints.Add("Der Master-Limiter greift praktisch nicht ein.");
        }
        else if (limiterReduction.MaxReductionDb <= 3d && limiterReduction.PercentAbove3Db <= 1d)
        {
            hints.Add("Der Master-Limiter arbeitet moderat und fängt hauptsächlich einzelne Spitzen ab.");
        }
        else if (limiterReduction.MaxReductionDb <= 5d && limiterReduction.PercentAbove3Db <= 5d)
        {
            hints.Add("Der Master-Limiter arbeitet deutlich. Auf Pumpen, verlorene Transienten und hörbare Verdichtung achten.");
        }
        else
        {
            hints.Add("Der Master-Limiter arbeitet stark. Vor dem Export Master-Pegel und Dynamikbearbeitung kritisch gegenhören.");
        }

        return hints;
    }

    private static AudioProjectDocument CloneDocument(AudioProjectDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        return JsonSerializer.Deserialize<AudioProjectDocument>(bytes)
               ?? throw new InvalidDataException("Der Projektsnapshot konnte für den Master-Check nicht erstellt werden.");
    }
}
