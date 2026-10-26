using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

/// <summary>
/// View model for the detachable EQ/frequency tool window. It works on the same
/// AudioTrackViewModel instance as the main inspector and polls only the latest
/// analysis sample window from playback. FFT calculation intentionally happens
/// here, outside the realtime render/device path.
/// </summary>
public sealed class EqAnalysisWindowViewModel : ObservableObject, IDisposable
{
    private readonly AudioEditorViewModel _editor;
    private readonly RestorationAnalysisService _restorationAnalysisService;
    private readonly DispatcherTimer _spectrumTimer;
    private AudioTrackViewModel? _track;
    private bool _isMasterTarget;
    private bool _isPinned;
    private bool _isTopmost;
    private int _spectrumStageIndex = 1;
    private int _analysisViewIndex;
    private int _levelHistoryStageIndex = 1;
    private bool _showLevelProblemMarkers;
    private double _waveformTimeZoomLevel;
    private double _waveformAmplitudeZoomLevel;
    private double _waveformWindowStartSeconds;
    private double[] _spectrumDb = [];
    private int _spectrumFftSize;
    private int _spectrumSampleRate;
    private long _lastSpectrumVersion = -1;
    private string _spectrumStatusLabel = "Wiedergabe starten, um das Live-Spektrum zu sehen.";
    private bool _disposed;
    private CancellationTokenSource? _spectrogramCts;
    private SpectrogramData? _spectrogram;
    private bool _isSpectrogramBusy;
    private string _spectrogramStatusLabel = "Zeit-Zoom wählen, um ein Spektrogramm zu berechnen.";
    private double _waveformSelectionStartSeconds = -1;
    private double _waveformSelectionEndSeconds = -1;
    private NoiseProfileData? _noiseProfile;
    private bool _isNoiseProfileBusy;
    private string _noiseProfileStatusLabel = "Noch kein Rauschprofil gelernt.";

    public EqAnalysisWindowViewModel(AudioEditorViewModel editor, RestorationAnalysisService restorationAnalysisService)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(restorationAnalysisService);

        _editor = editor;
        _restorationAnalysisService = restorationAnalysisService;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.Tracks.CollectionChanged += OnTracksCollectionChanged;

        ResetEqualizerCommand = new RelayCommand(
            ResetEqualizer,
            () => HasTarget);
        LearnNoiseProfileCommand = new AsyncRelayCommand(
            LearnNoiseProfileAsync,
            () => HasValidNoiseSelection && !IsNoiseProfileBusy);
        ClearNoiseSelectionCommand = new RelayCommand(
            ClearNoiseSelection,
            () => HasNoiseSelection);
        ClearNoiseProfileCommand = new RelayCommand(
            ClearNoiseProfile,
            () => Track?.HasNoiseProfile == true);

        _spectrumTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _spectrumTimer.Tick += OnSpectrumTimerTick;

