using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Contracts.Media.Audio;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

public sealed class AudioTrackViewModel : ObservableObject
{
    private readonly Action<AudioTrackViewModel> _remove;
    private int _trackNumber;
    private string _name;
    private AudioChannelLayout _channelLayout;
    private bool _isMuted;
    private bool _isSolo;
    private double _volumeDb;
    private double _pan;
    private bool _noiseReductionEnabled;
    private double _noiseReductionStrength = 30;
    private bool _noiseReductionListenRemoved;
    private int _noiseProfileSampleRate;
    private int _noiseProfileFftSize;
    private double[] _noiseProfileDb = [];
    private bool _expanderEnabled;
    private double _expanderThresholdDb = -45;
    private double _expanderReductionDb = 18;
    private bool _notchEnabled;
    private double _notchFrequencyHz = 6_500;
    private double _notchWidthPercent = 30;
    private bool _humRemovalEnabled;
    private double _humFrequencyHz = 50;
    private double _humHarmonics = 4;
    private double _humReductionDb = 18;
    private bool _deEsserEnabled;
    private double _deEsserFrequencyHz = 6_500;
    private double _deEsserStrength = 35;
    private bool _equalizerEnabled;
    private double _bassDb;
    private double _midDb;
    private double _trebleDb;
    private double _midFrequencyHz = 1_000;
    private bool _highPassEnabled;
    private double _highPassFrequencyHz = 80;
    private bool _lowPassEnabled;
    private double _lowPassFrequencyHz = 18_000;
    private bool _compressorEnabled;
    private double _compressorStrength = 35;
    private bool _limiterEnabled;
    private double _limiterCeilingDb = -1;
    private double _meterLeftDb = -60;
    private double _meterRightDb = -60;
    private WaveformEnvelope? _trackWaveform;
    private WaveformEnvelope? _trackDisplayWaveform;
    private bool _waveformAutoScale;

