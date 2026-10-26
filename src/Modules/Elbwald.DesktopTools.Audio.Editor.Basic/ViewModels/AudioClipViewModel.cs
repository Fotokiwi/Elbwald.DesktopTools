using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

public sealed class AudioClipViewModel : ObservableObject
{
    private string _name;
    private double _timelineStartSeconds;
    private double _sourceStartSeconds;
    private double _durationSeconds;
    private double _gainDb;
    private double _fadeInSeconds;
    private double _fadeOutSeconds;
    private WaveformEnvelope? _waveform;
    private bool _isWaveformLoading;
    private string _waveformStateLabel = "Wellenform noch nicht geladen";

    public AudioClipViewModel(
        Guid id,
        string name,
        string sourcePath,
        double timelineStartSeconds,
        double sourceStartSeconds,
        double durationSeconds,
        double gainDb,
        double fadeInSeconds,
        double fadeOutSeconds,
        int sourceSampleRate,
        int sourceChannels,
        int sourceBitsPerSample,
        string sourceEncoding)
    {
        Id = id;
        _name = string.IsNullOrWhiteSpace(name) ? "Clip" : name.Trim();
        SourcePath = Path.GetFullPath(sourcePath);
        _timelineStartSeconds = Math.Max(0, timelineStartSeconds);
        _sourceStartSeconds = Math.Max(0, sourceStartSeconds);
        _durationSeconds = Math.Max(0, durationSeconds);
        _gainDb = Math.Clamp(gainDb, -60, 24);
        _fadeInSeconds = Math.Clamp(fadeInSeconds, 0, _durationSeconds);
        _fadeOutSeconds = Math.Clamp(fadeOutSeconds, 0, _durationSeconds);
        SourceSampleRate = sourceSampleRate;
        SourceChannels = sourceChannels;
        SourceBitsPerSample = sourceBitsPerSample;
        SourceEncoding = sourceEncoding;
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? "Clip" : value.Trim());
    }

    public string SourcePath { get; }

    public string SourceFileName => Path.GetFileName(SourcePath);

    public bool IsMissing => !File.Exists(SourcePath);

    public string SourceStateLabel => IsMissing ? "Quelldatei fehlt" : SourcePath;

    public double TimelineStartSeconds
    {
        get => _timelineStartSeconds;
        set
        {
            if (!SetProperty(ref _timelineStartSeconds, Math.Max(0, value)))
            {
                return;
            }

            OnPropertyChanged(nameof(TimelineLabel));
            OnPropertyChanged(nameof(TimelineEndSeconds));
        }
    }

    public double SourceStartSeconds
    {
        get => _sourceStartSeconds;
        set
        {
            if (!SetProperty(ref _sourceStartSeconds, Math.Max(0, value)))
            {
                return;
            }

            OnPropertyChanged(nameof(SourceRangeLabel));
        }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            var normalized = Math.Max(0, value);
            if (!SetProperty(ref _durationSeconds, normalized))
            {
                return;
            }

            if (_fadeInSeconds > normalized)
            {
                FadeInSeconds = normalized;
            }

            if (_fadeOutSeconds > normalized)
            {
                FadeOutSeconds = normalized;
            }

            OnPropertyChanged(nameof(DurationLabel));
            OnPropertyChanged(nameof(TimelineLabel));
            OnPropertyChanged(nameof(TimelineEndSeconds));
            OnPropertyChanged(nameof(SourceRangeLabel));
        }
    }

    public double TimelineEndSeconds => TimelineStartSeconds + DurationSeconds;

    public double GainDb
    {
        get => _gainDb;
        set => SetProperty(ref _gainDb, Math.Clamp(value, -60, 24));
    }

    public double FadeInSeconds
    {
        get => _fadeInSeconds;
        set => SetProperty(ref _fadeInSeconds, Math.Clamp(value, 0, DurationSeconds));
    }

    public double FadeOutSeconds
    {
        get => _fadeOutSeconds;
        set => SetProperty(ref _fadeOutSeconds, Math.Clamp(value, 0, DurationSeconds));
    }

    public int SourceSampleRate { get; }

    public int SourceChannels { get; }

    public int SourceBitsPerSample { get; }

    public string SourceEncoding { get; }

    public WaveformEnvelope? Waveform
    {
        get => _waveform;
        private set
        {
            if (!SetProperty(ref _waveform, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasWaveform));
        }
    }

    public bool HasWaveform => Waveform is not null;

    public bool IsWaveformLoading
    {
        get => _isWaveformLoading;
        private set => SetProperty(ref _isWaveformLoading, value);
    }

    public string WaveformStateLabel
    {
        get => _waveformStateLabel;
        private set => SetProperty(ref _waveformStateLabel, value);
    }

    public string DurationLabel => FormatDuration(DurationSeconds);

    public string TimelineLabel => $"Start {FormatDuration(TimelineStartSeconds)} · {DurationLabel}";

    public string SourceRangeLabel =>
        $"Quelle {FormatDuration(SourceStartSeconds)} – {FormatDuration(SourceStartSeconds + DurationSeconds)}";

    public string TechnicalLabel =>
        $"{SourceSampleRate / 1000d:0.#} kHz · {SourceBitsPerSample} Bit · {(SourceChannels == 1 ? "Mono" : "Stereo")} · {SourceEncoding}";

    public void BeginWaveformLoad()
    {
        IsWaveformLoading = true;
        WaveformStateLabel = "Wellenform wird analysiert …";
    }

    public void SetWaveform(WaveformEnvelope waveform)
    {
        ArgumentNullException.ThrowIfNull(waveform);
        Waveform = waveform;
        IsWaveformLoading = false;
        WaveformStateLabel = $"Wellenform · {waveform.PeakCount} Peaks";
    }

    public void SetWaveformUnavailable(string reason)
    {
        Waveform = null;
        IsWaveformLoading = false;
        WaveformStateLabel = string.IsNullOrWhiteSpace(reason)
            ? "Wellenform nicht verfügbar"
            : reason;
    }

    public AudioClipDocument ToDocument() =>
        new()
        {
            Id = Id,
            Name = Name,
            SourcePath = SourcePath,
            SourcePathIsRelative = false,
            TimelineStartSeconds = TimelineStartSeconds,
            SourceStartSeconds = SourceStartSeconds,
            DurationSeconds = DurationSeconds,
            GainDb = GainDb,
            FadeInSeconds = FadeInSeconds,
            FadeOutSeconds = FadeOutSeconds,
            SourceSampleRate = SourceSampleRate,
            SourceChannels = SourceChannels,
            SourceBitsPerSample = SourceBitsPerSample,
            SourceEncoding = SourceEncoding
        };

    public static AudioClipViewModel FromDocument(AudioClipDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new AudioClipViewModel(
            document.Id,
            document.Name,
            document.SourcePath,
            document.TimelineStartSeconds,
            document.SourceStartSeconds,
            document.DurationSeconds,
            document.GainDb,
            document.FadeInSeconds,
            document.FadeOutSeconds,
            document.SourceSampleRate,
            document.SourceChannels,
            document.SourceBitsPerSample,
            document.SourceEncoding);
    }

    public static AudioClipViewModel FromWav(
        WavAudioInfo info,
        double timelineStartSeconds)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new AudioClipViewModel(
            Guid.NewGuid(),
            Path.GetFileNameWithoutExtension(info.FilePath),
            info.FilePath,
            timelineStartSeconds,
            sourceStartSeconds: 0,
            info.DurationSeconds,
            gainDb: 0,
            fadeInSeconds: 0,
            fadeOutSeconds: 0,
            info.SampleRate,
            info.Channels,
            info.BitsPerSample,
            info.Encoding);
    }

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss\.fff")
            : time.ToString(@"mm\:ss\.fff");
    }
}