        FollowEditorTarget();
        _spectrumTimer.Start();
    }

    public AudioTrackViewModel? Track
    {
        get => _track;
        private set
        {
            if (ReferenceEquals(_track, value) && !_isMasterTarget)
            {
                return;
            }

            if (_track is not null)
            {
                if (_track.NoiseReductionListenRemoved)
                {
                    _track.NoiseReductionListenRemoved = false;
                }

                _track.PropertyChanged -= OnTrackPropertyChanged;
            }

            _track = value;
            _isMasterTarget = false;

            if (_track is not null)
            {
                _track.PropertyChanged += OnTrackPropertyChanged;
            }

            _editor.SetSpectrumAnalysisTrack(_track?.Id);
            ClearSpectrum("Wiedergabe starten, um das Live-Spektrum zu sehen.");

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTrack));
            OnPropertyChanged(nameof(IsMasterTarget));
            OnPropertyChanged(nameof(IsTrackTarget));
            OnPropertyChanged(nameof(HasTarget));
            OnPropertyChanged(nameof(TargetWaveform));
            OnPropertyChanged(nameof(TargetLevelWarningMarkers));
            OnPropertyChanged(nameof(TargetLevelHistory));
            OnPropertyChanged(nameof(TargetComparisonLevelHistory));
            OnPropertyChanged(nameof(LevelHistorySignalLabel));
            OnPropertyChanged(nameof(LevelHistoryPeakLabel));
            OnPropertyChanged(nameof(TargetTitle));
            OnPropertyChanged(nameof(TargetSubtitle));
            _waveformWindowStartSeconds = 0;
            ClearRestorationState();
            LoadNoiseProfileFromTrack();
            NotifyWaveformViewportChanged();
            ResetEqualizerCommand.NotifyCanExecuteChanged();
            ClearNoiseProfileCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasTrack => Track is not null;
    public bool IsMasterTarget => _isMasterTarget;
    public bool IsTrackTarget => !_isMasterTarget && Track is not null;
    public bool HasTarget => IsMasterTarget || Track is not null;

    /// <summary>
    /// When false, the tool window follows the track selected in the main Audio Studio.
    /// When true, it keeps editing/analyzing the currently bound track until unpinned.
    /// </summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (!SetProperty(ref _isPinned, value))
            {
                return;
            }

            OnPropertyChanged(nameof(FollowModeLabel));

            if (!value)
            {
                FollowEditorTarget();
            }
        }
    }

    public bool IsTopmost
    {
        get => _isTopmost;
        set => SetProperty(ref _isTopmost, value);
    }

    /// <summary>
    /// 0 = signal immediately before EQ/filter processing, 1 = signal immediately
    /// after the track EQ/filter chain and before track volume/pan.
    /// </summary>
    public int SpectrumStageIndex
    {
        get => _spectrumStageIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (!SetProperty(ref _spectrumStageIndex, normalized))
            {
                return;
            }

            _lastSpectrumVersion = -1;
            _spectrumDb = [];
            _spectrumFftSize = 0;
            _spectrumSampleRate = 0;
            OnPropertyChanged(nameof(SpectrumDb));
            OnPropertyChanged(nameof(SpectrumFftSize));
            OnPropertyChanged(nameof(SpectrumSampleRate));
            OnPropertyChanged(nameof(SpectrumModeLabel));
            OnPropertyChanged(nameof(SpectrumStatusLabel));
        }
    }


    /// <summary>
    /// 0 = EQ/spectrum, 1 = time-domain waveform inspection,
    /// 2 = restoration/spectrogram, 3 = time-based dBFS level history.
    /// </summary>
    public int AnalysisViewIndex
    {
        get => _analysisViewIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 3);
            if (!SetProperty(ref _analysisViewIndex, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEqView));
            OnPropertyChanged(nameof(IsWaveformView));
            OnPropertyChanged(nameof(IsRestorationView));
            OnPropertyChanged(nameof(IsLevelView));
            OnPropertyChanged(nameof(AnalysisViewTitle));
            QueueSpectrogramRefresh();
        }
    }

    public bool IsEqView
    {
        get => AnalysisViewIndex == 0;
        set
        {
            if (value)
            {
                AnalysisViewIndex = 0;
            }
        }
    }

    public bool IsWaveformView
    {
        get => AnalysisViewIndex == 1;
        set
        {
            if (value)
            {
                AnalysisViewIndex = 1;
            }
        }
    }

    public bool IsRestorationView
    {
        get => AnalysisViewIndex == 2;
        set
        {
            if (value)
            {
                AnalysisViewIndex = 2;
            }
        }
    }

    public bool IsLevelView
    {
        get => AnalysisViewIndex == 3;
        set
        {
            if (value)
            {
                AnalysisViewIndex = 3;
            }
        }
    }

    /// <summary>
    /// 0 = Master directly before the True-Peak limiter,
    /// 1 = final Master after limiter,
    /// 2 = comparison overlay (final dominant, pre-limiter subdued).
    /// Track targets always use their waveform envelope and ignore this selector.
    /// </summary>
    public int LevelHistoryStageIndex
    {
        get => _levelHistoryStageIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 1);
            if (!SetProperty(ref _levelHistoryStageIndex, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPreLimiterLevelHistory));
            OnPropertyChanged(nameof(IsFinalLevelHistory));
            OnPropertyChanged(nameof(IsComparisonLevelHistory));
            OnPropertyChanged(nameof(TargetLevelHistory));
            OnPropertyChanged(nameof(TargetComparisonLevelHistory));
            OnPropertyChanged(nameof(LevelHistorySignalLabel));
            OnPropertyChanged(nameof(LevelHistoryPeakLabel));
        }
    }

    public bool IsPreLimiterLevelHistory
    {
        get => LevelHistoryStageIndex == 0;
        set
        {
            if (value)
            {
                LevelHistoryStageIndex = 0;
            }
        }
    }

    public bool IsFinalLevelHistory
    {
        get => LevelHistoryStageIndex == 1;
        set
        {
            if (value)
            {
                LevelHistoryStageIndex = 1;
            }
        }
    }

    public bool IsComparisonLevelHistory
    {
        get => LevelHistoryStageIndex == 2;
        set
        {
            if (value)
            {
                LevelHistoryStageIndex = 2;
            }
        }
    }

    public bool ShowLevelProblemMarkers
    {
        get => _showLevelProblemMarkers;
        set
        {
            if (!SetProperty(ref _showLevelProblemMarkers, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetLevelWarningMarkers));
        }
    }

    public string AnalysisViewTitle => AnalysisViewIndex switch
    {
        0 => "Frequenzgang & Live-Spektrum",
        1 => "Wellenform & Rauschkontrolle",
        2 => "Restaurierung & Spektrogramm",
        _ => "Pegelverlauf · dBFS"
    };

    /// <summary>
    /// Discrete logarithmic time zoom: level 0..6 maps to x1..x64.
    /// </summary>
    public double WaveformTimeZoomLevel
    {
        get => _waveformTimeZoomLevel;
        set
        {
            var normalized = Math.Clamp(Math.Round(value), 0, 6);
            if (!SetProperty(ref _waveformTimeZoomLevel, normalized))
            {
                return;
            }

            ClampWaveformWindowStart();
            NotifyWaveformViewportChanged();
        }
    }

    public double WaveformTimeZoom => Math.Pow(2, WaveformTimeZoomLevel);

    public string WaveformTimeZoomLabel => $"Zeit ×{WaveformTimeZoom:0}";

    /// <summary>
    /// Visual-only amplitude magnification: level 0..7 maps to x1..x128.
    /// </summary>
    public double WaveformAmplitudeZoomLevel
    {
        get => _waveformAmplitudeZoomLevel;
        set
        {
            var normalized = Math.Clamp(Math.Round(value), 0, 7);
            if (!SetProperty(ref _waveformAmplitudeZoomLevel, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(WaveformAmplitudeScale));
            OnPropertyChanged(nameof(WaveformAmplitudeZoomLabel));
        }
    }

    public double WaveformAmplitudeScale => Math.Pow(2, WaveformAmplitudeZoomLevel);

    public string WaveformAmplitudeZoomLabel => $"Pegel ×{WaveformAmplitudeScale:0}";

    public WaveformEnvelope? TargetWaveform => IsMasterTarget ? _editor.MasterWaveform : Track?.TrackWaveform;

    public IReadOnlyList<WaveformWarningMarker> TargetLevelWarningMarkers =>
        IsMasterTarget && ShowLevelProblemMarkers ? _editor.MasterWaveformWarningMarkers : [];

    public IReadOnlyList<MasteringLevelHistoryPoint> TargetLevelHistory => IsMasterTarget
        ? LevelHistoryStageIndex == 0
            ? _editor.ProjectMasterPreLimiterLevelHistory
            : _editor.ProjectMasterLevelHistory
        : [];

    public IReadOnlyList<MasteringLevelHistoryPoint> TargetComparisonLevelHistory =>
        IsMasterTarget && LevelHistoryStageIndex == 2
            ? _editor.ProjectMasterPreLimiterLevelHistory
            : [];

    public string LevelHistorySignalLabel => IsMasterTarget
        ? TargetLevelHistory.Count > 0
            ? LevelHistoryStageIndex switch
            {
                0 => "Master vor True-Peak-Limiter · Diagnoseansicht in dBFS",
                2 => "Vergleich · kräftig = finaler Master · blass = vor Master-Limiter",
                _ => "Finaler Master nach EQ & True-Peak-Limiter · entspricht dem Export-Signal"
            }
            : "Noch keine Projektanalyse · vorläufige Master-Hüllkurve vor EQ/Limiter"
        : "Spurpegel aus der Wellenform-Hüllkurve · vor Spur-DSP, nach Clip-/Mixer-Gain";

    public string LevelHistoryPeakLabel
    {
        get
        {
            var history = TargetLevelHistory;
            if (history.Count == 0)
            {
                return MeterPeakLabel;
            }

            var peak = history.Max(item => Math.Max(item.LeftPeakDbfs, item.RightPeakDbfs));
            if (LevelHistoryStageIndex != 2 || TargetComparisonLevelHistory.Count == 0)
            {
                return peak <= -119.9 ? "– dBFS" : $"{peak:0.0} dBFS";
            }

            var comparisonPeak = TargetComparisonLevelHistory.Max(
                item => Math.Max(item.LeftPeakDbfs, item.RightPeakDbfs));
            return $"F {peak:0.0} · V {comparisonPeak:0.0} dBFS";
        }
    }

    public double WaveformDurationSeconds => TargetWaveform?.DurationSeconds ?? 0;

    public double WaveformWindowDurationSeconds
    {
        get
        {
            var duration = WaveformDurationSeconds;
            if (duration <= 0)
            {
                return 0;
            }

            return Math.Max(0.05, duration / WaveformTimeZoom);
        }
    }

    public double WaveformMaximumStartSeconds =>
        Math.Max(0, WaveformDurationSeconds - WaveformWindowDurationSeconds);

    public bool IsWaveformZoomed => WaveformTimeZoom > 1.01 && WaveformMaximumStartSeconds > 0.001;

    public double WaveformWindowStartSeconds
    {
        get => _waveformWindowStartSeconds;
        set
        {
            var normalized = Math.Clamp(value, 0, WaveformMaximumStartSeconds);
            if (!SetProperty(ref _waveformWindowStartSeconds, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(WaveformWindowLabel));
        }
    }

    public double WaveformCursorSeconds => _editor.TimelineCursorSeconds;

    public string WaveformWindowLabel
    {
        get
        {
            var duration = WaveformDurationSeconds;
            if (duration <= 0)
            {
                return "Keine Wellenform verfügbar";
            }

            var end = Math.Min(duration, WaveformWindowStartSeconds + WaveformWindowDurationSeconds);
            return $"{FormatTime(WaveformWindowStartSeconds)} – {FormatTime(end)} / {FormatTime(duration)}";
        }
    }

    public string WaveformStartLabel => FormatTime(WaveformWindowStartSeconds);

    public string WaveformMiddleLabel =>
        FormatTime(WaveformWindowStartSeconds + WaveformWindowDurationSeconds / 2d);

    public string WaveformEndLabel =>
        FormatTime(Math.Min(WaveformDurationSeconds, WaveformWindowStartSeconds + WaveformWindowDurationSeconds));

    public double WaveformSelectionStartSeconds
    {
        get => _waveformSelectionStartSeconds;
        set
        {
            if (!SetProperty(ref _waveformSelectionStartSeconds, value))
            {
                return;
            }

            NotifyNoiseSelectionChanged();
            QueueSpectrogramRefresh();
        }
    }

    public double WaveformSelectionEndSeconds
    {
        get => _waveformSelectionEndSeconds;
        set
        {
            if (!SetProperty(ref _waveformSelectionEndSeconds, value))
            {
                return;
            }

            NotifyNoiseSelectionChanged();
            QueueSpectrogramRefresh();
        }
    }

    public bool HasNoiseSelection =>
        WaveformSelectionStartSeconds >= 0 &&
        WaveformSelectionEndSeconds >= 0 &&
        Math.Abs(WaveformSelectionEndSeconds - WaveformSelectionStartSeconds) > 0.001;

    public double NoiseSelectionStartSeconds =>
        HasNoiseSelection ? Math.Min(WaveformSelectionStartSeconds, WaveformSelectionEndSeconds) : 0;

    public double NoiseSelectionDurationSeconds =>
        HasNoiseSelection ? Math.Abs(WaveformSelectionEndSeconds - WaveformSelectionStartSeconds) : 0;

    public bool HasValidNoiseSelection =>
        NoiseSelectionDurationSeconds >= RestorationAnalysisService.MinimumNoiseProfileDurationSeconds &&
        NoiseSelectionDurationSeconds <= RestorationAnalysisService.MaximumNoiseProfileDurationSeconds;

    public string NoiseSelectionLabel => !HasNoiseSelection
        ? "Noch kein Bereich markiert"
        : $"{FormatTime(NoiseSelectionStartSeconds)} – {FormatTime(NoiseSelectionStartSeconds + NoiseSelectionDurationSeconds)} · {NoiseSelectionDurationSeconds:0.00} s";

    public SpectrogramData? Spectrogram => _spectrogram;

    public bool IsSpectrogramBusy
    {
        get => _isSpectrogramBusy;
        private set => SetProperty(ref _isSpectrogramBusy, value);
    }

    public string SpectrogramStatusLabel
    {
        get => _spectrogramStatusLabel;
        private set => SetProperty(ref _spectrogramStatusLabel, value);
    }

    public string SpectrogramStartLabel =>
        FormatTime(_spectrogram?.StartSeconds ?? GetSpectrogramWindow().StartSeconds);

    public string SpectrogramMiddleLabel
    {
        get
        {
            var window = _spectrogram is not null
                ? (_spectrogram.StartSeconds, _spectrogram.DurationSeconds)
                : GetSpectrogramWindow();
            return FormatTime(window.Item1 + window.Item2 / 2d);
        }
    }

    public string SpectrogramEndLabel
    {
        get
        {
            var window = _spectrogram is not null
                ? (_spectrogram.StartSeconds, _spectrogram.DurationSeconds)
                : GetSpectrogramWindow();
            return FormatTime(window.Item1 + window.Item2);
        }
    }

    public IReadOnlyList<double> NoiseProfileDb => _noiseProfile?.MagnitudesDb ?? [];

    public int NoiseProfileSampleRate => _noiseProfile?.SampleRate ?? ProjectSampleRate;

    public int NoiseProfileFftSize => _noiseProfile?.FftSize ?? 4096;

    public bool HasNoiseProfile => _noiseProfile?.HasData == true;

    public bool IsNoiseProfileBusy
    {
        get => _isNoiseProfileBusy;
        private set
        {
            if (!SetProperty(ref _isNoiseProfileBusy, value))
            {
                return;
            }

            LearnNoiseProfileCommand.NotifyCanExecuteChanged();
        }
    }

    public string NoiseProfileStatusLabel
    {
        get => _noiseProfileStatusLabel;
        private set => SetProperty(ref _noiseProfileStatusLabel, value);
    }

    public string NoiseProfilePeakLabel
    {
        get
        {
            if (_noiseProfile?.HasData != true)
            {
                return "Profilspitze: –";
            }

            var profile = _noiseProfile.MagnitudesDb;
            var maxFrequency = Math.Min(20_000, _noiseProfile.SampleRate / 2d);
            var bestBin = 1;
            var bestDb = double.NegativeInfinity;
            for (var bin = 1; bin < profile.Length; bin++)
            {
                var frequency = bin * _noiseProfile.SampleRate / (double)_noiseProfile.FftSize;
                if (frequency < 20 || frequency > maxFrequency)
                {
                    continue;
                }

                if (profile[bin] > bestDb)
                {
                    bestDb = profile[bin];
                    bestBin = bin;
                }
            }

            var peakFrequency = bestBin * _noiseProfile.SampleRate / (double)_noiseProfile.FftSize;
            return $"Profilspitze: {FormatFrequency(peakFrequency)} · {bestDb:0.0} dBFS";
        }
    }

    public string FollowModeLabel => IsPinned
        ? "Fixiert · Zielwechsel im Studio werden ignoriert"
        : "Folgt Spur oder MASTER im Audio-Studio";

    public string TargetTitle => IsMasterTarget
        ? "Σ MASTER · Gesamtprojekt"
        : Track is null
            ? "Keine Spur ausgewählt"
            : $"Spur {Track.TrackNumber:00} · {Track.Name}";

    public string TargetSubtitle => IsMasterTarget
        ? $"{_editor.ProjectName} · {_editor.MasterTrackSummary}"
        : Track is null
            ? "Wähle im Audio-Studio eine Spur oder MASTER aus."
            : $"{Track.ChannelLabel} · {Track.ClipCountLabel} · {Track.VolumeLabel} · {Track.PanLabel}";

    public bool EqualizerEnabled
    {
        get => IsMasterTarget ? _editor.MasterEqualizerEnabled : Track?.EqualizerEnabled == true;
        set { if (IsMasterTarget) _editor.MasterEqualizerEnabled = value; else if (Track is not null) Track.EqualizerEnabled = value; OnPropertyChanged(); }
    }
    public double BassDb { get => IsMasterTarget ? _editor.MasterBassDb : Track?.BassDb ?? 0; set { if (IsMasterTarget) _editor.MasterBassDb = value; else if (Track is not null) Track.BassDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(BassLabel)); } }
    public double MidDb { get => IsMasterTarget ? _editor.MasterMidDb : Track?.MidDb ?? 0; set { if (IsMasterTarget) _editor.MasterMidDb = value; else if (Track is not null) Track.MidDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(MidLabel)); } }
    public double MidFrequencyHz { get => IsMasterTarget ? _editor.MasterMidFrequencyHz : Track?.MidFrequencyHz ?? 1000; set { if (IsMasterTarget) _editor.MasterMidFrequencyHz = value; else if (Track is not null) Track.MidFrequencyHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(MidFrequencyLabel)); } }
    public double TrebleDb { get => IsMasterTarget ? _editor.MasterTrebleDb : Track?.TrebleDb ?? 0; set { if (IsMasterTarget) _editor.MasterTrebleDb = value; else if (Track is not null) Track.TrebleDb = value; OnPropertyChanged(); OnPropertyChanged(nameof(TrebleLabel)); } }
    public bool HighPassEnabled { get => IsMasterTarget ? _editor.MasterHighPassEnabled : Track?.HighPassEnabled == true; set { if (IsMasterTarget) _editor.MasterHighPassEnabled = value; else if (Track is not null) Track.HighPassEnabled = value; OnPropertyChanged(); } }
    public double HighPassFrequencyHz { get => IsMasterTarget ? _editor.MasterHighPassFrequencyHz : Track?.HighPassFrequencyHz ?? 80; set { if (IsMasterTarget) _editor.MasterHighPassFrequencyHz = value; else if (Track is not null) Track.HighPassFrequencyHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(HighPassFrequencyLabel)); } }
    public bool LowPassEnabled { get => IsMasterTarget ? _editor.MasterLowPassEnabled : Track?.LowPassEnabled == true; set { if (IsMasterTarget) _editor.MasterLowPassEnabled = value; else if (Track is not null) Track.LowPassEnabled = value; OnPropertyChanged(); } }
    public double LowPassFrequencyHz { get => IsMasterTarget ? _editor.MasterLowPassFrequencyHz : Track?.LowPassFrequencyHz ?? 18000; set { if (IsMasterTarget) _editor.MasterLowPassFrequencyHz = value; else if (Track is not null) Track.LowPassFrequencyHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(LowPassFrequencyLabel)); } }
    public string BassLabel => $"{BassDb:+0.0;-0.0;0.0} dB";
    public string MidLabel => $"{MidDb:+0.0;-0.0;0.0} dB";
    public string TrebleLabel => $"{TrebleDb:+0.0;-0.0;0.0} dB";
    public string MidFrequencyLabel => FormatFrequency(MidFrequencyHz);
    public string HighPassFrequencyLabel => FormatFrequency(HighPassFrequencyHz);
    public string LowPassFrequencyLabel => FormatFrequency(LowPassFrequencyHz);

    public bool NotchEnabled
    {
        get => IsTrackTarget && Track?.NotchEnabled == true;
        set
        {
            if (Track is not null)
            {
                Track.NotchEnabled = value;
            }

            OnPropertyChanged();
        }
    }

    public double NotchFrequencyHz
    {
        get => Track?.NotchFrequencyHz ?? 6_500;
        set
        {
            if (Track is not null)
            {
                Track.NotchFrequencyHz = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NotchFrequencyLabel));
        }
    }

    public string NotchFrequencyLabel => FormatFrequency(NotchFrequencyHz);

    public double NotchWidthPercent
    {
        get => Track?.NotchWidthPercent ?? 30;
        set
        {
            if (Track is not null)
            {
                Track.NotchWidthPercent = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(NotchWidthLabel));
        }
    }

    public string NotchWidthLabel => Track?.NotchWidthLabel ?? "schmal";

    public bool HumRemovalEnabled
    {
        get => IsTrackTarget && Track?.HumRemovalEnabled == true;
        set
        {
            if (Track is not null)
            {
                Track.HumRemovalEnabled = value;
            }

            OnPropertyChanged();
        }
    }

    public bool IsHum50Hz
    {
        get => Track?.IsHum50Hz == true;
        set
        {
            if (value && Track is not null)
            {
                Track.HumFrequencyHz = 50;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHum60Hz));
            OnPropertyChanged(nameof(HumFrequencyHz));
        }
    }

    public bool IsHum60Hz
    {
        get => Track?.IsHum60Hz == true;
        set
        {
            if (value && Track is not null)
            {
                Track.HumFrequencyHz = 60;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsHum50Hz));
            OnPropertyChanged(nameof(HumFrequencyHz));
        }
    }

    public double HumFrequencyHz => Track?.HumFrequencyHz ?? 50;

    public int HumHarmonics
    {
        get => (int)Math.Round(Track?.HumHarmonics ?? 4);
        set
        {
            if (Track is not null)
            {
                Track.HumHarmonics = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HumHarmonicsLabel));
        }
    }

    public string HumHarmonicsLabel => $"{HumHarmonics}×";

    public double HumReductionDb
    {
        get => Track?.HumReductionDb ?? 18;
        set
        {
            if (Track is not null)
            {
                Track.HumReductionDb = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HumReductionLabel));
        }
    }

    public string HumReductionLabel => $"-{HumReductionDb:0} dB";

    public bool DeEsserEnabled
    {
        get => IsTrackTarget && Track?.DeEsserEnabled == true;
        set
        {
            if (Track is not null)
            {
                Track.DeEsserEnabled = value;
            }

            OnPropertyChanged();
        }
    }

    public double DeEsserFrequencyHz
    {
        get => Track?.DeEsserFrequencyHz ?? 6_500;
        set
        {
            if (Track is not null)
            {
                Track.DeEsserFrequencyHz = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(DeEsserFrequencyLabel));
        }
    }

    public string DeEsserFrequencyLabel => FormatFrequency(DeEsserFrequencyHz);

    public double DeEsserStrength
    {
        get => Track?.DeEsserStrength ?? 35;
        set
        {
            if (Track is not null)
            {
                Track.DeEsserStrength = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(DeEsserStrengthLabel));
        }
    }

    public string DeEsserStrengthLabel => $"{DeEsserStrength:0}%";
    public double MeterLeftDb => IsMasterTarget ? _editor.MasterMeterLeftDb : Track?.MeterLeftDb ?? -60;
    public double MeterRightDb => IsMasterTarget ? _editor.MasterMeterRightDb : Track?.MeterRightDb ?? -60;
    public string MeterPeakLabel => IsMasterTarget ? _editor.MasterPeakReadout : Track?.MeterPeakLabel ?? "−∞ dBFS";

    public int ProjectSampleRate => _editor.ProjectSampleRate;

    public string ProjectSampleRateLabel => $"{ProjectSampleRate:N0} Hz Projekt-Samplerate";

    public IReadOnlyList<double> SpectrumDb => _spectrumDb;

    public int SpectrumFftSize => _spectrumFftSize;

    public int SpectrumSampleRate => _spectrumSampleRate > 0 ? _spectrumSampleRate : ProjectSampleRate;

    public string SpectrumModeLabel => SpectrumStageIndex == 0 ? "vor EQ" : "nach EQ";

    public string SpectrumStatusLabel => _spectrumStatusLabel;

    public IRelayCommand ResetEqualizerCommand { get; }

    public IAsyncRelayCommand LearnNoiseProfileCommand { get; }

    public IRelayCommand ClearNoiseSelectionCommand { get; }

    public IRelayCommand ClearNoiseProfileCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _spectrumTimer.Stop();
        _spectrogramCts?.Cancel();
        _spectrogramCts?.Dispose();
        _spectrogramCts = null;
        _spectrumTimer.Tick -= OnSpectrumTimerTick;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _editor.Tracks.CollectionChanged -= OnTracksCollectionChanged;
        _editor.SetSpectrumAnalysisTrack(null);

        if (_track is not null)
        {
            if (_track.NoiseReductionListenRemoved)
            {
                _track.NoiseReductionListenRemoved = false;
            }

            _track.PropertyChanged -= OnTrackPropertyChanged;
        }
    }

    private void ResetEqualizer()
    {
        if (!HasTarget)
        {
            return;
        }

        HighPassEnabled = false;
        HighPassFrequencyHz = 80;
        EqualizerEnabled = false;
        BassDb = 0;
        MidDb = 0;
        MidFrequencyHz = 1_000;
        TrebleDb = 0;
        LowPassEnabled = false;
        LowPassFrequencyHz = 18_000;
    }

    private void OnSpectrumTimerTick(object? sender, EventArgs e)
    {
        if (!IsEqView)
        {
            return;
        }

        if (!HasTarget)
        {
            ClearSpectrum("Keine Spur oder MASTER ausgewählt.");
            return;
        }

        var snapshot = _editor.GetSpectrumSampleSnapshot();
        if (!snapshot.HasSamples)
        {
            DecaySpectrum();
            SetSpectrumStatus("Wiedergabe starten, um das Live-Spektrum zu sehen.");
            return;
        }

        if (snapshot.Version == _lastSpectrumVersion)
        {
            DecaySpectrum();
            return;
        }

        _lastSpectrumVersion = snapshot.Version;
        var samples = SpectrumStageIndex == 0
            ? snapshot.PreEqSamples
            : snapshot.PostEqSamples;
        var frame = SpectrumAnalyzer.Analyze(samples, snapshot.SampleRate);
        if (!frame.HasData)
        {
            DecaySpectrum();
            return;
        }

        ApplySmoothedSpectrum(frame);
        SetSpectrumStatus(
            $"Live-FFT {SpectrumModeLabel} · {frame.FftSize} Samples · {frame.SampleRate:N0} Hz");
    }

    private void ApplySmoothedSpectrum(SpectrumFrame frame)
    {
        if (_spectrumDb.Length != frame.MagnitudesDb.Length || _spectrumFftSize != frame.FftSize)
        {
            _spectrumDb = (double[])frame.MagnitudesDb.Clone();
        }
        else
        {
            var smoothed = new double[_spectrumDb.Length];
            for (var index = 0; index < _spectrumDb.Length; index++)
            {
                var current = _spectrumDb[index];
                var next = frame.MagnitudesDb[index];
                var factor = next >= current ? 0.62 : 0.20;
                smoothed[index] = current + (next - current) * factor;
            }

            _spectrumDb = smoothed;
        }

        _spectrumFftSize = frame.FftSize;
        _spectrumSampleRate = frame.SampleRate;
        OnPropertyChanged(nameof(SpectrumDb));
        OnPropertyChanged(nameof(SpectrumFftSize));
        OnPropertyChanged(nameof(SpectrumSampleRate));
    }

    private void DecaySpectrum()
    {
        if (_spectrumDb.Length == 0)
        {
            return;
        }

        var changed = false;
        var decayed = new double[_spectrumDb.Length];
        for (var index = 0; index < _spectrumDb.Length; index++)
        {
            var next = Math.Max(SpectrumAnalyzer.FloorDb, _spectrumDb[index] - 3.5);
            decayed[index] = next;
            changed |= Math.Abs(next - _spectrumDb[index]) > 0.01;
        }

        if (changed)
        {
            _spectrumDb = decayed;
            OnPropertyChanged(nameof(SpectrumDb));
        }
    }

    private void ClearSpectrum(string status)
    {
        _lastSpectrumVersion = -1;
        _spectrumDb = [];
        _spectrumFftSize = 0;
        _spectrumSampleRate = 0;
        _spectrumStatusLabel = status;
        OnPropertyChanged(nameof(SpectrumDb));
        OnPropertyChanged(nameof(SpectrumFftSize));
        OnPropertyChanged(nameof(SpectrumSampleRate));
        OnPropertyChanged(nameof(SpectrumStatusLabel));
    }

    private void SetSpectrumStatus(string status)
    {
        if (_spectrumStatusLabel == status)
        {
            return;
        }

        _spectrumStatusLabel = status;
        OnPropertyChanged(nameof(SpectrumStatusLabel));
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((e.PropertyName == nameof(AudioEditorViewModel.SelectedTrack) ||
             e.PropertyName == nameof(AudioEditorViewModel.IsMasterInspectorSelected)) && !IsPinned)
        {
            FollowEditorTarget();
            return;
        }

        if (e.PropertyName == nameof(AudioEditorViewModel.MasterWaveform))
        {
            OnPropertyChanged(nameof(TargetWaveform));
            OnPropertyChanged(nameof(LevelHistorySignalLabel));
            ClampWaveformWindowStart();
            NotifyWaveformViewportChanged();
        }

        if (e.PropertyName == nameof(AudioEditorViewModel.MasterWaveformWarningMarkers))
        {
            OnPropertyChanged(nameof(TargetLevelWarningMarkers));
        }

        if (e.PropertyName is nameof(AudioEditorViewModel.ProjectMasterLevelHistory) or
            nameof(AudioEditorViewModel.ProjectMasterPreLimiterLevelHistory))
        {
            OnPropertyChanged(nameof(TargetLevelHistory));
            OnPropertyChanged(nameof(TargetComparisonLevelHistory));
            OnPropertyChanged(nameof(LevelHistorySignalLabel));
            OnPropertyChanged(nameof(LevelHistoryPeakLabel));
        }

        if (e.PropertyName is nameof(AudioEditorViewModel.MasterMeterLeftDb) or
            nameof(AudioEditorViewModel.MasterMeterRightDb))
        {
            OnPropertyChanged(nameof(MeterLeftDb));
            OnPropertyChanged(nameof(MeterRightDb));
            OnPropertyChanged(nameof(MeterPeakLabel));
            OnPropertyChanged(nameof(LevelHistoryPeakLabel));
        }

        if (e.PropertyName is nameof(AudioEditorViewModel.MasterEqualizerEnabled) or
            nameof(AudioEditorViewModel.MasterBassDb) or
            nameof(AudioEditorViewModel.MasterMidDb) or
            nameof(AudioEditorViewModel.MasterMidFrequencyHz) or
            nameof(AudioEditorViewModel.MasterTrebleDb) or
            nameof(AudioEditorViewModel.MasterHighPassEnabled) or
            nameof(AudioEditorViewModel.MasterHighPassFrequencyHz) or
            nameof(AudioEditorViewModel.MasterLowPassEnabled) or
            nameof(AudioEditorViewModel.MasterLowPassFrequencyHz))
        {
            NotifyEqualizerPropertiesChanged();
        }

        if (e.PropertyName == nameof(AudioEditorViewModel.ProjectSampleRate))
        {
            OnPropertyChanged(nameof(ProjectSampleRate));
            OnPropertyChanged(nameof(ProjectSampleRateLabel));
            OnPropertyChanged(nameof(SpectrumSampleRate));
        }

        if (e.PropertyName == nameof(AudioEditorViewModel.TimelineCursorSeconds))
        {
            OnPropertyChanged(nameof(WaveformCursorSeconds));
        }
    }

    private void OnTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Track is not null && !_editor.Tracks.Contains(Track))
        {
            _isPinned = false;
            OnPropertyChanged(nameof(IsPinned));
            OnPropertyChanged(nameof(FollowModeLabel));
            Track = _editor.SelectedTrack;
        }
    }

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioTrackViewModel.Name) or
            nameof(AudioTrackViewModel.TrackNumber))
        {
            OnPropertyChanged(nameof(TargetTitle));
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.ChannelLayout) or
            nameof(AudioTrackViewModel.ClipCountLabel) or
            nameof(AudioTrackViewModel.VolumeDb) or
            nameof(AudioTrackViewModel.Pan))
        {
            OnPropertyChanged(nameof(TargetSubtitle));
        }

        if (e.PropertyName == nameof(AudioTrackViewModel.TrackWaveform))
        {
            OnPropertyChanged(nameof(TargetWaveform));
            OnPropertyChanged(nameof(LevelHistorySignalLabel));
            ClampWaveformWindowStart();
            NotifyWaveformViewportChanged();
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.EqualizerEnabled) or
            nameof(AudioTrackViewModel.BassDb) or
            nameof(AudioTrackViewModel.MidDb) or
            nameof(AudioTrackViewModel.MidFrequencyHz) or
            nameof(AudioTrackViewModel.TrebleDb) or
            nameof(AudioTrackViewModel.HighPassEnabled) or
            nameof(AudioTrackViewModel.HighPassFrequencyHz) or
            nameof(AudioTrackViewModel.LowPassEnabled) or
            nameof(AudioTrackViewModel.LowPassFrequencyHz))
        {
            NotifyEqualizerPropertiesChanged();
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.NotchEnabled) or
            nameof(AudioTrackViewModel.NotchFrequencyHz) or
            nameof(AudioTrackViewModel.NotchWidthPercent) or
            nameof(AudioTrackViewModel.HumRemovalEnabled) or
            nameof(AudioTrackViewModel.HumFrequencyHz) or
            nameof(AudioTrackViewModel.HumHarmonics) or
            nameof(AudioTrackViewModel.HumReductionDb) or
            nameof(AudioTrackViewModel.DeEsserEnabled) or
            nameof(AudioTrackViewModel.DeEsserFrequencyHz) or
            nameof(AudioTrackViewModel.DeEsserStrength))
        {
            NotifyRepairFxPropertiesChanged();
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.MeterLeftDb) or
            nameof(AudioTrackViewModel.MeterRightDb))
        {
            OnPropertyChanged(nameof(MeterLeftDb));
            OnPropertyChanged(nameof(MeterRightDb));
            OnPropertyChanged(nameof(MeterPeakLabel));
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.NoiseProfileDb) or
            nameof(AudioTrackViewModel.NoiseProfileFftSize) or
            nameof(AudioTrackViewModel.NoiseProfileSampleRate))
        {
            LoadNoiseProfileFromTrack();
            ClearNoiseProfileCommand.NotifyCanExecuteChanged();
        }
    }
    private void FollowEditorTarget()
    {
        if (_editor.IsMasterInspectorSelected)
        {
            SetMasterTarget();
        }
        else
        {
            Track = _editor.SelectedTrack;
        }
    }

    private void SetMasterTarget()
    {
        Track = null;
        _isMasterTarget = true;
        if (AnalysisViewIndex == 2)
        {
            AnalysisViewIndex = 0;
        }
        _editor.SetSpectrumAnalysisTrack(Guid.Empty);
        ClearSpectrum("Wiedergabe starten, um das Master-Spektrum zu sehen.");
        _waveformWindowStartSeconds = 0;
        ClearRestorationState();
        NotifyTargetPropertiesChanged();
        ResetEqualizerCommand.NotifyCanExecuteChanged();
    }

    private void NotifyEqualizerPropertiesChanged()
    {
        OnPropertyChanged(nameof(EqualizerEnabled));
        OnPropertyChanged(nameof(BassDb));
        OnPropertyChanged(nameof(BassLabel));
        OnPropertyChanged(nameof(MidDb));
        OnPropertyChanged(nameof(MidLabel));
        OnPropertyChanged(nameof(MidFrequencyHz));
        OnPropertyChanged(nameof(MidFrequencyLabel));
        OnPropertyChanged(nameof(TrebleDb));
        OnPropertyChanged(nameof(TrebleLabel));
        OnPropertyChanged(nameof(HighPassEnabled));
        OnPropertyChanged(nameof(HighPassFrequencyHz));
        OnPropertyChanged(nameof(HighPassFrequencyLabel));
        OnPropertyChanged(nameof(LowPassEnabled));
        OnPropertyChanged(nameof(LowPassFrequencyHz));
        OnPropertyChanged(nameof(LowPassFrequencyLabel));
    }

    private void NotifyRepairFxPropertiesChanged()
    {
        OnPropertyChanged(nameof(NotchEnabled));
        OnPropertyChanged(nameof(NotchFrequencyHz));
        OnPropertyChanged(nameof(NotchFrequencyLabel));
        OnPropertyChanged(nameof(NotchWidthPercent));
        OnPropertyChanged(nameof(NotchWidthLabel));
        OnPropertyChanged(nameof(HumRemovalEnabled));
        OnPropertyChanged(nameof(HumFrequencyHz));
        OnPropertyChanged(nameof(IsHum50Hz));
        OnPropertyChanged(nameof(IsHum60Hz));
        OnPropertyChanged(nameof(HumHarmonics));
        OnPropertyChanged(nameof(HumHarmonicsLabel));
        OnPropertyChanged(nameof(HumReductionDb));
        OnPropertyChanged(nameof(HumReductionLabel));
        OnPropertyChanged(nameof(DeEsserEnabled));
        OnPropertyChanged(nameof(DeEsserFrequencyHz));
        OnPropertyChanged(nameof(DeEsserFrequencyLabel));
        OnPropertyChanged(nameof(DeEsserStrength));
        OnPropertyChanged(nameof(DeEsserStrengthLabel));
    }

    private void NotifyTargetPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsMasterTarget));
        OnPropertyChanged(nameof(IsTrackTarget));
        OnPropertyChanged(nameof(HasTrack));
        OnPropertyChanged(nameof(HasTarget));
        OnPropertyChanged(nameof(TargetTitle));
        OnPropertyChanged(nameof(TargetSubtitle));
        OnPropertyChanged(nameof(TargetWaveform));
        OnPropertyChanged(nameof(TargetLevelWarningMarkers));
        OnPropertyChanged(nameof(TargetLevelHistory));
        OnPropertyChanged(nameof(TargetComparisonLevelHistory));
        OnPropertyChanged(nameof(LevelHistorySignalLabel));
        OnPropertyChanged(nameof(LevelHistoryPeakLabel));
        OnPropertyChanged(nameof(EqualizerEnabled));
        OnPropertyChanged(nameof(BassDb));
        OnPropertyChanged(nameof(BassLabel));
        OnPropertyChanged(nameof(MidDb));
        OnPropertyChanged(nameof(MidLabel));
        OnPropertyChanged(nameof(MidFrequencyHz));
        OnPropertyChanged(nameof(MidFrequencyLabel));
        OnPropertyChanged(nameof(TrebleDb));
        OnPropertyChanged(nameof(TrebleLabel));
        OnPropertyChanged(nameof(HighPassEnabled));
        OnPropertyChanged(nameof(HighPassFrequencyHz));
        OnPropertyChanged(nameof(HighPassFrequencyLabel));
        OnPropertyChanged(nameof(LowPassEnabled));
        OnPropertyChanged(nameof(LowPassFrequencyHz));
        OnPropertyChanged(nameof(LowPassFrequencyLabel));
        NotifyRepairFxPropertiesChanged();
        OnPropertyChanged(nameof(MeterLeftDb));
        OnPropertyChanged(nameof(MeterRightDb));
        OnPropertyChanged(nameof(MeterPeakLabel));
        ClampWaveformWindowStart();
        NotifyWaveformViewportChanged();
    }

    private void ClampWaveformWindowStart()
    {
        var normalized = Math.Clamp(_waveformWindowStartSeconds, 0, WaveformMaximumStartSeconds);
        if (Math.Abs(normalized - _waveformWindowStartSeconds) > 0.0001)
        {
            _waveformWindowStartSeconds = normalized;
            OnPropertyChanged(nameof(WaveformWindowStartSeconds));
        }
    }

    private void NotifyWaveformViewportChanged()
    {
        OnPropertyChanged(nameof(WaveformTimeZoom));
        OnPropertyChanged(nameof(WaveformTimeZoomLabel));
        OnPropertyChanged(nameof(WaveformDurationSeconds));
        OnPropertyChanged(nameof(WaveformWindowDurationSeconds));
        OnPropertyChanged(nameof(WaveformMaximumStartSeconds));
        OnPropertyChanged(nameof(IsWaveformZoomed));
        OnPropertyChanged(nameof(WaveformWindowStartSeconds));
        OnPropertyChanged(nameof(WaveformCursorSeconds));
        OnPropertyChanged(nameof(WaveformWindowLabel));
        OnPropertyChanged(nameof(WaveformStartLabel));
        OnPropertyChanged(nameof(WaveformMiddleLabel));
        OnPropertyChanged(nameof(WaveformEndLabel));
        OnPropertyChanged(nameof(SpectrogramStartLabel));
        OnPropertyChanged(nameof(SpectrogramMiddleLabel));
        OnPropertyChanged(nameof(SpectrogramEndLabel));
        QueueSpectrogramRefresh();
    }

    private void NotifyNoiseSelectionChanged()
    {
        OnPropertyChanged(nameof(HasNoiseSelection));
        OnPropertyChanged(nameof(NoiseSelectionStartSeconds));
        OnPropertyChanged(nameof(NoiseSelectionDurationSeconds));
        OnPropertyChanged(nameof(HasValidNoiseSelection));
        OnPropertyChanged(nameof(NoiseSelectionLabel));
        LearnNoiseProfileCommand.NotifyCanExecuteChanged();
        ClearNoiseSelectionCommand.NotifyCanExecuteChanged();
    }

    private void ClearNoiseSelection()
    {
        _waveformSelectionStartSeconds = -1;
        _waveformSelectionEndSeconds = -1;
        OnPropertyChanged(nameof(WaveformSelectionStartSeconds));
        OnPropertyChanged(nameof(WaveformSelectionEndSeconds));
        NotifyNoiseSelectionChanged();
        QueueSpectrogramRefresh();
    }

    private void ClearNoiseProfile()
    {
        Track?.ClearNoiseProfile();
        _noiseProfile = null;
        NoiseProfileStatusLabel = "Noch kein Rauschprofil gelernt.";
        OnPropertyChanged(nameof(NoiseProfileDb));
        OnPropertyChanged(nameof(NoiseProfileSampleRate));
        OnPropertyChanged(nameof(NoiseProfileFftSize));
        OnPropertyChanged(nameof(HasNoiseProfile));
        OnPropertyChanged(nameof(NoiseProfilePeakLabel));
        ClearNoiseProfileCommand.NotifyCanExecuteChanged();
    }

    private async Task LearnNoiseProfileAsync()
    {
        var track = Track;
        if (track is null || !HasValidNoiseSelection)
        {
            return;
        }

        IsNoiseProfileBusy = true;
        NoiseProfileStatusLabel = "Rauschprofil wird aus der Auswahl gelernt …";
        try
        {
            var profile = await _restorationAnalysisService.LearnNoiseProfileAsync(
                track,
                NoiseSelectionStartSeconds,
                NoiseSelectionDurationSeconds,
                ProjectSampleRate);
            _noiseProfile = profile;
            if (profile.HasData)
            {
                track.SetNoiseProfile(profile);
            }
            OnPropertyChanged(nameof(NoiseProfileDb));
            OnPropertyChanged(nameof(NoiseProfileSampleRate));
            OnPropertyChanged(nameof(NoiseProfileFftSize));
            OnPropertyChanged(nameof(HasNoiseProfile));
            OnPropertyChanged(nameof(NoiseProfilePeakLabel));
            NoiseProfileStatusLabel = profile.HasData
                ? $"Rauschprofil gelernt und der Spur zugewiesen · {NoiseSelectionDurationSeconds:0.00} s · {profile.FftSize}er FFT"
                : "In der Auswahl konnten keine verwertbaren Samples analysiert werden.";
        }
        catch (Exception ex)
        {
            NoiseProfileStatusLabel = $"Rauschprofil fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsNoiseProfileBusy = false;
        }
    }

    private void ClearRestorationState()
    {
        _spectrogramCts?.Cancel();
        _spectrogramCts?.Dispose();
        _spectrogramCts = null;
        _spectrogram = null;
        _noiseProfile = null;
        _waveformSelectionStartSeconds = -1;
        _waveformSelectionEndSeconds = -1;
        _spectrogramStatusLabel = "Zeit-Zoom wählen, um ein Spektrogramm zu berechnen.";
        _noiseProfileStatusLabel = "Noch kein Rauschprofil gelernt.";
        OnPropertyChanged(nameof(Spectrogram));
        OnPropertyChanged(nameof(SpectrogramStatusLabel));
        OnPropertyChanged(nameof(NoiseProfileDb));
        OnPropertyChanged(nameof(NoiseProfileSampleRate));
        OnPropertyChanged(nameof(NoiseProfileFftSize));
        OnPropertyChanged(nameof(HasNoiseProfile));
        OnPropertyChanged(nameof(NoiseProfileStatusLabel));
        OnPropertyChanged(nameof(NoiseProfilePeakLabel));
        OnPropertyChanged(nameof(WaveformSelectionStartSeconds));
        OnPropertyChanged(nameof(WaveformSelectionEndSeconds));
        NotifyNoiseSelectionChanged();
    }

    private void QueueSpectrogramRefresh()
    {
        if (!IsRestorationView || Track is null)
        {
            return;
        }

        _spectrogramCts?.Cancel();
        _spectrogramCts?.Dispose();
        _spectrogramCts = null;

        if (WaveformWindowDurationSeconds <= 0)
        {
            _spectrogram = null;
            OnPropertyChanged(nameof(Spectrogram));
            SpectrogramStatusLabel = "Keine Wellenform verfügbar.";
            return;
        }

        var spectrogramWindow = GetSpectrogramWindow();
        if (spectrogramWindow.DurationSeconds > RestorationAnalysisService.MaximumSpectrogramDurationSeconds)
        {
            _spectrogram = null;
            OnPropertyChanged(nameof(Spectrogram));
            SpectrogramStatusLabel = $"Für das Spektrogramm Zeit-Zoom erhöhen oder einen Bereich markieren: maximal {RestorationAnalysisService.MaximumSpectrogramDurationSeconds:0} s.";
            return;
        }

        var cts = new CancellationTokenSource();
        _spectrogramCts = cts;
        _ = RefreshSpectrogramDebouncedAsync(cts);
    }

    private async Task RefreshSpectrogramDebouncedAsync(CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(120, owner.Token);
            await RefreshSpectrogramAsync(owner);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshSpectrogramAsync(CancellationTokenSource owner)
    {
        var track = Track;
        if (track is null)
        {
            return;
        }

        IsSpectrogramBusy = true;
        SpectrogramStatusLabel = "Spektrogramm wird berechnet …";
        try
        {
            var spectrogramWindow = GetSpectrogramWindow();
            var data = await _restorationAnalysisService.BuildSpectrogramAsync(
                track,
                spectrogramWindow.StartSeconds,
                spectrogramWindow.DurationSeconds,
                ProjectSampleRate,
                owner.Token);
            if (owner.IsCancellationRequested || !ReferenceEquals(_spectrogramCts, owner))
            {
                return;
            }

            _spectrogram = data;
            OnPropertyChanged(nameof(Spectrogram));
            OnPropertyChanged(nameof(SpectrogramStartLabel));
            OnPropertyChanged(nameof(SpectrogramMiddleLabel));
            OnPropertyChanged(nameof(SpectrogramEndLabel));
            SpectrogramStatusLabel = HasNoiseSelection
                ? $"Spektrogramm der Auswahl · {data.DurationSeconds:0.0} s · {data.FftSize}er FFT"
                : $"Spektrogramm · {data.DurationSeconds:0.0} s · {data.FftSize}er FFT";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!owner.IsCancellationRequested)
            {
                _spectrogram = null;
                OnPropertyChanged(nameof(Spectrogram));
                SpectrogramStatusLabel = $"Spektrogramm fehlgeschlagen: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_spectrogramCts, owner))
            {
                IsSpectrogramBusy = false;
            }
        }
    }

    private (double StartSeconds, double DurationSeconds) GetSpectrogramWindow()
    {
        if (HasNoiseSelection && NoiseSelectionDurationSeconds >= 0.05 &&
            NoiseSelectionDurationSeconds <= RestorationAnalysisService.MaximumSpectrogramDurationSeconds)
        {
            return (NoiseSelectionStartSeconds, NoiseSelectionDurationSeconds);
        }

        return (WaveformWindowStartSeconds, WaveformWindowDurationSeconds);
    }

    private void LoadNoiseProfileFromTrack()
    {
        var track = Track;
        if (track?.HasNoiseProfile != true)
        {
            _noiseProfile = null;
            OnPropertyChanged(nameof(NoiseProfileDb));
            OnPropertyChanged(nameof(NoiseProfileSampleRate));
            OnPropertyChanged(nameof(NoiseProfileFftSize));
            OnPropertyChanged(nameof(HasNoiseProfile));
            OnPropertyChanged(nameof(NoiseProfilePeakLabel));
            return;
        }

        _noiseProfile = new NoiseProfileData(
            track.NoiseProfileSampleRate,
            track.NoiseProfileFftSize,
            0,
            0,
            track.NoiseProfileDb.ToArray());
        NoiseProfileStatusLabel = $"Gespeichertes Rauschprofil · {track.NoiseProfileFftSize}er FFT";
        OnPropertyChanged(nameof(NoiseProfileDb));
        OnPropertyChanged(nameof(NoiseProfileSampleRate));
        OnPropertyChanged(nameof(NoiseProfileFftSize));
        OnPropertyChanged(nameof(HasNoiseProfile));
        OnPropertyChanged(nameof(NoiseProfilePeakLabel));
    }

    private static string FormatFrequency(double frequencyHz) =>
        frequencyHz >= 1_000
            ? $"{frequencyHz / 1_000d:0.##} kHz"
            : $"{frequencyHz:0} Hz";

    private static string FormatTime(double seconds)
    {
        var safe = Math.Max(0, seconds);
        var span = TimeSpan.FromSeconds(safe);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 100}"
            : $"{span.Minutes:00}:{span.Seconds:00}.{span.Milliseconds / 100}";
    }

}