    public AudioTrackViewModel(
        int trackNumber,
        AudioChannelLayout channelLayout,
        Action<AudioTrackViewModel> remove,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(remove);

        Id = id ?? Guid.NewGuid();
        _trackNumber = trackNumber;
        _channelLayout = channelLayout;
        _name = $"Spur {trackNumber}";
        _remove = remove;

        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public Guid Id { get; }

    public ObservableCollection<AudioClipViewModel> Clips { get; } = [];

    public int TrackNumber
    {
        get => _trackNumber;
        internal set
        {
            if (!SetProperty(ref _trackNumber, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TrackLabel));
        }
    }

    public string TrackLabel => TrackNumber.ToString("00");

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? $"Spur {TrackNumber}" : value.Trim());
    }

    public AudioChannelLayout ChannelLayout
    {
        get => _channelLayout;
        set
        {
            if (!SetProperty(ref _channelLayout, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ChannelLabel));
        }
    }

    public string ChannelLabel =>
        ChannelLayout == AudioChannelLayout.Mono ? "Mono" : "Stereo";

    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }

    public bool IsSolo
    {
        get => _isSolo;
        set => SetProperty(ref _isSolo, value);
    }

    public double VolumeDb
    {
        get => _volumeDb;
        set
        {
            var normalized = Math.Clamp(value, -60, 12);
            if (!SetProperty(ref _volumeDb, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(VolumeLabel));
        }
    }

    public string VolumeLabel =>
        VolumeDb <= -59.9 ? "-∞ dB" : $"{VolumeDb:+0.0;-0.0;0.0} dB";

    public double Pan
    {
        get => _pan;
        set
        {
            var normalized = Math.Clamp(value, -100, 100);
            if (!SetProperty(ref _pan, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(PanLabel));
        }
    }

    public string PanLabel
    {
        get
        {
            if (Math.Abs(Pan) < 0.5)
            {
                return "Mitte";
            }

            return Pan < 0
                ? $"L {Math.Abs(Pan):0}%"
                : $"R {Pan:0}%";
        }
    }

    public bool NoiseReductionEnabled
    {
        get => _noiseReductionEnabled;
        set
        {
            if (!SetProperty(ref _noiseReductionEnabled, value))
            {
                return;
            }

            if (!value && NoiseReductionListenRemoved)
            {
                NoiseReductionListenRemoved = false;
            }
        }
    }

    public double NoiseReductionStrength
    {
        get => _noiseReductionStrength;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _noiseReductionStrength, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(NoiseReductionStrengthLabel));
        }
    }

    public string NoiseReductionStrengthLabel => $"{NoiseReductionStrength:0}%";

    public bool NoiseReductionListenRemoved
    {
        get => _noiseReductionListenRemoved;
        set => SetProperty(ref _noiseReductionListenRemoved, value);
    }

    public int NoiseProfileSampleRate => _noiseProfileSampleRate;

    public int NoiseProfileFftSize => _noiseProfileFftSize;

    public IReadOnlyList<double> NoiseProfileDb => _noiseProfileDb;

    public bool HasNoiseProfile =>
        _noiseProfileSampleRate > 0 &&
        _noiseProfileFftSize > 0 &&
        _noiseProfileDb.Length > 1;

    public string NoiseProfileSummaryLabel => HasNoiseProfile
        ? $"Profil {_noiseProfileFftSize} FFT · {_noiseProfileSampleRate / 1000d:0.#} kHz"
        : "Kein Rauschprofil gelernt";

    public void SetNoiseProfile(NoiseProfileData profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SetNoiseProfile(profile.SampleRate, profile.FftSize, profile.MagnitudesDb);
    }

    public void SetNoiseProfile(int sampleRate, int fftSize, IReadOnlyList<double>? magnitudesDb)
    {
        _noiseProfileSampleRate = sampleRate > 0 ? sampleRate : 0;
        _noiseProfileFftSize = fftSize > 0 ? fftSize : 0;
        _noiseProfileDb = magnitudesDb is { Count: > 1 } ? magnitudesDb.ToArray() : [];
        if (!HasNoiseProfile)
        {
            _noiseProfileSampleRate = 0;
            _noiseProfileFftSize = 0;
            _noiseProfileDb = [];
            NoiseReductionEnabled = false;
            NoiseReductionListenRemoved = false;
        }

        OnPropertyChanged(nameof(NoiseProfileSampleRate));
        OnPropertyChanged(nameof(NoiseProfileFftSize));
        OnPropertyChanged(nameof(NoiseProfileDb));
        OnPropertyChanged(nameof(HasNoiseProfile));
        OnPropertyChanged(nameof(NoiseProfileSummaryLabel));
    }

    public void ClearNoiseProfile() => SetNoiseProfile(0, 0, null);

    public bool ExpanderEnabled
    {
        get => _expanderEnabled;
        set => SetProperty(ref _expanderEnabled, value);
    }

    public double ExpanderThresholdDb
    {
        get => _expanderThresholdDb;
        set
        {
            if (!SetProperty(ref _expanderThresholdDb, Math.Clamp(value, -80, -10)))
            {
                return;
            }

            OnPropertyChanged(nameof(ExpanderThresholdLabel));
        }
    }

    public string ExpanderThresholdLabel => $"{ExpanderThresholdDb:0} dBFS";

    public double ExpanderReductionDb
    {
        get => _expanderReductionDb;
        set
        {
            if (!SetProperty(ref _expanderReductionDb, Math.Clamp(value, 0, 60)))
            {
                return;
            }

            OnPropertyChanged(nameof(ExpanderReductionLabel));
        }
    }

    public string ExpanderReductionLabel => $"-{ExpanderReductionDb:0} dB";

    public bool NotchEnabled
    {
        get => _notchEnabled;
        set => SetProperty(ref _notchEnabled, value);
    }

    public double NotchFrequencyHz
    {
        get => _notchFrequencyHz;
        set
        {
            if (!SetProperty(ref _notchFrequencyHz, Math.Clamp(value, 20, 20_000)))
            {
                return;
            }

            OnPropertyChanged(nameof(NotchFrequencyLabel));
        }
    }

    public string NotchFrequencyLabel => FormatFrequency(NotchFrequencyHz);

    public double NotchWidthPercent
    {
        get => _notchWidthPercent;
        set
        {
            if (!SetProperty(ref _notchWidthPercent, Math.Clamp(value, 0, 100)))
            {
                return;
            }

            OnPropertyChanged(nameof(NotchWidthLabel));
        }
    }

    public string NotchWidthLabel => NotchWidthPercent switch
    {
        <= 20 => "sehr schmal",
        <= 45 => "schmal",
        <= 70 => "mittel",
        _ => "breit"
    };

    public bool HumRemovalEnabled
    {
        get => _humRemovalEnabled;
        set => SetProperty(ref _humRemovalEnabled, value);
    }

    public double HumFrequencyHz
    {
        get => _humFrequencyHz;
        set
        {
            var normalized = value < 55 ? 50d : 60d;
            if (!SetProperty(ref _humFrequencyHz, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsHum50Hz));
            OnPropertyChanged(nameof(IsHum60Hz));
            OnPropertyChanged(nameof(HumFrequencyLabel));
        }
    }

    public bool IsHum50Hz
    {
        get => HumFrequencyHz < 55;
        set
        {
            if (value)
            {
                HumFrequencyHz = 50;
            }
        }
    }

    public bool IsHum60Hz
    {
        get => HumFrequencyHz >= 55;
        set
        {
            if (value)
            {
                HumFrequencyHz = 60;
            }
        }
    }

    public string HumFrequencyLabel => $"{HumFrequencyHz:0} Hz";

    public double HumHarmonics
    {
        get => _humHarmonics;
        set
        {
            var normalized = Math.Clamp(Math.Round(value), 1, 8);
            if (!SetProperty(ref _humHarmonics, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(HumHarmonicsLabel));
        }
    }

    public string HumHarmonicsLabel => $"{HumHarmonics:0}×";

    public double HumReductionDb
    {
        get => _humReductionDb;
        set
        {
            if (!SetProperty(ref _humReductionDb, Math.Clamp(value, 0, 30)))
            {
                return;
            }

            OnPropertyChanged(nameof(HumReductionLabel));
        }
    }

    public string HumReductionLabel => $"-{HumReductionDb:0} dB";

    public bool DeEsserEnabled
    {
        get => _deEsserEnabled;
        set => SetProperty(ref _deEsserEnabled, value);
    }

    public double DeEsserFrequencyHz
    {
        get => _deEsserFrequencyHz;
        set
        {
            if (!SetProperty(ref _deEsserFrequencyHz, Math.Clamp(value, 3_000, 12_000)))
            {
                return;
            }

            OnPropertyChanged(nameof(DeEsserFrequencyLabel));
        }
    }

    public string DeEsserFrequencyLabel => FormatFrequency(DeEsserFrequencyHz);

    public double DeEsserStrength
    {
        get => _deEsserStrength;
        set
        {
            if (!SetProperty(ref _deEsserStrength, Math.Clamp(value, 0, 100)))
            {
                return;
            }

            OnPropertyChanged(nameof(DeEsserStrengthLabel));
        }
    }

    public string DeEsserStrengthLabel => $"{DeEsserStrength:0}%";

    public bool EqualizerEnabled
    {
        get => _equalizerEnabled;
        set
        {
            if (!SetProperty(ref _equalizerEnabled, value))
            {
                return;
            }

            NotifyEqualizerSummaryChanged();
        }
    }

    public double BassDb
    {
        get => _bassDb;
        set
        {
            if (!SetProperty(ref _bassDb, Math.Clamp(value, -12, 12)))
            {
                return;
            }

            OnPropertyChanged(nameof(BassLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string BassLabel => FormatEq(BassDb);

    public double MidDb
    {
        get => _midDb;
        set
        {
            if (!SetProperty(ref _midDb, Math.Clamp(value, -12, 12)))
            {
                return;
            }

            OnPropertyChanged(nameof(MidLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string MidLabel => FormatEq(MidDb);

    public double TrebleDb
    {
        get => _trebleDb;
        set
        {
            if (!SetProperty(ref _trebleDb, Math.Clamp(value, -12, 12)))
            {
                return;
            }

            OnPropertyChanged(nameof(TrebleLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string TrebleLabel => FormatEq(TrebleDb);

    public double MidFrequencyHz
    {
        get => _midFrequencyHz;
        set
        {
            var normalized = Math.Clamp(value, 120, 8_000);
            if (!SetProperty(ref _midFrequencyHz, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(MidFrequencyLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string MidFrequencyLabel => FormatFrequency(MidFrequencyHz);

    public bool HighPassEnabled
    {
        get => _highPassEnabled;
        set
        {
            if (!SetProperty(ref _highPassEnabled, value))
            {
                return;
            }

            NotifyEqualizerSummaryChanged();
        }
    }

    public double HighPassFrequencyHz
    {
        get => _highPassFrequencyHz;
        set
        {
            var normalized = Math.Clamp(value, 20, 600);
            if (!SetProperty(ref _highPassFrequencyHz, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(HighPassFrequencyLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string HighPassFrequencyLabel => FormatFrequency(HighPassFrequencyHz);

    public bool LowPassEnabled
    {
        get => _lowPassEnabled;
        set
        {
            if (!SetProperty(ref _lowPassEnabled, value))
            {
                return;
            }

            NotifyEqualizerSummaryChanged();
        }
    }

    public double LowPassFrequencyHz
    {
        get => _lowPassFrequencyHz;
        set
        {
            var normalized = Math.Clamp(value, 2_000, 20_000);
            if (!SetProperty(ref _lowPassFrequencyHz, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(LowPassFrequencyLabel));
            NotifyEqualizerSummaryChanged();
        }
    }

    public string LowPassFrequencyLabel => FormatFrequency(LowPassFrequencyHz);

    public string EqualizerSummaryLabel
    {
        get
        {
            var parts = new List<string>(5);

            if (HighPassEnabled)
            {
                parts.Add($"HP {HighPassFrequencyLabel}");
            }

            if (EqualizerEnabled)
            {
                parts.Add($"Bass {BassLabel}");
                parts.Add($"Mitten {MidLabel} @ {MidFrequencyLabel}");
                parts.Add($"Höhen {TrebleLabel}");
            }

            if (LowPassEnabled)
            {
                parts.Add($"LP {LowPassFrequencyLabel}");
            }

            return parts.Count == 0
                ? "Keine Frequenzbearbeitung aktiv"
                : string.Join(" · ", parts);
        }
    }

    public bool CompressorEnabled
    {
        get => _compressorEnabled;
        set => SetProperty(ref _compressorEnabled, value);
    }

    public double CompressorStrength
    {
        get => _compressorStrength;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _compressorStrength, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(CompressorStrengthLabel));
        }
    }

    public string CompressorStrengthLabel => $"{CompressorStrength:0}%";

    public bool LimiterEnabled
    {
        get => _limiterEnabled;
        set => SetProperty(ref _limiterEnabled, value);
    }

    public double LimiterCeilingDb
    {
        get => _limiterCeilingDb;
        set
        {
            var normalized = Math.Clamp(value, -12, 0);
            if (!SetProperty(ref _limiterCeilingDb, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(LimiterCeilingLabel));
        }
    }

    public string LimiterCeilingLabel => $"{LimiterCeilingDb:0.0} dBTP";

    public double MeterLeftDb => _meterLeftDb;

    public double MeterRightDb => _meterRightDb;

    public WaveformEnvelope? TrackWaveform
    {
        get => _trackWaveform;
        private set => SetProperty(ref _trackWaveform, value);
    }

    internal void SetTrackWaveform(WaveformEnvelope? waveform) =>
        TrackWaveform = waveform;

    public WaveformEnvelope? TrackDisplayWaveform
    {
        get => _trackDisplayWaveform;
        private set => SetProperty(ref _trackDisplayWaveform, value);
    }

    internal void SetTrackDisplayWaveform(WaveformEnvelope? waveform) =>
        TrackDisplayWaveform = waveform;

    public bool WaveformAutoScale
    {
        get => _waveformAutoScale;
        internal set => SetProperty(ref _waveformAutoScale, value);
    }

    public string MeterPeakLabel
    {
        get
        {
            var peak = Math.Max(MeterLeftDb, MeterRightDb);
            return peak <= -59.9 ? "−∞" : $"{peak:0.0}";
        }
    }

    internal void SetMeterLevels(double leftDb, double rightDb, bool immediate = false)
    {
        var nextLeft = ResolveMeterValue(_meterLeftDb, leftDb, immediate);
        var nextRight = ResolveMeterValue(_meterRightDb, rightDb, immediate);
        var changed = false;

        if (Math.Abs(_meterLeftDb - nextLeft) > 0.01)
        {
            _meterLeftDb = nextLeft;
            OnPropertyChanged(nameof(MeterLeftDb));
            changed = true;
        }

        if (Math.Abs(_meterRightDb - nextRight) > 0.01)
        {
            _meterRightDb = nextRight;
            OnPropertyChanged(nameof(MeterRightDb));
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(MeterPeakLabel));
        }
    }

    public int ClipCount => Clips.Count;

    public bool HasClips => Clips.Count > 0;

    public string ClipCountLabel => Clips.Count switch
    {
        0 => "leer",
        1 => "1 Clip",
        _ => $"{Clips.Count} Clips"
    };

    public double ProjectEndSeconds =>
        Clips.Count == 0 ? 0 : Clips.Max(clip => clip.TimelineEndSeconds);

    public string ProjectDurationLabel => FormatDuration(ProjectEndSeconds);

    public IRelayCommand RemoveCommand { get; }

    public void AddClip(AudioClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        clip.PropertyChanged += OnClipPropertyChanged;
        Clips.Add(clip);
        NotifyClipStateChanged();
    }

    public AudioTrackEffectsDocument CreateEffectsDocument() =>
        new()
        {
            NoiseReductionEnabled = NoiseReductionEnabled,
            NoiseReductionStrength = NoiseReductionStrength,
            NoiseReductionListenRemoved = NoiseReductionListenRemoved,
            NoiseProfileSampleRate = NoiseProfileSampleRate,
            NoiseProfileFftSize = NoiseProfileFftSize,
            NoiseProfileDb = _noiseProfileDb.ToArray(),
            ExpanderEnabled = ExpanderEnabled,
            ExpanderThresholdDb = ExpanderThresholdDb,
            ExpanderReductionDb = ExpanderReductionDb,
            NotchEnabled = NotchEnabled,
            NotchFrequencyHz = NotchFrequencyHz,
            NotchWidthPercent = NotchWidthPercent,
            HumRemovalEnabled = HumRemovalEnabled,
            HumFrequencyHz = HumFrequencyHz,
            HumHarmonics = (int)Math.Round(HumHarmonics),
            HumReductionDb = HumReductionDb,
            DeEsserEnabled = DeEsserEnabled,
            DeEsserFrequencyHz = DeEsserFrequencyHz,
            DeEsserStrength = DeEsserStrength,
            EqualizerEnabled = EqualizerEnabled,
            BassDb = BassDb,
            MidDb = MidDb,
            TrebleDb = TrebleDb,
            MidFrequencyHz = MidFrequencyHz,
            HighPassEnabled = HighPassEnabled,
            HighPassFrequencyHz = HighPassFrequencyHz,
            LowPassEnabled = LowPassEnabled,
            LowPassFrequencyHz = LowPassFrequencyHz,
            CompressorEnabled = CompressorEnabled,
            CompressorStrength = CompressorStrength,
            LimiterEnabled = LimiterEnabled,
            LimiterCeilingDb = LimiterCeilingDb
        };

    public AudioTrackDocument ToDocument()
    {
        var document = new AudioTrackDocument
        {
            Id = Id,
            Name = Name,
            ChannelLayout = ChannelLayout,
            IsMuted = IsMuted,
            IsSolo = IsSolo,
            VolumeDb = VolumeDb,
            Pan = Pan,
            Effects = CreateEffectsDocument()
        };

        document.Clips.AddRange(Clips.Select(clip => clip.ToDocument()));
        return document;
    }

    public static AudioTrackViewModel FromDocument(
        int trackNumber,
        AudioTrackDocument document,
        Action<AudioTrackViewModel> remove)
    {
        ArgumentNullException.ThrowIfNull(document);

        var track = new AudioTrackViewModel(
            trackNumber,
            document.ChannelLayout,
            remove,
            document.Id)
        {
            Name = document.Name,
            IsMuted = document.IsMuted,
            IsSolo = document.IsSolo,
            VolumeDb = document.VolumeDb,
            Pan = document.Pan,
            NoiseReductionEnabled = document.Effects.NoiseReductionEnabled,
            NoiseReductionStrength = document.Effects.NoiseReductionStrength,
            ExpanderEnabled = document.Effects.ExpanderEnabled,
            ExpanderThresholdDb = document.Effects.ExpanderThresholdDb,
            ExpanderReductionDb = document.Effects.ExpanderReductionDb,
            NotchEnabled = document.Effects.NotchEnabled,
            NotchFrequencyHz = document.Effects.NotchFrequencyHz,
            NotchWidthPercent = document.Effects.NotchWidthPercent,
            HumRemovalEnabled = document.Effects.HumRemovalEnabled,
            HumFrequencyHz = document.Effects.HumFrequencyHz,
            HumHarmonics = document.Effects.HumHarmonics,
            HumReductionDb = document.Effects.HumReductionDb,
            DeEsserEnabled = document.Effects.DeEsserEnabled,
            DeEsserFrequencyHz = document.Effects.DeEsserFrequencyHz,
            DeEsserStrength = document.Effects.DeEsserStrength,
            EqualizerEnabled = document.Effects.EqualizerEnabled,
            BassDb = document.Effects.BassDb,
            MidDb = document.Effects.MidDb,
            TrebleDb = document.Effects.TrebleDb,
            MidFrequencyHz = document.Effects.MidFrequencyHz,
            HighPassEnabled = document.Effects.HighPassEnabled,
            HighPassFrequencyHz = document.Effects.HighPassFrequencyHz,
            LowPassEnabled = document.Effects.LowPassEnabled,
            LowPassFrequencyHz = document.Effects.LowPassFrequencyHz,
            CompressorEnabled = document.Effects.CompressorEnabled,
            CompressorStrength = document.Effects.CompressorStrength,
            LimiterEnabled = document.Effects.LimiterEnabled,
            LimiterCeilingDb = document.Effects.LimiterCeilingDb
        };

        track.SetNoiseProfile(
            document.Effects.NoiseProfileSampleRate,
            document.Effects.NoiseProfileFftSize,
            document.Effects.NoiseProfileDb);

        foreach (var clip in document.Clips)
        {
            track.AddClip(AudioClipViewModel.FromDocument(clip));
        }

        return track;
    }

    private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioClipViewModel.Waveform) or
            nameof(AudioClipViewModel.HasWaveform) or
            nameof(AudioClipViewModel.IsWaveformLoading) or
            nameof(AudioClipViewModel.WaveformStateLabel))
        {
            return;
        }

        NotifyClipStateChanged();
    }

    private void NotifyClipStateChanged()
    {
        OnPropertyChanged(nameof(ClipCount));
        OnPropertyChanged(nameof(HasClips));
        OnPropertyChanged(nameof(ClipCountLabel));
        OnPropertyChanged(nameof(ProjectEndSeconds));
        OnPropertyChanged(nameof(ProjectDurationLabel));
    }

    private void NotifyEqualizerSummaryChanged()
    {
        OnPropertyChanged(nameof(EqualizerSummaryLabel));
    }

    private static double ResolveMeterValue(double current, double requested, bool immediate)
    {
        var target = Math.Clamp(requested, -60, 12);
        if (immediate || target >= current)
        {
            return target;
        }

        return Math.Max(target, current - 3.5);
    }

    private static string FormatEq(double value) =>
        $"{value:+0.0;-0.0;0.0} dB";

    private static string FormatFrequency(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000d:0.#} kHz"
            : $"{frequencyHz:0} Hz";

    private static string FormatDuration(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }
}
