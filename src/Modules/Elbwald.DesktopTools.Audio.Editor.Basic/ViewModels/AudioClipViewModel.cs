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
    private List<AudioClipMuteRangeDocument> _mutedRanges = [];
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
        string sourceEncoding,
        IReadOnlyList<AudioClipMuteRangeDocument>? mutedRanges = null)
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
        _mutedRanges = NormalizeMutedRanges(mutedRanges, _durationSeconds);
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

            var normalizedMutedRanges = NormalizeMutedRanges(_mutedRanges, normalized);
            if (!MuteRangesEqual(_mutedRanges, normalizedMutedRanges))
            {
                _mutedRanges = normalizedMutedRanges;
                OnPropertyChanged(nameof(MutedRanges));
                OnPropertyChanged(nameof(HasMutedRanges));
                OnPropertyChanged(nameof(MutedRangesLabel));
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


    public IReadOnlyList<AudioClipMuteRangeDocument> MutedRanges => _mutedRanges;

    public bool HasMutedRanges => _mutedRanges.Count > 0;

    public string MutedRangesLabel => _mutedRanges.Count switch
    {
        0 => "Keine stummgeschalteten Bereiche",
        1 => "1 Bereich stummgeschaltet",
        _ => $"{_mutedRanges.Count} Bereiche stummgeschaltet"
    };

    public void ReplaceMutedRanges(IReadOnlyList<AudioClipMuteRangeDocument>? ranges)
    {
        _mutedRanges = NormalizeMutedRanges(ranges, DurationSeconds);
        OnPropertyChanged(nameof(MutedRanges));
        OnPropertyChanged(nameof(HasMutedRanges));
        OnPropertyChanged(nameof(MutedRangesLabel));
    }

    public bool IsMutedAtLocalSeconds(double localSeconds)
    {
        if (localSeconds < 0 || localSeconds > DurationSeconds)
        {
            return false;
        }

        return _mutedRanges.Any(range =>
            localSeconds >= range.StartSeconds &&
            localSeconds < range.StartSeconds + range.DurationSeconds);
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
            MutedRanges = _mutedRanges.Select(CloneMuteRange).ToList(),
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
            document.SourceEncoding,
            document.MutedRanges);
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

    private static List<AudioClipMuteRangeDocument> NormalizeMutedRanges(
        IReadOnlyList<AudioClipMuteRangeDocument>? ranges,
        double durationSeconds)
    {
        if (ranges is null || ranges.Count == 0 || durationSeconds <= 0)
        {
            return [];
        }

        var normalized = ranges
            .Select(range => new AudioClipMuteRangeDocument
            {
                StartSeconds = Math.Clamp(range.StartSeconds, 0, durationSeconds),
                DurationSeconds = Math.Max(0, range.DurationSeconds)
            })
            .Select(range => new AudioClipMuteRangeDocument
            {
                StartSeconds = range.StartSeconds,
                DurationSeconds = Math.Min(range.DurationSeconds, durationSeconds - range.StartSeconds)
            })
            .Where(range => range.DurationSeconds > 0.000_001)
            .OrderBy(range => range.StartSeconds)
            .ToList();

        if (normalized.Count <= 1)
        {
            return normalized;
        }

        var merged = new List<AudioClipMuteRangeDocument>(normalized.Count);
        foreach (var range in normalized)
        {
            if (merged.Count == 0)
            {
                merged.Add(CloneMuteRange(range));
                continue;
            }

            var previous = merged[^1];
            var previousEnd = previous.StartSeconds + previous.DurationSeconds;
            var currentEnd = range.StartSeconds + range.DurationSeconds;
            if (range.StartSeconds <= previousEnd + 0.000_001)
            {
                previous.DurationSeconds = Math.Max(previousEnd, currentEnd) - previous.StartSeconds;
            }
            else
            {
                merged.Add(CloneMuteRange(range));
            }
        }

        return merged;
    }

    private static bool MuteRangesEqual(
        IReadOnlyList<AudioClipMuteRangeDocument> left,
        IReadOnlyList<AudioClipMuteRangeDocument> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (Math.Abs(left[index].StartSeconds - right[index].StartSeconds) > 0.000_001 ||
                Math.Abs(left[index].DurationSeconds - right[index].DurationSeconds) > 0.000_001)
            {
                return false;
            }
        }

        return true;
    }

    private static AudioClipMuteRangeDocument CloneMuteRange(AudioClipMuteRangeDocument range) =>
        new()
        {
            StartSeconds = range.StartSeconds,
            DurationSeconds = range.DurationSeconds
        };

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss\.fff")
            : time.ToString(@"mm\:ss\.fff");
    }
}
