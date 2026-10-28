using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Audio.Editor.Basic.Dsp;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Playback;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;
using Elbwald.DesktopTools.Contracts.Media.Audio;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

public sealed class AudioEditorViewModel : ObservableObject
{
    private readonly IAudioStudioStoragePicker _storagePicker;
    private readonly WavMetadataReader _wavMetadataReader;
    private readonly AudioProjectSerializer _projectSerializer;
    private readonly MasteringAnalysisService _masteringAnalysisService;
    private readonly WaveformCache _waveformCache;
    private readonly AudioPlaybackService _playbackService;
    private readonly EqAnalysisWindowService _eqAnalysisWindowService;
    private readonly StemRepairWindowService _stemRepairWindowService;
    private readonly EffectsHelpWindowService _effectsHelpWindowService;
    private readonly AudioExportWindowService _audioExportWindowService;
    private readonly StemRepairAuditionService _stemRepairAuditionService;
    private readonly DispatcherTimer _playbackTimer;

    private AudioTrackViewModel? _selectedTrack;
    private bool _isMasterInspectorSelected;
    private AudioClipViewModel? _selectedClip;
    private bool _hasUnsavedChanges;
    private bool _isBusy;
    private string _statusMessage =
        "Standalone Audio-Studio bereit. WAV importieren oder ein .ewaudio-Projekt öffnen.";
    private string? _currentProjectPath;
    private string _projectName = "Unbenannt";
    private int _projectSampleRate = 48_000;
    private double _masterVolumeDb;
    private double _targetLufs = -12;
    private bool _masterLimiterEnabled = true;
    private MasteringPresetOption? _selectedMasteringPreset;
    private bool _updatingMasteringPreset;
    private double _masterLimiterCeilingDb = -1;
    private bool _masterEqualizerEnabled;
    private double _masterBassDb;
    private double _masterMidDb;
    private double _masterMidFrequencyHz = 1_000;
    private double _masterTrebleDb;
    private bool _masterHighPassEnabled;
    private double _masterHighPassFrequencyHz = 80;
    private bool _masterLowPassEnabled;
    private double _masterLowPassFrequencyHz = 18_000;
    private bool _suppressDirtyTracking;
    private double _timelineZoom = 80;
    private bool _isWaveformAutoScale;
    private double _timelineCursorSeconds;
    private bool _isTimelineSeekInteraction;
    private AudioPlaybackState _lastPlaybackState = AudioPlaybackState.Stopped;
    private double _masterMeterLeftDb = -60;
    private double _masterMeterRightDb = -60;
    private WaveformEnvelope? _masterWaveform;
    private string _masterWaveformStateLabel = "Master-Summe noch leer";
    private MasterLoudnessSnapshot _liveMasterLoudness = MasterLoudnessSnapshot.Empty;
    private MasterLimiterReductionSnapshot _liveMasterLimiterReduction = MasterLimiterReductionSnapshot.Empty;
    private MasteringAnalysisResult? _offlineMasterAnalysis;
    private double _masterAnalysisProgress;
    private bool _isMasterAnalyzing;
    private long _masterAnalysisRevision;
    private bool _isMasteringPreviewActive;
    private MasteringAssistantUndoState? _masteringAssistantUndoState;
    private double? _peakAuditionEndSeconds;
    private bool _isRepairAuditionActive;
    private double _repairAuditionRestoreCursorSeconds;
    private CancellationTokenSource? _repairAuditionCts;
    private readonly SemaphoreSlim _repairAuditionGate = new(1, 1);
    private StemRepairAuditionClip? _activeRepairAuditionClip;
    private AudioTrackEffectsDocument? _effectsClipboard;
    private bool _selectedTrackEffectsBypassed;
    private double _clipSelectionStartSourceSeconds = -1d;
    private double _clipSelectionEndSourceSeconds = -1d;
    private readonly Stack<ClipEditUndoEntry> _clipEditUndo = new();
    private readonly Stack<ClipEditUndoEntry> _clipEditRedo = new();
    private bool _isApplyingClipEditSnapshot;

    private const double MinimumClipEditSeconds = 0.001d;

    public AudioEditorViewModel(
        IAudioStudioStoragePicker storagePicker,
        WavMetadataReader wavMetadataReader,
        AudioProjectSerializer projectSerializer,
        MasteringAnalysisService masteringAnalysisService,
        WaveformCache waveformCache,
        AudioPlaybackService playbackService,
        EqAnalysisWindowService eqAnalysisWindowService,
        StemRepairWindowService stemRepairWindowService,
        StemRepairAuditionService stemRepairAuditionService,
        EffectsHelpWindowService effectsHelpWindowService,
        AudioExportWindowService audioExportWindowService)
    {
        ArgumentNullException.ThrowIfNull(storagePicker);
        ArgumentNullException.ThrowIfNull(wavMetadataReader);
        ArgumentNullException.ThrowIfNull(projectSerializer);
        ArgumentNullException.ThrowIfNull(masteringAnalysisService);
        ArgumentNullException.ThrowIfNull(waveformCache);
        ArgumentNullException.ThrowIfNull(playbackService);
        ArgumentNullException.ThrowIfNull(eqAnalysisWindowService);
        ArgumentNullException.ThrowIfNull(stemRepairWindowService);
        ArgumentNullException.ThrowIfNull(stemRepairAuditionService);
        ArgumentNullException.ThrowIfNull(effectsHelpWindowService);
        ArgumentNullException.ThrowIfNull(audioExportWindowService);

        _storagePicker = storagePicker;
        _wavMetadataReader = wavMetadataReader;
        _projectSerializer = projectSerializer;
        _masteringAnalysisService = masteringAnalysisService;
        _waveformCache = waveformCache;
        _playbackService = playbackService;
        _eqAnalysisWindowService = eqAnalysisWindowService;
        _stemRepairWindowService = stemRepairWindowService;
        _stemRepairAuditionService = stemRepairAuditionService;
        _effectsHelpWindowService = effectsHelpWindowService;
        _audioExportWindowService = audioExportWindowService;

        AddMonoTrackCommand = new RelayCommand(
            () => AddTrack(AudioChannelLayout.Mono),
            () => CanAddTrack && !IsBusy);
        AddStereoTrackCommand = new RelayCommand(
            () => AddTrack(AudioChannelLayout.Stereo),
            () => CanAddTrack && !IsBusy);
        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync, () => !IsBusy);
        ImportWavCommand = new AsyncRelayCommand(ImportWavAsync, () => !IsBusy);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync, () => !IsBusy);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, () => !IsBusy);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync, () => !IsBusy);
        ExportWavCommand = new AsyncRelayCommand(ExportWavAsync, () => HasPlayableAudio && !IsBusy);
        PlayCommand = new AsyncRelayCommand(PlayAsync, CanPlay);
        PauseCommand = new RelayCommand(PausePlayback, () => PlaybackState == AudioPlaybackState.Playing);
        StopCommand = new AsyncRelayCommand(StopPlaybackAsync, () => PlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering or AudioPlaybackState.Faulted);
        SeekToCursorCommand = new AsyncRelayCommand(SeekToCursorAsync, () => HasPlayableAudio && !IsBusy);
        SplitClipCommand = new RelayCommand(SplitSelectedClip, CanSplitSelectedClip);
        TrimClipStartCommand = new RelayCommand(TrimSelectedClipStart, CanTrimSelectedClipStart);
        TrimClipEndCommand = new RelayCommand(TrimSelectedClipEnd, CanTrimSelectedClipEnd);
        RemoveClipSelectionCommand = new RelayCommand(RemoveSelectedClipRange, CanEditClipSelection);
        MuteClipSelectionCommand = new RelayCommand(MuteSelectedClipRange, CanEditClipSelection);
        ClearClipSelectionCommand = new RelayCommand(ClearClipSelection, () => HasClipSelection);
        MoveClipToCursorCommand = new RelayCommand(MoveSelectedClipToCursor, CanMoveSelectedClipToCursor);
        UndoClipEditCommand = new RelayCommand(UndoClipEdit, () => _clipEditUndo.Count > 0 && CanUseClipEditHistory);
        RedoClipEditCommand = new RelayCommand(RedoClipEdit, () => _clipEditRedo.Count > 0 && CanUseClipEditHistory);
        OpenEqAnalysisCommand = new RelayCommand(
            () => _eqAnalysisWindowService.Show(this),
            () => HasSelectedTrack || IsMasterInspectorSelected);
        OpenEffectsHelpCommand = new RelayCommand(() => _effectsHelpWindowService.Show());
        ToggleTrackEffectsBypassCommand = new RelayCommand(ToggleTrackEffectsBypass, () => HasSelectedTrack);
        CopyTrackEffectsCommand = new RelayCommand(CopyTrackEffects, () => HasSelectedTrack);
        PasteTrackEffectsCommand = new RelayCommand(PasteTrackEffects, () => HasSelectedTrack && _effectsClipboard is not null);
        ResetAllTrackEffectsCommand = new RelayCommand(ResetAllTrackEffects, () => HasSelectedTrack);
        ResetTrackEffectCommand = new RelayCommand<string?>(ResetTrackEffect, key => HasSelectedTrack && !string.IsNullOrWhiteSpace(key));
        AnalyzeMasterCommand = new AsyncRelayCommand(AnalyzeMasterAsync, () => HasPlayableAudio && !IsBusy);
        ApplyLoudnessTargetCommand = new RelayCommand(ApplyLoudnessTarget, CanApplyLoudnessTarget);
        ToggleMasteringPreviewCommand = new RelayCommand(ToggleMasteringPreview, CanToggleMasteringPreview);
        ApplyMasteringAssistantCommand = new AsyncRelayCommand(ApplyMasteringAssistantAsync, CanApplyMasteringAssistant);
        UndoMasteringAssistantCommand = new RelayCommand(UndoMasteringAssistant, () => _masteringAssistantUndoState is not null && !IsBusy);
        JumpToPeakEventCommand = new AsyncRelayCommand<MasteringPeakEventResult>(JumpToPeakEventAsync, peak => peak is not null && !IsBusy);
        ListenPeakEventCommand = new AsyncRelayCommand<MasteringPeakEventResult>(ListenPeakEventAsync, peak => peak is not null && HasPlayableAudio && !IsBusy);

        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _playbackTimer.Tick += OnPlaybackTimerTick;
        _playbackTimer.Start();

        MasteringPresets =
        [
            new("streaming", "Streaming / dynamisch", -14.0, -1.0, "Viel Dynamik und ein konservatives Streaming-Ziel."),
            new("universal", "Universal", -12.0, -1.0, "Ausgewogener Standard für Bibliothek, Auto und normale Wiedergabe."),
            new("auto", "Laut / Auto", -11.0, -1.0, "Etwas dichter und durchsetzungsfähiger, ohne direkt auf maximale Lautheit zu zielen."),
            new("event", "Event / kräftig", -10.0, -1.0, "Kräftiges Ziel. Der Mix sollte Peaks und Dynamik dafür bereits gut kontrollieren."),
            new("custom", "Benutzerdefiniert", -12.0, -1.0, "Freie Zielwerte für LUFS und True Peak.", IsCustom: true)
        ];
        _selectedMasteringPreset = MasteringPresets.First(preset => preset.Key == "universal");

        ResetProject(createInitialTrack: true);
    }

    public ObservableCollection<AudioTrackViewModel> Tracks { get; } = [];

    public bool IsWaveformAutoScale
    {
        get => _isWaveformAutoScale;
        set
        {
            if (!SetProperty(ref _isWaveformAutoScale, value))
            {
                return;
            }

            foreach (var track in Tracks)
            {
                track.WaveformAutoScale = value;
            }

            OnPropertyChanged(nameof(WaveformScaleModeLabel));
        }
    }

    public string WaveformScaleModeLabel => IsWaveformAutoScale ? "Auto" : "Absolut dB";

    public IRelayCommand AddMonoTrackCommand { get; }

    public IRelayCommand AddStereoTrackCommand { get; }

    public IAsyncRelayCommand NewProjectCommand { get; }

    public IAsyncRelayCommand ImportWavCommand { get; }

    public IAsyncRelayCommand OpenProjectCommand { get; }

    public IAsyncRelayCommand SaveProjectCommand { get; }

    public IAsyncRelayCommand SaveProjectAsCommand { get; }

    public IAsyncRelayCommand ExportWavCommand { get; }

    public IAsyncRelayCommand PlayCommand { get; }

    public IRelayCommand PauseCommand { get; }

    public IAsyncRelayCommand StopCommand { get; }

    public IAsyncRelayCommand SeekToCursorCommand { get; }

    public IRelayCommand SplitClipCommand { get; }

    public IRelayCommand TrimClipStartCommand { get; }

    public IRelayCommand TrimClipEndCommand { get; }

    public IRelayCommand RemoveClipSelectionCommand { get; }

    public IRelayCommand MuteClipSelectionCommand { get; }

    public IRelayCommand ClearClipSelectionCommand { get; }

    public IRelayCommand MoveClipToCursorCommand { get; }

    public IRelayCommand UndoClipEditCommand { get; }

    public IRelayCommand RedoClipEditCommand { get; }

    public IRelayCommand OpenEqAnalysisCommand { get; }

    public IRelayCommand OpenEffectsHelpCommand { get; }

    public IRelayCommand ToggleTrackEffectsBypassCommand { get; }

    public IRelayCommand CopyTrackEffectsCommand { get; }

    public IRelayCommand PasteTrackEffectsCommand { get; }

    public IRelayCommand ResetAllTrackEffectsCommand { get; }

    public IRelayCommand<string?> ResetTrackEffectCommand { get; }

    public IAsyncRelayCommand AnalyzeMasterCommand { get; }

    public IRelayCommand ApplyLoudnessTargetCommand { get; }

    public IRelayCommand ToggleMasteringPreviewCommand { get; }

    public IAsyncRelayCommand ApplyMasteringAssistantCommand { get; }

    public IRelayCommand UndoMasteringAssistantCommand { get; }

    public IAsyncRelayCommand<MasteringPeakEventResult> JumpToPeakEventCommand { get; }

    public IAsyncRelayCommand<MasteringPeakEventResult> ListenPeakEventCommand { get; }

    public IReadOnlyList<MasteringPresetOption> MasteringPresets { get; }

    public MasteringPresetOption? SelectedMasteringPreset
    {
        get => _selectedMasteringPreset;
        set
        {
            if (value is null || !SetProperty(ref _selectedMasteringPreset, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MasteringPresetDescription));
            if (_updatingMasteringPreset || value.IsCustom)
            {
                return;
            }

            _updatingMasteringPreset = true;
            try
            {
                TargetLufs = value.TargetLufs;
                MasterLimiterCeilingDb = value.TruePeakCeilingDbtp;
            }
            finally
            {
                _updatingMasteringPreset = false;
            }
        }
    }

    public string MasteringPresetDescription => SelectedMasteringPreset?.Description ?? string.Empty;

    public AudioTrackViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            var previousTrack = _selectedTrack;
            if (!SetProperty(ref _selectedTrack, value))
            {
                return;
            }

            if (_selectedTrackEffectsBypassed && previousTrack is not null)
            {
                _playbackService.UpdateTrackEffects(previousTrack.Id, previousTrack.CreateEffectsDocument());
                _selectedTrackEffectsBypassed = false;
                OnPropertyChanged(nameof(IsSelectedTrackEffectsBypassed));
                OnPropertyChanged(nameof(EffectsAbButtonLabel));
                OnPropertyChanged(nameof(EffectsAbStatusLabel));
            }

            if (value is not null && IsMasterInspectorSelected)
            {
                IsMasterInspectorSelected = false;
            }

            OnPropertyChanged(nameof(HasSelectedTrack));
            OnPropertyChanged(nameof(IsTrackInspectorSelected));
            OnPropertyChanged(nameof(SelectedTrackTitle));
            OnPropertyChanged(nameof(SelectedTrackClipSummary));
            OnPropertyChanged(nameof(InspectorContextTitle));
            OnPropertyChanged(nameof(InspectorContextSubtitle));
            OnPropertyChanged(nameof(SelectedTrackEffectsSummaryLabel));
            OpenEqAnalysisCommand.NotifyCanExecuteChanged();
            RefreshFxRackCommandState();
            SelectedClip = value?.Clips.FirstOrDefault();
        }
    }

    public bool IsSelectedTrackEffectsBypassed => _selectedTrackEffectsBypassed;

    public string EffectsAbButtonLabel => IsSelectedTrackEffectsBypassed
        ? "B · TROCKEN"
        : "A · MIT FX";

    public string EffectsAbStatusLabel => IsSelectedTrackEffectsBypassed
        ? "Monitor: komplette Spur-FX-Kette umgangen · Projektwerte bleiben erhalten"
        : "Monitor: Effektkette aktiv";

    public string EffectsClipboardLabel => _effectsClipboard is null
        ? "Noch keine FX kopiert"
        : "FX-Zwischenablage bereit";

    public string SelectedTrackEffectsSummaryLabel
    {
        get
        {
            if (SelectedTrack is null)
            {
                return "Keine Spur ausgewählt";
            }

            var track = SelectedTrack;
            var active = new List<string>(12);
            if (track.NoiseReductionEnabled) active.Add("Rauschreduzierung");
            if (track.ExpanderEnabled) active.Add("Expander");
            if (track.HumRemovalEnabled) active.Add("Hum");
            if (track.NotchEnabled) active.Add("Notch");
            if (track.EqualizerEnabled || track.HighPassEnabled || track.LowPassEnabled) active.Add("EQ/Filter");
            if (track.DeEsserEnabled) active.Add("De-Esser");
            if (track.ChannelModeIndex != 0 || track.PhaseInvertLeft || track.PhaseInvertRight) active.Add("Channel Tools");
            if (track.SaturationEnabled) active.Add("Saturation");
            if (track.CompressorEnabled) active.Add("Kompressor");
            if (track.ChorusEnabled) active.Add("Chorus");
            if (track.StereoWidthEnabled) active.Add("Stereo Width");
            if (track.DelayEnabled) active.Add("Delay");
            if (track.ReverbEnabled) active.Add("Reverb");
            if (track.LimiterEnabled) active.Add("Limiter");

            return active.Count == 0
                ? "Keine Spur-Effekte aktiv · Originalsignal"
                : $"{active.Count} aktiv · {string.Join(" → ", active)}";
        }
    }

    public AudioClipViewModel? SelectedClip
    {
        get => _selectedClip;
        set
        {
            if (!SetProperty(ref _selectedClip, value))
            {
                return;
            }

            _clipSelectionStartSourceSeconds = -1d;
            _clipSelectionEndSourceSeconds = -1d;
            OnPropertyChanged(nameof(HasSelectedClip));
            OnPropertyChanged(nameof(CanEditClip));
            OnPropertyChanged(nameof(SelectedClipTitle));
            OnPropertyChanged(nameof(SelectedClipWaveformWidth));
            OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
            OnPropertyChanged(nameof(ClipSelectionStartSourceSeconds));
            OnPropertyChanged(nameof(ClipSelectionEndSourceSeconds));
            OnPropertyChanged(nameof(HasClipSelection));
            OnPropertyChanged(nameof(ClipSelectionDurationSeconds));
            OnPropertyChanged(nameof(ClipSelectionLabel));
            OnPropertyChanged(nameof(SelectedClipTimelineStartSeconds));
            OnPropertyChanged(nameof(SelectedClipFadeInSeconds));
            OnPropertyChanged(nameof(SelectedClipFadeOutSeconds));
            OnPropertyChanged(nameof(SelectedClipFadeMaximumSeconds));
            OnPropertyChanged(nameof(SelectedClipMutedRangesLabel));
            RefreshClipEditCommandState();
        }
    }

    public bool CanUseClipEditHistory =>
        !IsBusy &&
        PlaybackState is AudioPlaybackState.Stopped or AudioPlaybackState.Faulted;

    public bool CanEditClip => HasSelectedClip && CanUseClipEditHistory;

    public double ClipSelectionStartSourceSeconds
    {
        get => _clipSelectionStartSourceSeconds;
        set
        {
            var normalized = NormalizeClipSelectionSourceSecond(value);
            if (!SetProperty(ref _clipSelectionStartSourceSeconds, normalized))
            {
                return;
            }

            NotifyClipSelectionChanged();
        }
    }

    public double ClipSelectionEndSourceSeconds
    {
        get => _clipSelectionEndSourceSeconds;
        set
        {
            var normalized = NormalizeClipSelectionSourceSecond(value);
            if (!SetProperty(ref _clipSelectionEndSourceSeconds, normalized))
            {
                return;
            }

            if (SelectedClip is { } clip && normalized >= clip.SourceStartSeconds)
            {
                TimelineCursorSeconds = clip.TimelineStartSeconds +
                    Math.Clamp(normalized - clip.SourceStartSeconds, 0, clip.DurationSeconds);
            }

            NotifyClipSelectionChanged();
        }
    }

    public bool HasClipSelection =>
        TryGetClipSelectionLocalRange(out var start, out var end) &&
        end - start >= MinimumClipEditSeconds;

    public double ClipSelectionDurationSeconds =>
        TryGetClipSelectionLocalRange(out var start, out var end)
            ? Math.Max(0, end - start)
            : 0d;

    public string ClipSelectionLabel => HasClipSelection
        ? $"Auswahl {FormatTimelineTime(ClipSelectionDurationSeconds)} · Ziehen ändert den Bereich"
        : "In der Clip-Wellenform ziehen, um einen Bereich zu markieren";

    public decimal SelectedClipTimelineStartSeconds
    {
        get => (decimal)(SelectedClip?.TimelineStartSeconds ?? 0d);
        set => SetSelectedClipTimelineStart((double)value);
    }

    public decimal SelectedClipFadeInSeconds
    {
        get => (decimal)(SelectedClip?.FadeInSeconds ?? 0d);
        set => SetSelectedClipFadeIn((double)value);
    }

    public decimal SelectedClipFadeOutSeconds
    {
        get => (decimal)(SelectedClip?.FadeOutSeconds ?? 0d);
        set => SetSelectedClipFadeOut((double)value);
    }

    public decimal SelectedClipFadeMaximumSeconds => (decimal)(SelectedClip?.DurationSeconds ?? 0d);

    public string SelectedClipMutedRangesLabel => SelectedClip?.MutedRangesLabel ?? "Keine stummgeschalteten Bereiche";

    public bool HasSelectedTrack => SelectedTrack is not null;

    public bool IsMasterInspectorSelected
    {
        get => _isMasterInspectorSelected;
        private set
        {
            if (!SetProperty(ref _isMasterInspectorSelected, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTrackInspectorSelected));
            OnPropertyChanged(nameof(InspectorContextTitle));
            OnPropertyChanged(nameof(InspectorContextSubtitle));
        }
    }

    public bool IsTrackInspectorSelected => !IsMasterInspectorSelected && HasSelectedTrack;

    public string InspectorContextTitle => IsMasterInspectorSelected
        ? "Σ MASTER · Gesamtprojekt"
        : SelectedTrackTitle;

    public string InspectorContextSubtitle => IsMasterInspectorSelected
        ? $"{ProjectName} · {MasterTrackSummary}"
        : SelectedTrackClipSummary;

    public bool HasSelectedClip => SelectedClip is not null;

    public string SelectedClipTitle => SelectedClip is null
        ? "Kein Clip ausgewählt"
        : $"{SelectedClip.Name} · {SelectedClip.DurationLabel}";

    public string SelectedTrackTitle =>
        SelectedTrack is null
            ? "Keine Spur ausgewählt"
            : $"Spur {SelectedTrack.TrackNumber:00} · {SelectedTrack.Name}";

    public string SelectedTrackClipSummary =>
        SelectedTrack is null
            ? "Keine Clips"
            : $"{SelectedTrack.ClipCountLabel} · Länge {SelectedTrack.ProjectDurationLabel}";

    public AudioPlaybackState PlaybackState => _playbackService.State;

    public bool IsPlaying => PlaybackState == AudioPlaybackState.Playing;

    public bool IsPlaybackPaused => PlaybackState == AudioPlaybackState.Paused;

    internal void SetSpectrumAnalysisTrack(Guid? trackId) =>
        _playbackService.SetSpectrumTarget(trackId);

    internal AudioSpectrumSampleSnapshot GetSpectrumSampleSnapshot() =>
        _playbackService.GetSpectrumSampleSnapshot();

    public bool IsPlaybackBusy => PlaybackState == AudioPlaybackState.Buffering;

    public bool HasPlayableAudio => ClipCount > 0 && MissingSourceCount < ClipCount && ProjectDurationSeconds > 0;

    public string PlaybackStateLabel => PlaybackState switch
    {
        AudioPlaybackState.Buffering => "Puffer wird aufgebaut …",
        AudioPlaybackState.Playing => "Wiedergabe",
        AudioPlaybackState.Paused => "Pausiert",
        AudioPlaybackState.Faulted => string.IsNullOrWhiteSpace(_playbackService.FaultMessage)
            ? "Audiofehler"
            : $"Audiofehler · {_playbackService.FaultMessage}",
        _ => "Bereit"
    };

    public string PlaybackBufferLabel => PlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering
        ? $"Live-Puffer {_playbackService.BufferedSeconds * 1000:0} ms · Underruns {_playbackService.UnderrunCount}"
        : "miniaudio · Stereo Float32 · Live-Mixer";

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanAutoApplyMasteringAssistant));
            OnPropertyChanged(nameof(CanUndoMasteringAssistant));
            RefreshCommandState();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Unbenannt" : value.Trim();
            if (!SetProperty(ref _projectName, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectTitle));
            OnPropertyChanged(nameof(InspectorContextSubtitle));
            MarkDirty();
        }
    }

    public string ProjectTitle => HasUnsavedChanges
        ? $"{ProjectName} *"
        : ProjectName;

    public string ProjectLocationLabel => string.IsNullOrWhiteSpace(CurrentProjectPath)
        ? "Noch nicht gespeichert"
        : CurrentProjectPath;

    public string? CurrentProjectPath
    {
        get => _currentProjectPath;
        private set
        {
            if (!SetProperty(ref _currentProjectPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectLocationLabel));
        }
    }

    public int ProjectSampleRate
    {
        get => _projectSampleRate;
        private set
        {
            if (!SetProperty(ref _projectSampleRate, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProjectSampleRateLabel));
        }
    }

    public string ProjectSampleRateLabel => $"{ProjectSampleRate / 1000d:0.#} kHz";

    public double ProjectDurationSeconds => Tracks.Count == 0
        ? 0
        : Tracks.Max(track => track.ProjectEndSeconds);

    public double TimelineMaximumSeconds => Math.Max(1, ProjectDurationSeconds);

    public double TimelineCursorSeconds
    {
        get => _timelineCursorSeconds;
        set
        {
            var normalized = Math.Clamp(value, 0, TimelineMaximumSeconds);
            if (!SetProperty(ref _timelineCursorSeconds, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(TimelineCursorLabel));
            OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
            RefreshClipEditCommandState();
        }
    }

    public string TimelineCursorLabel =>
        $"{FormatTimelineTime(TimelineCursorSeconds)} / {FormatTimelineTime(ProjectDurationSeconds)}";

    public double TimelineZoom
    {
        get => _timelineZoom;
        set
        {
            var normalized = Math.Clamp(value, 20, 240);
            if (!SetProperty(ref _timelineZoom, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(TimelineZoomLabel));
            OnPropertyChanged(nameof(SelectedClipWaveformWidth));
        }
    }

    public string TimelineZoomLabel => $"{TimelineZoom:0} px/s";

    public double SelectedClipWaveformWidth => SelectedClip is null
        ? 640
        : Math.Clamp(SelectedClip.DurationSeconds * TimelineZoom, 640, 12000);

    public double SelectedClipCursorSourceSeconds
    {
        get
        {
            if (SelectedClip is null)
            {
                return -1;
            }

            var localProjectTime = TimelineCursorSeconds - SelectedClip.TimelineStartSeconds;
            if (localProjectTime < 0 || localProjectTime > SelectedClip.DurationSeconds)
            {
                return -1;
            }

            return SelectedClip.SourceStartSeconds + localProjectTime;
        }
    }

    public int TrackCount => Tracks.Count;

    public int ClipCount => Tracks.Sum(track => track.ClipCount);

    public int MissingSourceCount => Tracks
        .SelectMany(track => track.Clips)
        .Count(clip => clip.IsMissing);

    public bool HasMissingSources => MissingSourceCount > 0;

    public bool CanAddTrack => Tracks.Count < AudioProjectLimits.MaxTrackCount;

    public string TrackCountLabel =>
        $"{Tracks.Count}/{AudioProjectLimits.MaxTrackCount} Spuren · {ClipCount} Clips";

    public WaveformEnvelope? MasterWaveform
    {
        get => _masterWaveform;
        private set => SetProperty(ref _masterWaveform, value);
    }

    public string MasterWaveformStateLabel
    {
        get => _masterWaveformStateLabel;
        private set => SetProperty(ref _masterWaveformStateLabel, value);
    }

    public string MasterTrackSummary =>
        $"{Tracks.Count} Spuren · {ClipCount} Clips · {FormatTimelineTime(ProjectDurationSeconds)}";

    public string MissingSourceLabel => MissingSourceCount switch
    {
        0 => "Alle Quelldateien gefunden",
        1 => "1 Quelldatei fehlt",
        _ => $"{MissingSourceCount} Quelldateien fehlen"
    };

    public double MasterVolumeDb
    {
        get => _masterVolumeDb;
        set
        {
            var normalized = Math.Clamp(value, -60, 12);
            if (!SetProperty(ref _masterVolumeDb, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(MasterVolumeLabel));
            _playbackService.UpdateMasterMixer(MasterVolumeDb, MasterLimiterEnabled, MasterLimiterCeilingDb);
            ResetLiveMasterAnalysis();
            InvalidateOfflineMasterAnalysis();
            RebuildMasterWaveform();
            MarkDirty();
        }
    }

    public string MasterVolumeLabel =>
        MasterVolumeDb <= -59.9 ? "-∞ dB" : $"{MasterVolumeDb:+0.0;-0.0;0.0} dB";

    public double TargetLufs
    {
        get => _targetLufs;
        set
        {
            var normalized = Math.Clamp(value, -30, -5);
            if (!SetProperty(ref _targetLufs, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(TargetLufsLabel));
            if (!_updatingMasteringPreset)
            {
                SyncMasteringPresetFromTargets();
            }
            NotifyMasterRecommendationChanged();
            RefreshMasteringPreviewIfActive();
            MarkDirty();
        }
    }

    public string TargetLufsLabel => $"{TargetLufs:0.0} LUFS";

    public bool MasterLimiterEnabled
    {
        get => _masterLimiterEnabled;
        set
        {
            if (!SetProperty(ref _masterLimiterEnabled, value))
            {
                return;
            }

            _playbackService.UpdateMasterMixer(MasterVolumeDb, MasterLimiterEnabled, MasterLimiterCeilingDb);
            ResetLiveMasterAnalysis();
            InvalidateOfflineMasterAnalysis();
            OnPropertyChanged(nameof(MasterLimiterStateLabel));
            MarkDirty();
        }
    }

    public double MasterLimiterCeilingDb
    {
        get => _masterLimiterCeilingDb;
        set
        {
            var normalized = Math.Clamp(value, -12, 0);
            if (!SetProperty(ref _masterLimiterCeilingDb, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(MasterLimiterCeilingLabel));
            _playbackService.UpdateMasterMixer(MasterVolumeDb, MasterLimiterEnabled, MasterLimiterCeilingDb);
            ResetLiveMasterAnalysis();
            InvalidateOfflineMasterAnalysis();
            if (!_updatingMasteringPreset)
            {
                SyncMasteringPresetFromTargets();
            }
            NotifyMasterRecommendationChanged();
            MarkDirty();
        }
    }

    public string MasterLimiterCeilingLabel => $"{MasterLimiterCeilingDb:0.0} dBTP";

    public bool MasterEqualizerEnabled
    {
        get => _masterEqualizerEnabled;
        set => SetMasterEqValue(ref _masterEqualizerEnabled, value, nameof(MasterEqualizerEnabled));
    }

    public double MasterBassDb
    {
        get => _masterBassDb;
        set => SetMasterEqValue(ref _masterBassDb, Math.Clamp(value, -12, 12), nameof(MasterBassDb), nameof(MasterBassLabel));
    }

    public string MasterBassLabel => $"{MasterBassDb:+0.0;-0.0;0.0} dB";

    public double MasterMidDb
    {
        get => _masterMidDb;
        set => SetMasterEqValue(ref _masterMidDb, Math.Clamp(value, -12, 12), nameof(MasterMidDb), nameof(MasterMidLabel));
    }

    public string MasterMidLabel => $"{MasterMidDb:+0.0;-0.0;0.0} dB";

    public double MasterMidFrequencyHz
    {
        get => _masterMidFrequencyHz;
        set => SetMasterEqValue(ref _masterMidFrequencyHz, Math.Clamp(value, 120, 8_000), nameof(MasterMidFrequencyHz), nameof(MasterMidFrequencyLabel));
    }

    public string MasterMidFrequencyLabel => MasterMidFrequencyHz >= 1000 ? $"{MasterMidFrequencyHz / 1000d:0.##} kHz" : $"{MasterMidFrequencyHz:0} Hz";

    public double MasterTrebleDb
    {
        get => _masterTrebleDb;
        set => SetMasterEqValue(ref _masterTrebleDb, Math.Clamp(value, -12, 12), nameof(MasterTrebleDb), nameof(MasterTrebleLabel));
    }

    public string MasterTrebleLabel => $"{MasterTrebleDb:+0.0;-0.0;0.0} dB";

    public bool MasterHighPassEnabled
    {
        get => _masterHighPassEnabled;
        set => SetMasterEqValue(ref _masterHighPassEnabled, value, nameof(MasterHighPassEnabled));
    }

    public double MasterHighPassFrequencyHz
    {
        get => _masterHighPassFrequencyHz;
        set => SetMasterEqValue(ref _masterHighPassFrequencyHz, Math.Clamp(value, 20, 600), nameof(MasterHighPassFrequencyHz), nameof(MasterHighPassFrequencyLabel));
    }

    public string MasterHighPassFrequencyLabel => $"{MasterHighPassFrequencyHz:0} Hz";

    public bool MasterLowPassEnabled
    {
        get => _masterLowPassEnabled;
        set => SetMasterEqValue(ref _masterLowPassEnabled, value, nameof(MasterLowPassEnabled));
    }

    public double MasterLowPassFrequencyHz
    {
        get => _masterLowPassFrequencyHz;
        set => SetMasterEqValue(ref _masterLowPassFrequencyHz, Math.Clamp(value, 2_000, 20_000), nameof(MasterLowPassFrequencyHz), nameof(MasterLowPassFrequencyLabel));
    }

    public string MasterLowPassFrequencyLabel => MasterLowPassFrequencyHz >= 1000 ? $"{MasterLowPassFrequencyHz / 1000d:0.##} kHz" : $"{MasterLowPassFrequencyHz:0} Hz";

    public AudioMasterEqualizerDocument CreateMasterEqualizerDocument() => new()
    {
        EqualizerEnabled = MasterEqualizerEnabled,
        BassDb = MasterBassDb,
        MidDb = MasterMidDb,
        MidFrequencyHz = MasterMidFrequencyHz,
        TrebleDb = MasterTrebleDb,
        HighPassEnabled = MasterHighPassEnabled,
        HighPassFrequencyHz = MasterHighPassFrequencyHz,
        LowPassEnabled = MasterLowPassEnabled,
        LowPassFrequencyHz = MasterLowPassFrequencyHz
    };

    private void SetMasterEqValue<T>(ref T field, T value, string propertyName, params string[] dependentProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        foreach (var dependent in dependentProperties)
        {
            OnPropertyChanged(dependent);
        }

        _playbackService.UpdateMasterEqualizer(CreateMasterEqualizerDocument());
        ResetLiveMasterAnalysis();
        InvalidateOfflineMasterAnalysis();
        MarkDirty();
    }

    public double MasterMeterLeftDb => _masterMeterLeftDb;

    public double MasterMeterRightDb => _masterMeterRightDb;

    public string MasterPeakReadout
    {
        get
        {
            var peak = Math.Max(MasterMeterLeftDb, MasterMeterRightDb);
            return peak <= -59.9 ? "−∞ dBFS" : $"{peak:0.0} dBFS";
        }
    }

    public bool HasMasterAnalysis => _offlineMasterAnalysis is not null;

    public bool IsMasterAnalyzing
    {
        get => _isMasterAnalyzing;
        private set
        {
            if (!SetProperty(ref _isMasterAnalyzing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MasterAnalysisButtonLabel));
        }
    }

    public double MasterAnalysisProgress
    {
        get => _masterAnalysisProgress;
        private set
        {
            if (!SetProperty(ref _masterAnalysisProgress, Math.Clamp(value, 0, 100)))
            {
                return;
            }

            OnPropertyChanged(nameof(MasterAnalysisProgressLabel));
        }
    }

    public string MasterAnalysisProgressLabel => IsMasterAnalyzing
        ? $"Projektanalyse {MasterAnalysisProgress:0}%"
        : MasterAnalysisSourceLabel;

    public string MasterAnalysisButtonLabel => IsMasterAnalyzing
        ? "Analyse läuft …"
        : "Projekt analysieren";

    public bool HasLiveMasterAnalysis => _liveMasterLoudness.HasData;

    public string ProjectIntegratedReadout => _offlineMasterAnalysis is null
        ? "– LUFS"
        : $"{_offlineMasterAnalysis.IntegratedLufs:0.0} LUFS";

    public string ProjectShortTermReadout => _offlineMasterAnalysis?.MaxShortTermLufs is double value
        ? $"{value:0.0} LUFS"
        : "– LUFS";

    public string ProjectMomentaryReadout => _offlineMasterAnalysis?.MaxMomentaryLufs is double value
        ? $"{value:0.0} LUFS"
        : "– LUFS";

    public string ProjectTruePeakReadout => _offlineMasterAnalysis is null
        ? "– dBTP"
        : $"{_offlineMasterAnalysis.TruePeakMaxDbtp:0.0} dBTP";

    public string ProjectTruePeakLeftReadout => _offlineMasterAnalysis is null
        ? "–"
        : FormatDbtp(_offlineMasterAnalysis.TruePeakLeftDbtp);

    public string ProjectTruePeakRightReadout => _offlineMasterAnalysis is null
        ? "–"
        : FormatDbtp(_offlineMasterAnalysis.TruePeakRightDbtp);

    public string ProjectTruePeakStatusLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null)
            {
                return "Noch keine vollständige Projektmessung.";
            }

            var peak = _offlineMasterAnalysis.TruePeakMaxDbtp;
            if (peak > 0)
            {
                return $"⚠ True Peak liegt {peak:0.0} dB über 0 dBTP. Vor Export unbedingt Peaks begrenzen.";
            }

            if (peak > MasterLimiterCeilingDb)
            {
                return $"⚠ True Peak liegt {peak - MasterLimiterCeilingDb:0.0} dB über dem Ziel von {MasterLimiterCeilingDb:0.0} dBTP.";
            }

            return "✓ True Peak liegt innerhalb des gewählten Zielbereichs.";
        }
    }

    public string LiveIntegratedReadout => FormatLufs(_liveMasterLoudness.IntegratedLufs);

    public string LiveShortTermReadout => FormatLufs(_liveMasterLoudness.ShortTermLufs);

    public string LiveMomentaryReadout => FormatLufs(_liveMasterLoudness.MomentaryLufs);

    public string LiveTruePeakReadout
    {
        get
        {
            var peak = Math.Max(_liveMasterLoudness.TruePeakLeftDbtp, _liveMasterLoudness.TruePeakRightDbtp);
            return peak <= -119.9 ? "– dBTP" : $"{peak:0.0} dBTP";
        }
    }

    public string LiveTruePeakLeftReadout => FormatDbtp(_liveMasterLoudness.TruePeakLeftDbtp);

    public string LiveTruePeakRightReadout => FormatDbtp(_liveMasterLoudness.TruePeakRightDbtp);

    public string LiveAnalysisStatusLabel => _liveMasterLoudness.HasData
        ? "Seit letztem Wiedergabestart / Seek"
        : "Wiedergabe starten, um die aktuelle Passage live zu messen.";

    public string LiveLimiterReductionReadout =>
        _liveMasterLimiterReduction.HasData && MasterLimiterEnabled
            ? $"{_liveMasterLimiterReduction.CurrentReductionDb:0.0} dB GR"
            : "– dB GR";

    public string ProjectLimiterMaxReductionReadout =>
        _offlineMasterAnalysis?.LimiterReduction.HasData == true
            ? $"{_offlineMasterAnalysis.LimiterReduction.MaxReductionDb:0.0} dB"
            : "–";

    public string ProjectLimiterAverageReductionReadout =>
        _offlineMasterAnalysis?.LimiterReduction.HasData == true
            ? $"{_offlineMasterAnalysis.LimiterReduction.AverageReductionDb:0.0} dB"
            : "–";

    public string ProjectLimiterAbove3Readout =>
        _offlineMasterAnalysis?.LimiterReduction.HasData == true
            ? $"{_offlineMasterAnalysis.LimiterReduction.PercentAbove3Db:0.0}%"
            : "–";

    public string ProjectLimiterAbove5Readout =>
        _offlineMasterAnalysis?.LimiterReduction.HasData == true
            ? $"{_offlineMasterAnalysis.LimiterReduction.PercentAbove5Db:0.0}%"
            : "–";

    public string ProjectLimiterTimeAboveThresholdsReadout =>
        $"{ProjectLimiterAbove3Readout} / {ProjectLimiterAbove5Readout}";

    public string ProjectLimiterReductionAssessmentLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null)
            {
                return "Projekt analysieren, um die tatsächliche Limiter-Belastung zu messen.";
            }

            var reduction = _offlineMasterAnalysis.LimiterReduction;
            if (!reduction.HasData)
            {
                return "Limiter war bei der Projektanalyse deaktiviert.";
            }

            if (reduction.MaxReductionDb < 0.25)
            {
                return "✓ Limiter greift praktisch nicht ein.";
            }

            if (reduction.PercentAbove5Db >= 1.0 || reduction.PercentAbove3Db >= 10.0 || reduction.MaxReductionDb >= 7.0)
            {
                return "⚠ Hohe Limiter-Belastung. Peaks/Dynamik besser vor dem Master bearbeiten.";
            }

            if (reduction.PercentAbove3Db >= 2.0 || reduction.MaxReductionDb >= 4.5)
            {
                return "⚠ Deutliches Limiting. A/B gegenhören und Transienten prüfen.";
            }

            return "✓ Limiter-Eingriff ist überwiegend moderat und kurzzeitig.";
        }
    }

    public string MasterAnalysisSourceLabel => _offlineMasterAnalysis is not null
        ? "Projektanalyse · kompletter Mix · ST/M = Maximalwerte"
        : "Noch nicht analysiert";

    public double? RecommendedMasterGainDb => _offlineMasterAnalysis is null
        ? null
        : TargetLufs - _offlineMasterAnalysis.PreLimiterIntegratedLufs;

    public string RecommendedMasterGainLabel => RecommendedMasterGainDb is double gain
        ? $"{gain:+0.0;-0.0;0.0} dB"
        : "–";

    public string PreLimiterLoudnessReadout => _offlineMasterAnalysis is null
        ? "–"
        : $"{_offlineMasterAnalysis.PreLimiterIntegratedLufs:0.0} LUFS";

    public string ProjectedTruePeakLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null || RecommendedMasterGainDb is not double gain)
            {
                return "–";
            }

            var projected = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp + gain;
            return $"{projected:0.0} dBTP";
        }
    }

    public double? RequiredPeakReductionDb
    {
        get
        {
            if (_offlineMasterAnalysis is null || RecommendedMasterGainDb is not double gain)
            {
                return null;
            }

            var projected = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp + gain;
            return Math.Max(0, projected - MasterLimiterCeilingDb);
        }
    }

    public string RequiredPeakReductionLabel => RequiredPeakReductionDb is double reduction
        ? $"{reduction:0.0} dB"
        : "–";

    public string MasterLimiterStateLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null || RecommendedMasterGainDb is not double gain)
            {
                return MasterLimiterEnabled
                    ? $"Limiter aktiv · Ceiling {MasterLimiterCeilingDb:0.0} dBTP"
                    : $"Limiter aus · True-Peak-Ziel bleibt {MasterLimiterCeilingDb:0.0} dBTP";
            }

            var projected = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp + gain;
            var reduction = Math.Max(0, projected - MasterLimiterCeilingDb);
            if (!MasterLimiterEnabled)
            {
                return reduction > 0
                    ? $"⚠ Limiter aus · zum Ziel fehlen etwa {reduction:0.0} dB Peak-Kontrolle"
                    : "Limiter aus · Ziel-True-Peak voraussichtlich bereits eingehalten";
            }

            return reduction > 0
                ? $"Limiter müsste an Spitzen etwa {reduction:0.0} dB reduzieren"
                : "Ziel liegt unter dem Limiter-Ceiling";
        }
    }

    public string MasterTargetAssessmentLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null || RecommendedMasterGainDb is not double gain)
            {
                return "Erst das komplette Projekt analysieren. Danach bewertet Elbwald, ob das gewählte Ziel durch reine Normalisierung erreichbar ist oder zusätzliche Peak-/Dynamikbearbeitung braucht.";
            }

            var reduction = RequiredPeakReductionDb ?? 0;

            if (gain < -0.25)
            {
                if (reduction <= 0.5)
                {
                    return $"✓ Gut erreichbar: Der komplette Mix wird nur um {Math.Abs(gain):0.0} dB abgesenkt. Die Dynamik bleibt praktisch unverändert.";
                }

                return $"⚠ Lautheit ist leicht erreichbar, aber einzelne Peaks benötigen trotzdem etwa {reduction:0.0} dB Kontrolle.";
            }

            if (gain >= 6 || reduction >= 6)
            {
                return $"⛔ Sehr aggressives Ziel: +{gain:0.0} dB Gesamt-Gain und etwa {reduction:0.0} dB Peak-Kontrolle wären nötig. Nicht nur normalisieren – Mix/Dynamik vorher gezielt bearbeiten.";
            }

            if (gain >= 4 || reduction >= 3.5)
            {
                return $"⚠ Deutliche Bearbeitung nötig: +{gain:0.0} dB Gain, dazu etwa {reduction:0.0} dB Peak-Kontrolle. Besser mit Kompression/Automation vorbereiten statt alles dem Master-Limiter zu überlassen.";
            }

            if (gain >= 2 || reduction >= 1.5)
            {
                return $"⚠ Moderate Peak-Bearbeitung: +{gain:0.0} dB Gain und etwa {reduction:0.0} dB Peak-Kontrolle. Mit sauberem Limiting meist gut machbar, aber gegenhören.";
            }

            return $"✓ Ziel gut erreichbar: {gain:+0.0;-0.0;0.0} dB Gesamt-Gain, nur etwa {reduction:0.0} dB Peak-Kontrolle erforderlich.";
        }
    }


    public bool HasMasteringAssistantPlan => BuildMasteringAssistantPlan() is not null;

    public string MasteringAssistantLevelLabel =>
        BuildMasteringAssistantPlan()?.Level ?? "Analyse erforderlich";

    public string MasteringAssistantHeadline =>
        BuildMasteringAssistantPlan()?.Headline ?? "Projekt zuerst vollständig analysieren";

    public string MasteringAssistantSummary =>
        BuildMasteringAssistantPlan()?.Summary ??
        "Der Assistent benötigt eine vollständige Projektanalyse, bevor er konkrete Maßnahmen vorschlagen kann.";

    public string MasteringAssistantStep1 =>
        BuildMasteringAssistantPlan()?.Step1 ?? "1 · Projekt analysieren";

    public string MasteringAssistantStep2 =>
        BuildMasteringAssistantPlan()?.Step2 ?? "2 · Mastering-Ziel auswählen";

    public string MasteringAssistantStep3 =>
        BuildMasteringAssistantPlan()?.Step3 ?? "3 · Vorschlag prüfen und anwenden";

    public string MasteringAssistantAutoApplyHint =>
        BuildMasteringAssistantPlan()?.AutoApplyHint ??
        "Nach der Analyse entscheidet Elbwald, ob der Vorschlag sicher automatisch umgesetzt werden kann.";

    public bool CanAutoApplyMasteringAssistant =>
        BuildMasteringAssistantPlan()?.CanAutoApply == true && !IsBusy;

    public bool CanUndoMasteringAssistant => _masteringAssistantUndoState is not null;

    public IReadOnlyList<MasteringTrackAnalysisResult> PeakContributors =>
        _offlineMasterAnalysis?.Tracks.Take(3).ToArray() ?? [];

    public bool HasPeakContributors => PeakContributors.Count > 0;

    public IReadOnlyList<MasteringPeakEventResult> PeakEvents =>
        _offlineMasterAnalysis?.PeakEvents ?? [];

    public bool HasPeakEvents => PeakEvents.Count > 0;

    public IReadOnlyList<MasteringPeakWarningEventResult> PeakWarningEvents =>
        _offlineMasterAnalysis?.PeakWarningEvents ?? [];

    public IReadOnlyList<MasteringLevelHistoryPoint> ProjectMasterPreLimiterLevelHistory =>
        _offlineMasterAnalysis?.PreLimiterLevelHistory ?? [];

    public IReadOnlyList<MasteringLevelHistoryPoint> ProjectMasterLevelHistory =>
        _offlineMasterAnalysis?.LevelHistory ?? [];

    public int PeakWarningOnlyCount => PeakWarningEvents.Count(item => !item.IsCritical);

    public int CriticalTransientCount =>
        PeakWarningEvents.Count(item => item.IsCritical && !item.IsSustained);

    public int SustainedOverloadCount =>
        PeakWarningEvents.Count(item => item.IsCritical && item.IsSustained);

    public bool HasMasterPeakWarnings => PeakWarningEvents.Count > 0;

    public IReadOnlyList<WaveformWarningMarker> MasterWaveformWarningMarkers =>
        PeakWarningEvents
            .Select(item => new WaveformWarningMarker(
                item.IsSustained ? item.StartSeconds : item.MarkerTimeSeconds,
                item.MaxTruePeakDbtp,
                item.IsCritical
                    ? WaveformWarningSeverity.Critical
                    : WaveformWarningSeverity.Warning,
                $"{item.KindLabel} · {item.LevelReadout}",
                item.IsSustained ? item.DurationSeconds : 0d,
                item.IsSustained))
            .OrderBy(item => item.TimeSeconds)
            .ToArray();

    public string MasterPeakWarningSummaryLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null)
            {
                return "Peak-Warnung nach Projektanalyse";
            }

            if (!HasMasterPeakWarnings)
            {
                return "✓ Keine Bereiche über −3 dBTP";
            }

            var parts = new List<string>(3);
            if (CriticalTransientCount > 0)
            {
                parts.Add($"{CriticalTransientCount} kritische Transienten");
            }

            if (SustainedOverloadCount > 0)
            {
                parts.Add($"{SustainedOverloadCount} längere Übersteuerungen");
            }

            if (PeakWarningOnlyCount > 0)
            {
                parts.Add($"{PeakWarningOnlyCount} Vorwarnbereiche");
            }

            return string.Join(" · ", parts);
        }
    }

    public string MasterPeakWarningDetailLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null)
            {
                return "Projekt analysieren: Gelb warnt ab −3 dBTP, Rot markiert 0 dBTP oder mehr.";
            }

            if (!HasMasterPeakWarnings)
            {
                return "Der analysierte Master besitzt aktuell ausreichend True-Peak-Reserve.";
            }

            var strongest = PeakWarningEvents.OrderByDescending(item => item.MaxTruePeakDbtp).First();
            if (SustainedOverloadCount > 0)
            {
                return $"Rote Bänder markieren länger anhaltende Übersteuerung, rote Linien kurze Transienten. Stärkster Bereich: {strongest.LevelReadout} bei {strongest.TimeReadout}.";
            }

            if (CriticalTransientCount > 0)
            {
                return $"Die roten Marker sind kurze Transienten, keine dauerhaft übersteuerten Passagen. Stärkster Peak: {strongest.LevelReadout} bei {strongest.TimeReadout}.";
            }

            return $"Nur Vorwarnungen zwischen −3 und 0 dBTP. Stärkster Bereich: {strongest.LevelReadout} bei {strongest.TimeReadout}.";
        }
    }

    public string PeakEventHeadline
    {
        get
        {
            if (!HasPeakEvents)
            {
                return "Peak-Ereignisse nach Projektanalyse verfügbar";
            }

            var strongest = PeakEvents.OrderByDescending(item => item.ApproxTruePeakDbtp).First();
            return $"{PeakEvents.Count} Peak-Ereignisse · stärkstes bei {strongest.TimeReadout}";
        }
    }

    public string PeakEventSummary
    {
        get
        {
            if (!HasPeakEvents)
            {
                return "Elbwald lokalisiert die stärksten Summenpeaks und vergleicht dort die gleichzeitig beitragenden Spuren.";
            }

            var strongest = PeakEvents.OrderByDescending(item => item.ApproxTruePeakDbtp).First();
            return $"Stärkstes Ereignis {strongest.TruePeakReadout}. Hauptbeiträge: {strongest.ContributorsSummary}. Prozentwerte sind eine Pegel-Näherung; Phasenaddition kann die Master-Summe zusätzlich verstärken oder abschwächen.";
        }
    }

    public string PeakCauseHeadline
    {
        get
        {
            if (_offlineMasterAnalysis is null || PeakContributors.Count == 0)
            {
                return "Peak-Ursache nach Projektanalyse verfügbar";
            }

            var top = PeakContributors[0];
            var topAtMaster = top.TruePeakMaxDbtp + MasterVolumeDb;
            var sumAdvantage = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp - topAtMaster;

            return sumAdvantage >= 1.5
                ? "Die höchsten Peaks entstehen vor allem durch Summierung"
                : $"{top.TrackName} liefert den stärksten Einzelbeitrag";
        }
    }

    public string PeakCauseSummary
    {
        get
        {
            if (_offlineMasterAnalysis is null || PeakContributors.Count == 0)
            {
                return "Elbwald misst bei der Projektanalyse jede hörbare Spur nach ihren Effekten und ihrem Spur-Mixer, aber vor Master-Gain und Master-Limiter.";
            }

            var top = PeakContributors[0];
            var topAtMaster = top.TruePeakMaxDbtp + MasterVolumeDb;
            var masterPeak = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp;
            var sumAdvantage = masterPeak - topAtMaster;

            if (sumAdvantage >= 1.5)
            {
                return $"Stärkste Einzelspur: {top.TrackName} ({top.TruePeakReadout}). Der Master liegt vor dem Limiter bei {masterPeak:0.0} dBTP und damit rund {sumAdvantage:0.0} dB höher. Mehrere Spuren addieren sich zeitgleich; nicht nur eine Spur ist verantwortlich.";
            }

            return $"{top.TrackName} erreicht {top.TruePeakReadout} vor dem Master. Diese Spur ist der naheliegendste Kandidat, um Transienten zuerst gezielt zu kontrollieren, bevor der Master-Limiter stärker arbeiten muss.";
        }
    }

    public string PeakCauseActionLabel
    {
        get
        {
            if (_offlineMasterAnalysis is null || PeakContributors.Count == 0)
            {
                return "Projekt analysieren, um Peak-Beiträge zu vergleichen.";
            }

            var top = PeakContributors[0];
            var topAtMaster = top.TruePeakMaxDbtp + MasterVolumeDb;
            var sumAdvantage = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp - topAtMaster;
            var requiredReduction = RequiredPeakReductionDb ?? 0;

            if (requiredReduction <= 1.0)
            {
                return "Nur geringe Peak-Kontrolle nötig. Master-Limiter und A/B-Vergleich reichen voraussichtlich aus.";
            }

            if (sumAdvantage >= 1.5)
            {
                return "Empfehlung: zuerst die 2–3 stärksten Spuren und ihre gemeinsamen Transienten prüfen. Ein einzelner Spur-Limiter würde die Summenpeaks nicht vollständig lösen.";
            }

            return $"Empfehlung: {top.TrackName} zuerst leicht komprimieren/limitieren und danach das Projekt erneut analysieren.";
        }
    }

    public bool IsMasteringPreviewActive
    {
        get => _isMasteringPreviewActive;
        private set
        {
            if (!SetProperty(ref _isMasteringPreviewActive, value))
            {
                return;
            }

            OnPropertyChanged(nameof(MasteringPreviewButtonLabel));
            OnPropertyChanged(nameof(MasteringPreviewStateLabel));
        }
    }

    public string MasteringPreviewButtonLabel => IsMasteringPreviewActive
        ? "A/B · Original hören"
        : "A/B · Vorschlag hören";

    public string MasteringPreviewStateLabel => IsMasteringPreviewActive
        ? "Vorschau aktiv · nur Wiedergabe, noch nicht ins Projekt übernommen"
        : "Original aktiv · Vorschau verändert das Projekt nicht";



    public void SelectMasterInspector()
    {
        if (IsMasterInspectorSelected)
        {
            return;
        }

        SelectedTrack = null;
        SelectedClip = null;
        IsMasterInspectorSelected = true;
        OpenEqAnalysisCommand.NotifyCanExecuteChanged();
        StatusMessage = "Master / Gesamtprojekt ausgewählt.";
    }

    public async Task SeekToPositionAsync(double positionSeconds)
    {
        TimelineCursorSeconds = positionSeconds;

        if (PlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Paused)
        {
            await SeekToCursorAsync();
            return;
        }

        StatusMessage = $"Startposition: {FormatTimelineTime(TimelineCursorSeconds)}";
        RefreshPlaybackState();
    }

    public void BeginTimelineSeek()
    {
        _isTimelineSeekInteraction = true;
    }

    public async Task CommitTimelineSeekAsync()
    {
        _isTimelineSeekInteraction = false;
        await SeekToCursorAsync();
    }

    private async Task AnalyzeMasterAsync()
    {
        if (!HasPlayableAudio)
        {
            StatusMessage = "Keine abspielbaren Clips für die Master-Analyse.";
            return;
        }

        try
        {
            DisableMasteringPreview();
            IsBusy = true;
            IsMasterAnalyzing = true;
            MasterAnalysisProgress = 0;

            if (PlaybackState is not AudioPlaybackState.Stopped)
            {
                await _playbackService.StopAsync(resetPosition: false);
                RefreshPlaybackState();
            }

            StatusMessage = "Kompletter Mix wird für LUFS und True Peak analysiert …";
            var analysisRevision = _masterAnalysisRevision;
            var progress = new Progress<double>(value => MasterAnalysisProgress = value * 100.0);
            var result = await _masteringAnalysisService.AnalyzeAsync(CreateDocument(), progress);
            if (analysisRevision != _masterAnalysisRevision)
            {
                StatusMessage = "Master-Analyse verworfen: Der Mix wurde während der Analyse verändert.";
                return;
            }

            _offlineMasterAnalysis = result;
            MasterAnalysisProgress = 100;
            NotifyMasterAnalysisDisplayChanged();

            var strongestTrack = result.Tracks.FirstOrDefault();
            var peakHint = strongestTrack is null
                ? string.Empty
                : $" · stärkster Einzelbeitrag: {strongestTrack.TrackName} {strongestTrack.TruePeakReadout}";
            StatusMessage =
                $"Master-Analyse abgeschlossen: {result.IntegratedLufs:0.0} LUFS · True Peak {result.TruePeakMaxDbtp:0.0} dBTP · vor Limiter {result.PreLimiterIntegratedLufs:0.0} LUFS{peakHint}.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException or
            InvalidOperationException)
        {
            StatusMessage = $"Master-Analyse fehlgeschlagen: {exception.Message}";
        }
        finally
        {
            IsMasterAnalyzing = false;
            IsBusy = false;
            RefreshCommandState();
        }
    }

    private bool CanApplyLoudnessTarget() =>
        _offlineMasterAnalysis is not null && !IsBusy;

    private void ApplyLoudnessTarget()
    {
        if (_offlineMasterAnalysis is null)
        {
            return;
        }

        var gain = TargetLufs - _offlineMasterAnalysis.PreLimiterIntegratedLufs;
        var requestedVolume = MasterVolumeDb + gain;
        var appliedVolume = Math.Clamp(requestedVolume, -60, 12);
        var actuallyApplied = appliedVolume - MasterVolumeDb;

        MasterVolumeDb = appliedVolume;
        StatusMessage = Math.Abs(actuallyApplied - gain) < 0.05
            ? $"Loudness-Normalisierung {actuallyApplied:+0.0;-0.0;0.0} dB auf den gesamten Master angewendet. Für den finalen Wert erneut analysieren."
            : $"Master-Gain auf den zulässigen Bereich begrenzt ({actuallyApplied:+0.0;-0.0;0.0} dB). Für den finalen Wert erneut analysieren.";
    }

    private bool CanToggleMasteringPreview() =>
        BuildMasteringAssistantPlan() is not null && HasPlayableAudio && !IsBusy;

    private void ToggleMasteringPreview()
    {
        var plan = BuildMasteringAssistantPlan();
        if (plan is null)
        {
            StatusMessage = "Für die A/B-Vorschau zuerst das komplette Projekt analysieren.";
            return;
        }

        if (IsMasteringPreviewActive)
        {
            DisableMasteringPreview();
            StatusMessage = "A/B-Vorschau: Original-Master aktiv.";
            return;
        }

        IsMasteringPreviewActive = true;
        ApplyMasteringPreviewToPlayback(plan);
        ResetLiveMasterAnalysis();
        StatusMessage = PlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering
            ? "A/B-Vorschau: Mastering-Vorschlag aktiv. Erneut klicken für das Original."
            : "A/B-Vorschau aktiviert. Wiedergabe starten, um den Vorschlag gegen das Original zu hören.";
        RefreshMasteringAssistantCommandState();
    }

    private bool CanApplyMasteringAssistant() =>
        BuildMasteringAssistantPlan()?.CanAutoApply == true && !IsBusy;

    private async Task ApplyMasteringAssistantAsync()
    {
        var plan = BuildMasteringAssistantPlan();
        if (plan is null)
        {
            StatusMessage = "Für den Mastering-Assistenten zuerst das komplette Projekt analysieren.";
            return;
        }

        if (!plan.CanAutoApply)
        {
            StatusMessage = plan.AutoApplyHint;
            return;
        }

        if (PlaybackState is not AudioPlaybackState.Stopped)
        {
            await _playbackService.StopAsync(resetPosition: false);
            RefreshPlaybackState();
        }

        DisableMasteringPreview(updatePlayback: false);
        _masteringAssistantUndoState = new MasteringAssistantUndoState(
            MasterVolumeDb,
            MasterLimiterEnabled,
            MasterLimiterCeilingDb);
        OnPropertyChanged(nameof(CanUndoMasteringAssistant));
        UndoMasteringAssistantCommand.NotifyCanExecuteChanged();

        MasterLimiterCeilingDb = Math.Clamp(MasterLimiterCeilingDb, -12, 0);
        MasterLimiterEnabled = plan.EnableLimiter;
        MasterVolumeDb = plan.ProposedMasterVolumeDb;

        StatusMessage = "Mastering-Vorschlag angewendet. Elbwald analysiert das Ergebnis jetzt erneut …";
        await AnalyzeMasterAsync();

        if (_offlineMasterAnalysis is null)
        {
            return;
        }

        var loudnessDelta = TargetLufs - _offlineMasterAnalysis.IntegratedLufs;
        var peakDelta = _offlineMasterAnalysis.TruePeakMaxDbtp - MasterLimiterCeilingDb;
        if (Math.Abs(loudnessDelta) <= 0.5 && peakDelta <= 0.2)
        {
            StatusMessage =
                $"Automatisches Mastering abgeschlossen: {_offlineMasterAnalysis.IntegratedLufs:0.0} LUFS · {_offlineMasterAnalysis.TruePeakMaxDbtp:0.0} dBTP. A/B gegenhören und bei Bedarf rückgängig machen.";
        }
        else
        {
            StatusMessage =
                $"Automatische erste Stufe abgeschlossen: {_offlineMasterAnalysis.IntegratedLufs:0.0} LUFS · {_offlineMasterAnalysis.TruePeakMaxDbtp:0.0} dBTP. Das Ziel wird nicht weiter erzwungen; bitte A/B prüfen.";
        }
    }

    private void UndoMasteringAssistant()
    {
        var undo = _masteringAssistantUndoState;
        if (undo is null || IsBusy)
        {
            return;
        }

        DisableMasteringPreview(updatePlayback: false);
        _masteringAssistantUndoState = null;

        MasterLimiterCeilingDb = undo.LimiterCeilingDb;
        MasterLimiterEnabled = undo.LimiterEnabled;
        MasterVolumeDb = undo.MasterVolumeDb;

        OnPropertyChanged(nameof(CanUndoMasteringAssistant));
        UndoMasteringAssistantCommand.NotifyCanExecuteChanged();
        StatusMessage = "Automatische Mastering-Änderungen wurden zurückgenommen. Für aktuelle Projektwerte bitte erneut analysieren.";
    }

    private async Task JumpToPeakEventAsync(MasteringPeakEventResult? peak)
    {
        if (peak is null || IsBusy)
        {
            return;
        }

        _peakAuditionEndSeconds = null;
        SelectMasterInspector();
        await SeekToPositionAsync(peak.TimeSeconds);
        StatusMessage = $"Peak-Ereignis bei {peak.TimeReadout} ausgewählt · {peak.TruePeakReadout}.";
    }

    private async Task ListenPeakEventAsync(MasteringPeakEventResult? peak)
    {
        if (peak is null || IsBusy || !HasPlayableAudio)
        {
            return;
        }

        var start = Math.Max(0, peak.TimeSeconds - 1.25);
        var end = Math.Min(ProjectDurationSeconds, peak.TimeSeconds + 1.75);
        SelectMasterInspector();
        TimelineCursorSeconds = start;

        try
        {
            if (PlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering)
            {
                await _playbackService.SeekAsync(start);
                if (PlaybackState == AudioPlaybackState.Paused)
                {
                    await _playbackService.PlayAsync(CreatePlaybackDocument(), start);
                }
            }
            else
            {
                await PlayAsync();
            }

            if (PlaybackState != AudioPlaybackState.Playing)
            {
                _peakAuditionEndSeconds = null;
                return;
            }

            _peakAuditionEndSeconds = end;
            StatusMessage = $"Peak abhören: {peak.TimeReadout} · {peak.TruePeakReadout} · {peak.ContributorsSummary}.";
            RefreshPlaybackState();
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            NotSupportedException or
            InvalidOperationException or
            DllNotFoundException)
        {
            _peakAuditionEndSeconds = null;
            StatusMessage = $"Peak konnte nicht angehört werden: {exception.Message}";
        }
    }

    private bool CanPlay() =>
        HasPlayableAudio &&
        !IsBusy &&
        PlaybackState is not AudioPlaybackState.Playing and not AudioPlaybackState.Buffering;

    private async Task PlayAsync()
    {
        _peakAuditionEndSeconds = null;
        if (!HasPlayableAudio)
        {
            StatusMessage = "Keine abspielbaren WAV-Clips im Projekt.";
            return;
        }

        try
        {
            IsBusy = true;
            var startSeconds = TimelineCursorSeconds >= ProjectDurationSeconds - 0.001
                ? 0
                : TimelineCursorSeconds;
            if (startSeconds <= 0.001)
            {
                TimelineCursorSeconds = 0;
            }

            _liveMasterLoudness = MasterLoudnessSnapshot.Empty;
            NotifyMasterAnalysisDisplayChanged();
            StatusMessage = "Audiopuffer wird vorbereitet …";
            await _playbackService.PlayAsync(CreatePlaybackDocument(), startSeconds);
            ApplySelectedTrackFxMonitorState();
            StatusMessage = IsSelectedTrackEffectsBypassed
                ? "Wiedergabe läuft · A/B steht auf B: ausgewählte Spur wird trocken abgehört."
                : "Wiedergabe läuft · Live-LUFS und True Peak werden am Master mitgemessen.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException or
            InvalidOperationException or
            DllNotFoundException)
        {
            StatusMessage = $"Wiedergabe konnte nicht gestartet werden: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshPlaybackState();
        }
    }

    private void PausePlayback()
    {
        _playbackService.Pause();
        StatusMessage = "Wiedergabe pausiert.";
        RefreshPlaybackState();
    }

    private async Task StopPlaybackAsync()
    {
        _peakAuditionEndSeconds = null;
        var wasRepairAudition = _isRepairAuditionActive;
        if (wasRepairAudition)
        {
            _repairAuditionCts?.Cancel();
        }

        await _playbackService.StopAsync(resetPosition: !wasRepairAudition);
        TimelineCursorSeconds = wasRepairAudition
            ? Math.Clamp(_repairAuditionRestoreCursorSeconds, 0d, TimelineMaximumSeconds)
            : 0d;
        StatusMessage = wasRepairAudition ? "Reparatur-Abhörung beendet." : "Wiedergabe gestoppt.";
        RefreshPlaybackState();
    }

    private async Task SeekToCursorAsync()
    {
        _peakAuditionEndSeconds = null;
        if (PlaybackState is AudioPlaybackState.Stopped or AudioPlaybackState.Faulted)
        {
            return;
        }

        try
        {
            await _playbackService.SeekAsync(TimelineCursorSeconds);
            StatusMessage = $"Wiedergabeposition: {FormatTimelineTime(TimelineCursorSeconds)}";
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException)
        {
            StatusMessage = $"Position konnte nicht gesetzt werden: {exception.Message}";
        }
        finally
        {
            RefreshPlaybackState();
        }
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        _playbackService.Update();
        var currentState = PlaybackState;
        RefreshMeters(currentState);
        RefreshMasterLoudness(currentState);
        RefreshMasterLimiterReduction(currentState);

        if (!_isRepairAuditionActive &&
            !_isTimelineSeekInteraction &&
            currentState is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering)
        {
            var playbackPosition = _playbackService.PositionSeconds;
            if (Math.Abs(playbackPosition - _timelineCursorSeconds) > 0.005)
            {
                _timelineCursorSeconds = Math.Clamp(playbackPosition, 0, TimelineMaximumSeconds);
                OnPropertyChanged(nameof(TimelineCursorSeconds));
                OnPropertyChanged(nameof(TimelineCursorLabel));
                OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
            }

            OnPropertyChanged(nameof(PlaybackBufferLabel));
        }

        if (_peakAuditionEndSeconds is double auditionEnd &&
            currentState == AudioPlaybackState.Playing &&
            _playbackService.PositionSeconds >= auditionEnd)
        {
            _playbackService.Pause();
            _peakAuditionEndSeconds = null;
            currentState = PlaybackState;
            StatusMessage = "Peak-Abhörung beendet.";
        }

        if (_lastPlaybackState == currentState)
        {
            return;
        }

        _lastPlaybackState = currentState;
        if (currentState == AudioPlaybackState.Stopped &&
            _playbackService.PositionSeconds >= ProjectDurationSeconds - 0.01 &&
            ProjectDurationSeconds > 0)
        {
            _timelineCursorSeconds = ProjectDurationSeconds;
            OnPropertyChanged(nameof(TimelineCursorSeconds));
            OnPropertyChanged(nameof(TimelineCursorLabel));
            OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
            StatusMessage = "Wiedergabe beendet.";
        }

        RefreshPlaybackState();
    }

    private void ApplyMasterEqualizer(AudioMasterEqualizerDocument? equalizer)
    {
        equalizer ??= new AudioMasterEqualizerDocument();
        _masterEqualizerEnabled = equalizer.EqualizerEnabled;
        _masterBassDb = Math.Clamp(equalizer.BassDb, -12, 12);
        _masterMidDb = Math.Clamp(equalizer.MidDb, -12, 12);
        _masterMidFrequencyHz = Math.Clamp(equalizer.MidFrequencyHz, 120, 8_000);
        _masterTrebleDb = Math.Clamp(equalizer.TrebleDb, -12, 12);
        _masterHighPassEnabled = equalizer.HighPassEnabled;
        _masterHighPassFrequencyHz = Math.Clamp(equalizer.HighPassFrequencyHz, 20, 600);
        _masterLowPassEnabled = equalizer.LowPassEnabled;
        _masterLowPassFrequencyHz = Math.Clamp(equalizer.LowPassFrequencyHz, 2_000, 20_000);

        OnPropertyChanged(nameof(MasterEqualizerEnabled));
        OnPropertyChanged(nameof(MasterBassDb));
        OnPropertyChanged(nameof(MasterBassLabel));
        OnPropertyChanged(nameof(MasterMidDb));
        OnPropertyChanged(nameof(MasterMidLabel));
        OnPropertyChanged(nameof(MasterMidFrequencyHz));
        OnPropertyChanged(nameof(MasterMidFrequencyLabel));
        OnPropertyChanged(nameof(MasterTrebleDb));
        OnPropertyChanged(nameof(MasterTrebleLabel));
        OnPropertyChanged(nameof(MasterHighPassEnabled));
        OnPropertyChanged(nameof(MasterHighPassFrequencyHz));
        OnPropertyChanged(nameof(MasterHighPassFrequencyLabel));
        OnPropertyChanged(nameof(MasterLowPassEnabled));
        OnPropertyChanged(nameof(MasterLowPassFrequencyHz));
        OnPropertyChanged(nameof(MasterLowPassFrequencyLabel));
        _playbackService.UpdateMasterEqualizer(CreateMasterEqualizerDocument());
    }

    private bool CanSplitSelectedClip()
    {
        if (!CanEditClip || SelectedClip is not { } clip)
        {
            return false;
        }

        var localSeconds = TimelineCursorSeconds - clip.TimelineStartSeconds;
        return localSeconds >= MinimumClipEditSeconds &&
               localSeconds <= clip.DurationSeconds - MinimumClipEditSeconds;
    }

    private bool CanTrimSelectedClipStart()
    {
        if (!CanEditClip || SelectedClip is not { } clip)
        {
            return false;
        }

        var localSeconds = TimelineCursorSeconds - clip.TimelineStartSeconds;
        return localSeconds >= MinimumClipEditSeconds &&
               localSeconds <= clip.DurationSeconds - MinimumClipEditSeconds;
    }

    private bool CanTrimSelectedClipEnd() => CanTrimSelectedClipStart();

    private bool CanEditClipSelection() => CanEditClip && HasClipSelection;

    private bool CanMoveSelectedClipToCursor() =>
        CanEditClip &&
        SelectedClip is { } clip &&
        Math.Abs(clip.TimelineStartSeconds - TimelineCursorSeconds) >= MinimumClipEditSeconds;

    private void SplitSelectedClip()
    {
        if (!CanSplitSelectedClip() || SelectedClip is not { } clip)
        {
            return;
        }

        var track = FindTrackForClip(clip.Id);
        if (track is null)
        {
            return;
        }

        var splitSeconds = Math.Clamp(
            TimelineCursorSeconds - clip.TimelineStartSeconds,
            MinimumClipEditSeconds,
            clip.DurationSeconds - MinimumClipEditSeconds);
        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var index = after.FindIndex(item => item.Id == clip.Id);
        if (index < 0)
        {
            return;
        }

        var source = after[index];
        var left = CreateClipSegment(
            source,
            0d,
            splitSeconds,
            source.TimelineStartSeconds,
            source.Id,
            source.Name);
        var right = CreateClipSegment(
            source,
            splitSeconds,
            source.DurationSeconds,
            source.TimelineStartSeconds + splitSeconds,
            Guid.NewGuid(),
            source.Name + " · Teil 2");

        after[index] = left;
        after.Insert(index + 1, right);
        CommitClipEdit("Clip geteilt", track, before, after, left.Id);
    }

    private void TrimSelectedClipStart()
    {
        if (!CanTrimSelectedClipStart() || SelectedClip is not { } clip)
        {
            return;
        }

        var track = FindTrackForClip(clip.Id);
        if (track is null)
        {
            return;
        }

        var trimSeconds = Math.Clamp(
            TimelineCursorSeconds - clip.TimelineStartSeconds,
            MinimumClipEditSeconds,
            clip.DurationSeconds - MinimumClipEditSeconds);
        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var index = after.FindIndex(item => item.Id == clip.Id);
        if (index < 0)
        {
            return;
        }

        var source = after[index];
        after[index] = CreateClipSegment(
            source,
            trimSeconds,
            source.DurationSeconds,
            source.TimelineStartSeconds + trimSeconds,
            source.Id,
            source.Name);
        CommitClipEdit("Clip-Anfang getrimmt", track, before, after, source.Id);
    }

    private void TrimSelectedClipEnd()
    {
        if (!CanTrimSelectedClipEnd() || SelectedClip is not { } clip)
        {
            return;
        }

        var track = FindTrackForClip(clip.Id);
        if (track is null)
        {
            return;
        }

        var trimSeconds = Math.Clamp(
            TimelineCursorSeconds - clip.TimelineStartSeconds,
            MinimumClipEditSeconds,
            clip.DurationSeconds - MinimumClipEditSeconds);
        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var index = after.FindIndex(item => item.Id == clip.Id);
        if (index < 0)
        {
            return;
        }

        var source = after[index];
        after[index] = CreateClipSegment(
            source,
            0d,
            trimSeconds,
            source.TimelineStartSeconds,
            source.Id,
            source.Name);
        CommitClipEdit("Clip-Ende getrimmt", track, before, after, source.Id);
    }

    private void RemoveSelectedClipRange()
    {
        if (!CanEditClipSelection() ||
            SelectedClip is not { } clip ||
            !TryGetClipSelectionLocalRange(out var selectionStart, out var selectionEnd))
        {
            return;
        }

        var track = FindTrackForClip(clip.Id);
        if (track is null)
        {
            return;
        }

        selectionStart = Math.Clamp(selectionStart, 0d, clip.DurationSeconds);
        selectionEnd = Math.Clamp(selectionEnd, selectionStart, clip.DurationSeconds);
        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var index = after.FindIndex(item => item.Id == clip.Id);
        if (index < 0)
        {
            return;
        }

        var source = after[index];
        Guid? selectedAfterId;
        if (selectionStart <= MinimumClipEditSeconds &&
            selectionEnd >= source.DurationSeconds - MinimumClipEditSeconds)
        {
            after.RemoveAt(index);
            selectedAfterId = after.Count == 0
                ? null
                : after[Math.Min(index, after.Count - 1)].Id;
        }
        else if (selectionStart <= MinimumClipEditSeconds)
        {
            var remainder = CreateClipSegment(
                source,
                selectionEnd,
                source.DurationSeconds,
                source.TimelineStartSeconds,
                source.Id,
                source.Name);
            after[index] = remainder;
            selectedAfterId = remainder.Id;
        }
        else if (selectionEnd >= source.DurationSeconds - MinimumClipEditSeconds)
        {
            var remainder = CreateClipSegment(
                source,
                0d,
                selectionStart,
                source.TimelineStartSeconds,
                source.Id,
                source.Name);
            after[index] = remainder;
            selectedAfterId = remainder.Id;
        }
        else
        {
            var left = CreateClipSegment(
                source,
                0d,
                selectionStart,
                source.TimelineStartSeconds,
                source.Id,
                source.Name);
            var right = CreateClipSegment(
                source,
                selectionEnd,
                source.DurationSeconds,
                source.TimelineStartSeconds + selectionStart,
                Guid.NewGuid(),
                source.Name + " · Rest");
            after[index] = left;
            after.Insert(index + 1, right);
            selectedAfterId = right.Id;
        }

        CommitClipEdit("Markierten Bereich entfernt", track, before, after, selectedAfterId);
    }

    private void MuteSelectedClipRange()
    {
        if (!CanEditClipSelection() ||
            SelectedClip is not { } clip ||
            !TryGetClipSelectionLocalRange(out var selectionStart, out var selectionEnd))
        {
            return;
        }

        var track = FindTrackForClip(clip.Id);
        if (track is null)
        {
            return;
        }

        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var target = after.FirstOrDefault(item => item.Id == clip.Id);
        if (target is null)
        {
            return;
        }

        target.MutedRanges.Add(new AudioClipMuteRangeDocument
        {
            StartSeconds = selectionStart,
            DurationSeconds = selectionEnd - selectionStart
        });
        target.MutedRanges = NormalizeMuteRanges(target.MutedRanges, target.DurationSeconds);
        CommitClipEdit("Markierten Bereich stummgeschaltet", track, before, after, target.Id);
    }

    private void MoveSelectedClipToCursor()
    {
        if (SelectedClip is null)
        {
            return;
        }

        SetSelectedClipTimelineStart(TimelineCursorSeconds);
    }

    private void SetSelectedClipTimelineStart(double value)
    {
        if (!CanEditClip || SelectedClip is not { } clip)
        {
            return;
        }

        var normalized = Math.Max(0d, value);
        if (Math.Abs(clip.TimelineStartSeconds - normalized) < 0.000_001)
        {
            return;
        }

        CommitSingleClipDocumentChange(
            "Clip verschoben",
            clip.Id,
            document => document.TimelineStartSeconds = normalized);
    }

    private void SetSelectedClipFadeIn(double value)
    {
        if (!CanEditClip || SelectedClip is not { } clip)
        {
            return;
        }

        var normalized = Math.Clamp(value, 0d, clip.DurationSeconds);
        if (Math.Abs(clip.FadeInSeconds - normalized) < 0.000_001)
        {
            return;
        }

        CommitSingleClipDocumentChange(
            "Fade-In geändert",
            clip.Id,
            document => document.FadeInSeconds = normalized);
    }

    private void SetSelectedClipFadeOut(double value)
    {
        if (!CanEditClip || SelectedClip is not { } clip)
        {
            return;
        }

        var normalized = Math.Clamp(value, 0d, clip.DurationSeconds);
        if (Math.Abs(clip.FadeOutSeconds - normalized) < 0.000_001)
        {
            return;
        }

        CommitSingleClipDocumentChange(
            "Fade-Out geändert",
            clip.Id,
            document => document.FadeOutSeconds = normalized);
    }

    private void CommitSingleClipDocumentChange(
        string description,
        Guid clipId,
        Action<AudioClipDocument> change)
    {
        var track = FindTrackForClip(clipId);
        if (track is null)
        {
            return;
        }

        var before = CaptureClipSnapshot(track);
        var after = CloneClipDocuments(before);
        var target = after.FirstOrDefault(item => item.Id == clipId);
        if (target is null)
        {
            return;
        }

        change(target);
        CommitClipEdit(description, track, before, after, clipId);
    }

    private void CommitClipEdit(
        string description,
        AudioTrackViewModel track,
        List<AudioClipDocument> before,
        List<AudioClipDocument> after,
        Guid? selectedAfterId)
    {
        var selectedBeforeId = SelectedClip?.Id;
        ApplyClipSnapshot(track, after, selectedAfterId);
        _clipEditUndo.Push(new ClipEditUndoEntry(
            track.Id,
            CloneClipDocuments(before),
            CloneClipDocuments(after),
            selectedBeforeId,
            selectedAfterId,
            description));
        _clipEditRedo.Clear();
        StatusMessage = description + ".";
        RefreshClipEditCommandState();
    }

    private void UndoClipEdit()
    {
        if (_clipEditUndo.Count == 0 || !CanUseClipEditHistory)
        {
            return;
        }

        var entry = _clipEditUndo.Pop();
        var track = Tracks.FirstOrDefault(item => item.Id == entry.TrackId);
        if (track is null)
        {
            _clipEditUndo.Clear();
            _clipEditRedo.Clear();
            RefreshClipEditCommandState();
            return;
        }

        ApplyClipSnapshot(track, entry.BeforeClips, entry.SelectedClipBeforeId);
        _clipEditRedo.Push(entry);
        StatusMessage = $"Rückgängig: {entry.Description}.";
        RefreshClipEditCommandState();
    }

    private void RedoClipEdit()
    {
        if (_clipEditRedo.Count == 0 || !CanUseClipEditHistory)
        {
            return;
        }

        var entry = _clipEditRedo.Pop();
        var track = Tracks.FirstOrDefault(item => item.Id == entry.TrackId);
        if (track is null)
        {
            _clipEditUndo.Clear();
            _clipEditRedo.Clear();
            RefreshClipEditCommandState();
            return;
        }

        ApplyClipSnapshot(track, entry.AfterClips, entry.SelectedClipAfterId);
        _clipEditUndo.Push(entry);
        StatusMessage = $"Wiederholt: {entry.Description}.";
        RefreshClipEditCommandState();
    }

    private void ApplyClipSnapshot(
        AudioTrackViewModel track,
        IReadOnlyList<AudioClipDocument> documents,
        Guid? selectedClipId)
    {
        var waveformBySource = track.Clips
            .Where(clip => clip.Waveform is not null)
            .GroupBy(clip => clip.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Waveform!, StringComparer.OrdinalIgnoreCase);

        var clips = documents
            .Select(document => AudioClipViewModel.FromDocument(CloneClipDocument(document)))
            .ToList();
        foreach (var clip in clips)
        {
            if (waveformBySource.TryGetValue(clip.SourcePath, out var waveform))
            {
                clip.SetWaveform(waveform);
            }
        }

        _isApplyingClipEditSnapshot = true;
        _suppressDirtyTracking = true;
        try
        {
            track.ReplaceClips(clips);
            SelectedTrack = track;
            SelectedClip = selectedClipId is Guid id
                ? clips.FirstOrDefault(clip => clip.Id == id) ?? clips.FirstOrDefault()
                : clips.FirstOrDefault();
        }
        finally
        {
            _suppressDirtyTracking = false;
            _isApplyingClipEditSnapshot = false;
        }

        ClearClipSelection();
        ResetLiveMasterAnalysis();
        InvalidateOfflineMasterAnalysis();
        RefreshProjectState();
        MarkDirty();

        foreach (var clip in clips.Where(clip => !clip.HasWaveform && !clip.IsMissing))
        {
            _ = LoadWaveformAsync(clip);
        }
    }

    private AudioTrackViewModel? FindTrackForClip(Guid clipId) =>
        Tracks.FirstOrDefault(track => track.Clips.Any(clip => clip.Id == clipId));

    private static List<AudioClipDocument> CaptureClipSnapshot(AudioTrackViewModel track) =>
        track.Clips.Select(clip => CloneClipDocument(clip.ToDocument())).ToList();

    private static List<AudioClipDocument> CloneClipDocuments(IEnumerable<AudioClipDocument> clips) =>
        clips.Select(CloneClipDocument).ToList();

    private static AudioClipDocument CloneClipDocument(AudioClipDocument clip) =>
        new()
        {
            Id = clip.Id,
            Name = clip.Name,
            SourcePath = clip.SourcePath,
            SourcePathIsRelative = clip.SourcePathIsRelative,
            TimelineStartSeconds = clip.TimelineStartSeconds,
            SourceStartSeconds = clip.SourceStartSeconds,
            DurationSeconds = clip.DurationSeconds,
            GainDb = clip.GainDb,
            FadeInSeconds = clip.FadeInSeconds,
            FadeOutSeconds = clip.FadeOutSeconds,
            MutedRanges = clip.MutedRanges.Select(CloneMuteRange).ToList(),
            SourceSampleRate = clip.SourceSampleRate,
            SourceChannels = clip.SourceChannels,
            SourceBitsPerSample = clip.SourceBitsPerSample,
            SourceEncoding = clip.SourceEncoding
        };

    private static AudioClipDocument CreateClipSegment(
        AudioClipDocument source,
        double localStartSeconds,
        double localEndSeconds,
        double timelineStartSeconds,
        Guid id,
        string name)
    {
        var start = Math.Clamp(localStartSeconds, 0d, source.DurationSeconds);
        var end = Math.Clamp(localEndSeconds, start, source.DurationSeconds);
        var duration = Math.Max(0d, end - start);
        return new AudioClipDocument
        {
            Id = id,
            Name = name,
            SourcePath = source.SourcePath,
            SourcePathIsRelative = source.SourcePathIsRelative,
            TimelineStartSeconds = Math.Max(0d, timelineStartSeconds),
            SourceStartSeconds = source.SourceStartSeconds + start,
            DurationSeconds = duration,
            GainDb = source.GainDb,
            FadeInSeconds = start <= MinimumClipEditSeconds
                ? Math.Min(source.FadeInSeconds, duration)
                : 0d,
            FadeOutSeconds = end >= source.DurationSeconds - MinimumClipEditSeconds
                ? Math.Min(source.FadeOutSeconds, duration)
                : 0d,
            MutedRanges = SliceMuteRanges(source.MutedRanges, start, end),
            SourceSampleRate = source.SourceSampleRate,
            SourceChannels = source.SourceChannels,
            SourceBitsPerSample = source.SourceBitsPerSample,
            SourceEncoding = source.SourceEncoding
        };
    }

    private static List<AudioClipMuteRangeDocument> SliceMuteRanges(
        IReadOnlyList<AudioClipMuteRangeDocument> ranges,
        double segmentStartSeconds,
        double segmentEndSeconds)
    {
        var result = new List<AudioClipMuteRangeDocument>();
        foreach (var range in ranges)
        {
            var rangeStart = range.StartSeconds;
            var rangeEnd = range.StartSeconds + range.DurationSeconds;
            var overlapStart = Math.Max(rangeStart, segmentStartSeconds);
            var overlapEnd = Math.Min(rangeEnd, segmentEndSeconds);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            result.Add(new AudioClipMuteRangeDocument
            {
                StartSeconds = overlapStart - segmentStartSeconds,
                DurationSeconds = overlapEnd - overlapStart
            });
        }

        return NormalizeMuteRanges(result, Math.Max(0d, segmentEndSeconds - segmentStartSeconds));
    }

    private static List<AudioClipMuteRangeDocument> NormalizeMuteRanges(
        IEnumerable<AudioClipMuteRangeDocument> ranges,
        double durationSeconds)
    {
        var ordered = ranges
            .Select(range => new AudioClipMuteRangeDocument
            {
                StartSeconds = Math.Clamp(range.StartSeconds, 0d, durationSeconds),
                DurationSeconds = Math.Max(0d, range.DurationSeconds)
            })
            .Select(range => new AudioClipMuteRangeDocument
            {
                StartSeconds = range.StartSeconds,
                DurationSeconds = Math.Min(range.DurationSeconds, durationSeconds - range.StartSeconds)
            })
            .Where(range => range.DurationSeconds >= MinimumClipEditSeconds)
            .OrderBy(range => range.StartSeconds)
            .ToList();

        var merged = new List<AudioClipMuteRangeDocument>(ordered.Count);
        foreach (var range in ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(CloneMuteRange(range));
                continue;
            }

            var previous = merged[^1];
            var previousEnd = previous.StartSeconds + previous.DurationSeconds;
            var currentEnd = range.StartSeconds + range.DurationSeconds;
            if (range.StartSeconds <= previousEnd + MinimumClipEditSeconds)
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

    private static AudioClipMuteRangeDocument CloneMuteRange(AudioClipMuteRangeDocument range) =>
        new()
        {
            StartSeconds = range.StartSeconds,
            DurationSeconds = range.DurationSeconds
        };

    private double NormalizeClipSelectionSourceSecond(double value)
    {
        if (SelectedClip is not { } clip || value < 0d)
        {
            return -1d;
        }

        return Math.Clamp(
            value,
            clip.SourceStartSeconds,
            clip.SourceStartSeconds + clip.DurationSeconds);
    }

    private bool TryGetClipSelectionLocalRange(out double startSeconds, out double endSeconds)
    {
        startSeconds = 0d;
        endSeconds = 0d;
        if (SelectedClip is not { } clip ||
            _clipSelectionStartSourceSeconds < 0d ||
            _clipSelectionEndSourceSeconds < 0d)
        {
            return false;
        }

        var sourceStart = Math.Min(_clipSelectionStartSourceSeconds, _clipSelectionEndSourceSeconds);
        var sourceEnd = Math.Max(_clipSelectionStartSourceSeconds, _clipSelectionEndSourceSeconds);
        startSeconds = Math.Clamp(sourceStart - clip.SourceStartSeconds, 0d, clip.DurationSeconds);
        endSeconds = Math.Clamp(sourceEnd - clip.SourceStartSeconds, startSeconds, clip.DurationSeconds);
        return true;
    }

    private void ClearClipSelection()
    {
        var changed = _clipSelectionStartSourceSeconds >= 0d || _clipSelectionEndSourceSeconds >= 0d;
        _clipSelectionStartSourceSeconds = -1d;
        _clipSelectionEndSourceSeconds = -1d;
        if (!changed)
        {
            RefreshClipEditCommandState();
            return;
        }

        OnPropertyChanged(nameof(ClipSelectionStartSourceSeconds));
        OnPropertyChanged(nameof(ClipSelectionEndSourceSeconds));
        NotifyClipSelectionChanged();
    }

    private void NotifyClipSelectionChanged()
    {
        OnPropertyChanged(nameof(HasClipSelection));
        OnPropertyChanged(nameof(ClipSelectionDurationSeconds));
        OnPropertyChanged(nameof(ClipSelectionLabel));
        RefreshClipEditCommandState();
    }

    private void RefreshClipEditCommandState()
    {
        OnPropertyChanged(nameof(CanUseClipEditHistory));
        OnPropertyChanged(nameof(CanEditClip));
        SplitClipCommand.NotifyCanExecuteChanged();
        TrimClipStartCommand.NotifyCanExecuteChanged();
        TrimClipEndCommand.NotifyCanExecuteChanged();
        RemoveClipSelectionCommand.NotifyCanExecuteChanged();
        MuteClipSelectionCommand.NotifyCanExecuteChanged();
        ClearClipSelectionCommand.NotifyCanExecuteChanged();
        MoveClipToCursorCommand.NotifyCanExecuteChanged();
        UndoClipEditCommand.NotifyCanExecuteChanged();
        RedoClipEditCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPlaybackState()
    {
        OnPropertyChanged(nameof(PlaybackState));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPlaybackPaused));
        OnPropertyChanged(nameof(IsPlaybackBusy));
        OnPropertyChanged(nameof(PlaybackStateLabel));
        OnPropertyChanged(nameof(PlaybackBufferLabel));
        OnPropertyChanged(nameof(HasPlayableAudio));
        OnPropertyChanged(nameof(CanEditClip));
        NotifyMasterAnalysisDisplayChanged();
        RefreshClipEditCommandState();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekToCursorCommand.NotifyCanExecuteChanged();
        AnalyzeMasterCommand.NotifyCanExecuteChanged();
        ApplyLoudnessTargetCommand.NotifyCanExecuteChanged();
        ToggleMasteringPreviewCommand.NotifyCanExecuteChanged();
        ApplyMasteringAssistantCommand.NotifyCanExecuteChanged();
        UndoMasteringAssistantCommand.NotifyCanExecuteChanged();
        JumpToPeakEventCommand.NotifyCanExecuteChanged();
        ListenPeakEventCommand.NotifyCanExecuteChanged();
        OpenEqAnalysisCommand.NotifyCanExecuteChanged();
    }

    private async Task StopPlaybackForProjectChangeAsync()
    {
        if (PlaybackState is AudioPlaybackState.Stopped)
        {
            return;
        }

        await _playbackService.StopAsync(resetPosition: true);
        _timelineCursorSeconds = 0;
        OnPropertyChanged(nameof(TimelineCursorSeconds));
        OnPropertyChanged(nameof(TimelineCursorLabel));
        OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
        RefreshPlaybackState();
    }

    private async Task NewProjectAsync()
    {
        if (HasUnsavedChanges)
        {
            StatusMessage = "Projekt nicht verworfen: Bitte Änderungen zuerst speichern. Ein Verwerfen-Dialog folgt separat.";
            return;
        }

        await StopPlaybackForProjectChangeAsync();
        ResetProject(createInitialTrack: true);
        StatusMessage = "Neues Standalone-Audioprojekt vorbereitet.";
    }

    private async Task ImportWavAsync()
    {
        try
        {
            IsBusy = true;
            var filePaths = await _storagePicker.PickWavFilesAsync();
            if (filePaths.Count == 0)
            {
                StatusMessage = "WAV-Import abgebrochen.";
                return;
            }

            await StopPlaybackForProjectChangeAsync();

            var importedCount = 0;
            var rejected = new List<string>();

            foreach (var filePath in filePaths)
            {
                if (Tracks.Count >= AudioProjectLimits.MaxTrackCount &&
                    FindReusableEmptyTrack() is null)
                {
                    rejected.Add($"{Path.GetFileName(filePath)}: Spurlimit erreicht");
                    continue;
                }

                try
                {
                    var info = await _wavMetadataReader.ReadAsync(filePath);
                    var channelLayout = info.Channels == 1
                        ? AudioChannelLayout.Mono
                        : AudioChannelLayout.Stereo;

                    var track = FindReusableEmptyTrack();
                    if (track is null)
                    {
                        track = AddTrack(channelLayout, markDirty: false);
                    }
                    else
                    {
                        track.ChannelLayout = channelLayout;
                    }

                    if (track is null)
                    {
                        rejected.Add($"{Path.GetFileName(filePath)}: keine freie Spur");
                        continue;
                    }

                    track.Name = Path.GetFileNameWithoutExtension(info.FilePath);
                    var clip = AudioClipViewModel.FromWav(info, timelineStartSeconds: 0);
                    track.AddClip(clip);
                    SelectedTrack = track;
                    SelectedClip = clip;
                    await LoadWaveformAsync(clip);

                    if (ClipCount == 1 && info.SampleRate > 0)
                    {
                        ProjectSampleRate = info.SampleRate;
                    }

                    importedCount++;
                }
                catch (Exception exception) when (
                    exception is IOException or
                    UnauthorizedAccessException or
                    InvalidDataException or
                    NotSupportedException)
                {
                    rejected.Add($"{Path.GetFileName(filePath)}: {exception.Message}");
                }
            }

            if (importedCount > 0)
            {
                MarkDirty();
                RefreshProjectState();
            }

            StatusMessage = rejected.Count == 0
                ? $"{importedCount} WAV-Datei(en) importiert. Originaldateien bleiben unverändert."
                : $"{importedCount} importiert · {rejected.Count} nicht importiert: {string.Join(" | ", rejected.Take(2))}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenProjectAsync()
    {
        if (HasUnsavedChanges)
        {
            StatusMessage = "Projekt nicht geöffnet: Bitte aktuelle Änderungen zuerst speichern.";
            return;
        }

        try
        {
            IsBusy = true;
            var projectPath = await _storagePicker.PickProjectToOpenAsync();
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                StatusMessage = "Projekt öffnen abgebrochen.";
                return;
            }

            var document = await _projectSerializer.LoadAsync(projectPath);
            await StopPlaybackForProjectChangeAsync();
            ApplyDocument(document, Path.GetFullPath(projectPath));
            await LoadProjectWaveformsAsync();

            StatusMessage = MissingSourceCount == 0
                ? $"Projekt '{ProjectName}' geöffnet. Alle Quelldateien wurden gefunden."
                : $"Projekt '{ProjectName}' geöffnet. {MissingSourceLabel}.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException or
            System.Text.Json.JsonException)
        {
            StatusMessage = $"Projekt konnte nicht geöffnet werden: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportWavAsync()
    {
        if (!HasPlayableAudio || IsBusy)
        {
            return;
        }

        try
        {
            var document = CreateDocument();
            var exportedPath = await _audioExportWindowService.ShowAsync(
                document,
                $"{ProjectName}-Mixdown");

            if (!string.IsNullOrWhiteSpace(exportedPath))
            {
                StatusMessage = $"WAV exportiert: {Path.GetFileName(exportedPath)}";
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException)
        {
            StatusMessage = $"Exportfenster konnte nicht abgeschlossen werden: {exception.Message}";
        }
    }

    private async Task SaveProjectAsync()
    {
        try
        {
            IsBusy = true;

            var projectPath = CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = await _storagePicker.PickProjectToSaveAsync(ProjectName);
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    StatusMessage = "Speichern abgebrochen.";
                    return;
                }
            }

            await SaveToPathAsync(projectPath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            StatusMessage = $"Projekt konnte nicht gespeichert werden: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveProjectAsAsync()
    {
        try
        {
            IsBusy = true;
            var projectPath = await _storagePicker.PickProjectToSaveAsync(ProjectName);
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                StatusMessage = "Speichern unter abgebrochen.";
                return;
            }

            await SaveToPathAsync(projectPath);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            StatusMessage = $"Projekt konnte nicht gespeichert werden: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveToPathAsync(string projectPath)
    {
        var fullPath = string.Equals(Path.GetExtension(projectPath), ".ewaudio", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(projectPath)
            : Path.GetFullPath(projectPath + ".ewaudio");

        var targetProjectName = Path.GetFileNameWithoutExtension(fullPath);
        var document = CreateDocument();
        document.Name = targetProjectName;

        await _projectSerializer.SaveAsync(fullPath, document);

        _suppressDirtyTracking = true;
        ProjectName = targetProjectName;
        _suppressDirtyTracking = false;
        CurrentProjectPath = fullPath;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(ProjectTitle));
        StatusMessage = $"Projekt gespeichert: {Path.GetFileName(fullPath)}";
    }

    private AudioProjectDocument CreateDocument()
    {
        var document = new AudioProjectDocument
        {
            Name = ProjectName,
            SampleRate = ProjectSampleRate,
            Master = new AudioMasterDocument
            {
                VolumeDb = MasterVolumeDb,
                TargetLufs = TargetLufs,
                LimiterEnabled = MasterLimiterEnabled,
                LimiterCeilingDb = MasterLimiterCeilingDb,
                Equalizer = CreateMasterEqualizerDocument()
            }
        };

        document.Tracks.AddRange(Tracks.Select(track => track.ToDocument()));
        return document;
    }

    private void ApplyDocument(AudioProjectDocument document, string projectPath)
    {
        _suppressDirtyTracking = true;
        ClearTracks();

        ProjectName = document.Name;
        ProjectSampleRate = document.SampleRate;
        _liveMasterLoudness = MasterLoudnessSnapshot.Empty;
        _offlineMasterAnalysis = null;
        _isMasteringPreviewActive = false;
        _masteringAssistantUndoState = null;
        MasterAnalysisProgress = 0;
        MasterVolumeDb = document.Master.VolumeDb;
        TargetLufs = document.Master.TargetLufs;
        MasterLimiterEnabled = document.Master.LimiterEnabled;
        MasterLimiterCeilingDb = document.Master.LimiterCeilingDb;
        ApplyMasterEqualizer(document.Master.Equalizer);
        SyncMasteringPresetFromTargets();

        for (var index = 0; index < document.Tracks.Count; index++)
        {
            var track = AudioTrackViewModel.FromDocument(
                index + 1,
                document.Tracks[index],
                RemoveTrack);
            track.PropertyChanged += OnTrackPropertyChanged;
            Tracks.Add(track);
        }

        SelectedTrack = Tracks.FirstOrDefault();
        CurrentProjectPath = projectPath;
        _suppressDirtyTracking = false;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(IsMasteringPreviewActive));
        OnPropertyChanged(nameof(MasteringPreviewButtonLabel));
        OnPropertyChanged(nameof(MasteringPreviewStateLabel));
        OnPropertyChanged(nameof(CanUndoMasteringAssistant));
        RefreshMasteringAssistantCommandState();
        RefreshProjectState();
    }

    private void ResetProject(bool createInitialTrack)
    {
        _suppressDirtyTracking = true;
        ClearTracks();

        CurrentProjectPath = null;
        ProjectName = "Unbenannt";
        ProjectSampleRate = 48_000;
        MasterVolumeDb = 0;
        TargetLufs = -12;
        MasterLimiterEnabled = true;
        MasterLimiterCeilingDb = -1;
        ApplyMasterEqualizer(new AudioMasterEqualizerDocument());
        TimelineCursorSeconds = 0;
        TimelineZoom = 80;
        _liveMasterLoudness = MasterLoudnessSnapshot.Empty;
        _offlineMasterAnalysis = null;
        _isMasteringPreviewActive = false;
        _masteringAssistantUndoState = null;
        MasterAnalysisProgress = 0;
        SetMasterMeterLevels(-60, -60, immediate: true);
        NotifyMasterAnalysisDisplayChanged();
        OnPropertyChanged(nameof(IsMasteringPreviewActive));
        OnPropertyChanged(nameof(MasteringPreviewButtonLabel));
        OnPropertyChanged(nameof(MasteringPreviewStateLabel));
        OnPropertyChanged(nameof(CanUndoMasteringAssistant));
        RefreshMasteringAssistantCommandState();

        if (createInitialTrack)
        {
            _ = AddTrack(AudioChannelLayout.Stereo, markDirty: false);
        }

        _suppressDirtyTracking = false;
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(ProjectTitle));
        RefreshProjectState();
    }

    private AudioTrackViewModel? AddTrack(
        AudioChannelLayout channelLayout,
        bool markDirty = true)
    {
        if (!CanAddTrack)
        {
            StatusMessage = $"Maximal {AudioProjectLimits.MaxTrackCount} Spuren sind vorgesehen.";
            return null;
        }

        var track = new AudioTrackViewModel(
            Tracks.Count + 1,
            channelLayout,
            RemoveTrack)
        {
            WaveformAutoScale = IsWaveformAutoScale
        };

        track.PropertyChanged += OnTrackPropertyChanged;
        Tracks.Add(track);
        SelectedTrack = track;

        RefreshProjectState();
        if (markDirty)
        {
            MarkDirty();
            StatusMessage = $"{track.ChannelLabel}-Spur {track.TrackNumber:00} hinzugefügt.";
        }

        return track;
    }

    private AudioTrackViewModel? FindReusableEmptyTrack()
    {
        if (SelectedTrack is { HasClips: false })
        {
            return SelectedTrack;
        }

        return Tracks.FirstOrDefault(track => !track.HasClips);
    }

    private void RemoveTrack(AudioTrackViewModel track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var removedIndex = Tracks.IndexOf(track);
        if (removedIndex < 0)
        {
            return;
        }

        _playbackService.UpdateTrackMixer(track.Id, true, false, -60, 0);
        track.SetMeterLevels(-60, -60, immediate: true);
        track.PropertyChanged -= OnTrackPropertyChanged;
        Tracks.RemoveAt(removedIndex);

        for (var index = 0; index < Tracks.Count; index++)
        {
            Tracks[index].TrackNumber = index + 1;
        }

        SelectedTrack = Tracks.Count == 0
            ? null
            : Tracks[Math.Min(removedIndex, Tracks.Count - 1)];

        RefreshProjectState();
        MarkDirty();
        StatusMessage = "Spur entfernt.";
    }

    private void ClearTracks()
    {
        foreach (var track in Tracks)
        {
            track.PropertyChanged -= OnTrackPropertyChanged;
        }

        Tracks.Clear();
        SelectedTrack = null;
        SelectedClip = null;
        _clipEditUndo.Clear();
        _clipEditRedo.Clear();
        RefreshClipEditCommandState();
    }

    public async Task<StemRepairAuditionClip> AuditionStemRepairAsync(
        AudioTrackViewModel track,
        StemRepairFinding finding)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(finding);

        _repairAuditionCts?.Cancel();
        await _repairAuditionGate.WaitAsync();

        _repairAuditionCts?.Dispose();
        _repairAuditionCts = new CancellationTokenSource();
        var currentCts = _repairAuditionCts;
        var token = currentCts.Token;
        var restoreCursor = TimelineCursorSeconds;
        _repairAuditionRestoreCursorSeconds = restoreCursor;
        _isRepairAuditionActive = true;
        _peakAuditionEndSeconds = null;

        StemRepairAuditionClip? clip = null;
        try
        {
            await _playbackService.StopAsync(resetPosition: false);
            clip = await _stemRepairAuditionService.CreateRemovedSignalAsync(
                track,
                finding,
                restoreCursor,
                ProjectSampleRate,
                token);
            _activeRepairAuditionClip = clip;

            var auditionDocument = CreateRepairAuditionDocument(track, clip);
            StatusMessage = $"Entferntes Signal wird vorbereitet · {track.Name}.";
            await _playbackService.PlayAsync(auditionDocument, 0d, token);
            RefreshPlaybackState();

            var boostLabel = clip.MonitorGainDb > 0.05d
                ? $" · Abhörpegel +{clip.MonitorGainDb:0.#} dB"
                : string.Empty;
            StatusMessage = $"Störung vorhören · nur der vom vorgeschlagenen Filter entfernte Anteil{boostLabel}.";

            await Task.Delay(TimeSpan.FromSeconds(clip.DurationSeconds), token);
            return clip;
        }
        finally
        {
            try
            {
                await _playbackService.StopAsync(resetPosition: false);
            }
            catch
            {
                // Preserve the original audition exception/cancellation.
            }

            _isRepairAuditionActive = false;
            _timelineCursorSeconds = Math.Clamp(_repairAuditionRestoreCursorSeconds, 0d, TimelineMaximumSeconds);
            OnPropertyChanged(nameof(TimelineCursorSeconds));
            OnPropertyChanged(nameof(TimelineCursorLabel));
            OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
            RefreshPlaybackState();

            var finishedClip = _activeRepairAuditionClip ?? clip;
            _activeRepairAuditionClip = null;
            _stemRepairAuditionService.TryDelete(finishedClip);

            if (ReferenceEquals(_repairAuditionCts, currentCts))
            {
                _repairAuditionCts.Dispose();
                _repairAuditionCts = null;
            }

            _repairAuditionGate.Release();
        }
    }

    private AudioProjectDocument CreateRepairAuditionDocument(
        AudioTrackViewModel sourceTrack,
        StemRepairAuditionClip clip)
    {
        var document = new AudioProjectDocument
        {
            Name = $"Repair-Audition · {sourceTrack.Name}",
            SampleRate = clip.SampleRate,
            Master = new AudioMasterDocument
            {
                VolumeDb = 0d,
                LimiterEnabled = false,
                LimiterCeilingDb = 0d
            }
        };

        var track = new AudioTrackDocument
        {
            Name = sourceTrack.Name,
            ChannelLayout = AudioChannelLayout.Stereo,
            VolumeDb = 0d,
            Pan = 0d,
            Effects = new AudioTrackEffectsDocument()
        };
        track.Clips.Add(new AudioClipDocument
        {
            Name = "Entferntes Signal",
            SourcePath = clip.FilePath,
            SourcePathIsRelative = false,
            TimelineStartSeconds = 0d,
            SourceStartSeconds = 0d,
            DurationSeconds = clip.DurationSeconds,
            GainDb = 0d,
            SourceSampleRate = clip.SampleRate,
            SourceChannels = 2,
            SourceBitsPerSample = 16,
            SourceEncoding = "PCM"
        });
        document.Tracks.Add(track);
        return document;
    }

    public void OpenStemRepairAnalysis(AudioTrackViewModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        SelectedTrack = track;
        _stemRepairWindowService.Show(this, track);
    }

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingClipEditSnapshot)
        {
            return;
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.MeterLeftDb) or
            nameof(AudioTrackViewModel.MeterRightDb) or
            nameof(AudioTrackViewModel.MeterPeakLabel) or
            nameof(AudioTrackViewModel.TrackWaveform) or
            nameof(AudioTrackViewModel.TrackDisplayWaveform) or
            nameof(AudioTrackViewModel.WaveformAutoScale))
        {
            return;
        }

        if (sender is AudioTrackViewModel liveTrack &&
            (e.PropertyName is nameof(AudioTrackViewModel.IsMuted) or
                nameof(AudioTrackViewModel.IsSolo) or
                nameof(AudioTrackViewModel.VolumeDb) or
                nameof(AudioTrackViewModel.Pan)))
        {
            _playbackService.UpdateTrackMixer(
                liveTrack.Id,
                liveTrack.IsMuted,
                liveTrack.IsSolo,
                liveTrack.VolumeDb,
                liveTrack.Pan);
            ResetLiveMasterAnalysis();
            InvalidateOfflineMasterAnalysis();
            RebuildTrackDisplayWaveform(liveTrack);
            RebuildMasterWaveform();
        }

        if (sender is AudioTrackViewModel effectsTrack &&
            e.PropertyName is nameof(AudioTrackViewModel.NoiseReductionEnabled) or
                nameof(AudioTrackViewModel.NoiseReductionStrength) or
                nameof(AudioTrackViewModel.NoiseReductionListenRemoved) or
                nameof(AudioTrackViewModel.NoiseProfileDb) or
                nameof(AudioTrackViewModel.NoiseProfileSampleRate) or
                nameof(AudioTrackViewModel.NoiseProfileFftSize) or
                nameof(AudioTrackViewModel.ExpanderEnabled) or
                nameof(AudioTrackViewModel.ExpanderThresholdDb) or
                nameof(AudioTrackViewModel.ExpanderReductionDb) or
                nameof(AudioTrackViewModel.NotchEnabled) or
                nameof(AudioTrackViewModel.NotchFrequencyHz) or
                nameof(AudioTrackViewModel.NotchWidthPercent) or
                nameof(AudioTrackViewModel.HumRemovalEnabled) or
                nameof(AudioTrackViewModel.HumFrequencyHz) or
                nameof(AudioTrackViewModel.HumHarmonics) or
                nameof(AudioTrackViewModel.HumReductionDb) or
                nameof(AudioTrackViewModel.DeEsserEnabled) or
                nameof(AudioTrackViewModel.DeEsserFrequencyHz) or
                nameof(AudioTrackViewModel.DeEsserStrength) or
                nameof(AudioTrackViewModel.ChannelModeIndex) or
                nameof(AudioTrackViewModel.PhaseInvertLeft) or
                nameof(AudioTrackViewModel.PhaseInvertRight) or
                nameof(AudioTrackViewModel.SaturationEnabled) or
                nameof(AudioTrackViewModel.SaturationDriveDb) or
                nameof(AudioTrackViewModel.SaturationMixPercent) or
                nameof(AudioTrackViewModel.ChorusEnabled) or
                nameof(AudioTrackViewModel.ChorusRateHz) or
                nameof(AudioTrackViewModel.ChorusDepthPercent) or
                nameof(AudioTrackViewModel.ChorusMixPercent) or
                nameof(AudioTrackViewModel.StereoWidthEnabled) or
                nameof(AudioTrackViewModel.StereoWidthPercent) or
                nameof(AudioTrackViewModel.DelayEnabled) or
                nameof(AudioTrackViewModel.DelayTimeMs) or
                nameof(AudioTrackViewModel.DelayFeedbackPercent) or
                nameof(AudioTrackViewModel.DelayMixPercent) or
                nameof(AudioTrackViewModel.DelayPingPong) or
                nameof(AudioTrackViewModel.ReverbEnabled) or
                nameof(AudioTrackViewModel.ReverbMixPercent) or
                nameof(AudioTrackViewModel.ReverbDecayPercent) or
                nameof(AudioTrackViewModel.ReverbDampingPercent) or
                nameof(AudioTrackViewModel.ReverbPreDelayMs) or
                nameof(AudioTrackViewModel.EqualizerEnabled) or
                nameof(AudioTrackViewModel.BassDb) or
                nameof(AudioTrackViewModel.MidDb) or
                nameof(AudioTrackViewModel.MidFrequencyHz) or
                nameof(AudioTrackViewModel.TrebleDb) or
                nameof(AudioTrackViewModel.HighPassEnabled) or
                nameof(AudioTrackViewModel.HighPassFrequencyHz) or
                nameof(AudioTrackViewModel.LowPassEnabled) or
                nameof(AudioTrackViewModel.LowPassFrequencyHz) or
                nameof(AudioTrackViewModel.CompressorEnabled) or
                nameof(AudioTrackViewModel.CompressorStrength) or
                nameof(AudioTrackViewModel.LimiterEnabled) or
                nameof(AudioTrackViewModel.LimiterCeilingDb))
        {
            _playbackService.UpdateTrackEffects(
                effectsTrack.Id,
                effectsTrack == SelectedTrack && IsSelectedTrackEffectsBypassed
                    ? CreateNeutralTrackEffectsDocument()
                    : effectsTrack.CreateEffectsDocument());
            if (effectsTrack == SelectedTrack)
            {
                OnPropertyChanged(nameof(SelectedTrackEffectsSummaryLabel));
            }
            ResetLiveMasterAnalysis();
            InvalidateOfflineMasterAnalysis();

            if (e.PropertyName is nameof(AudioTrackViewModel.EqualizerEnabled) or
                nameof(AudioTrackViewModel.HighPassEnabled) or
                nameof(AudioTrackViewModel.LowPassEnabled))
            {
                RebuildMasterWaveform();
            }
        }

        if (sender == SelectedTrack &&
            (e.PropertyName == nameof(AudioTrackViewModel.Name) ||
             e.PropertyName == nameof(AudioTrackViewModel.TrackNumber)))
        {
            OnPropertyChanged(nameof(SelectedTrackTitle));
        }

        if (e.PropertyName == nameof(AudioTrackViewModel.NoiseReductionListenRemoved))
        {
            return;
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.ClipCount) or
            nameof(AudioTrackViewModel.ClipCountLabel) or
            nameof(AudioTrackViewModel.ProjectDurationLabel))
        {
            OnPropertyChanged(nameof(SelectedTrackClipSummary));
            RefreshProjectState();
        }

        MarkDirty();
    }

    private void ToggleTrackEffectsBypass()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        _selectedTrackEffectsBypassed = !_selectedTrackEffectsBypassed;
        ApplySelectedTrackFxMonitorState();
        OnPropertyChanged(nameof(IsSelectedTrackEffectsBypassed));
        OnPropertyChanged(nameof(EffectsAbButtonLabel));
        OnPropertyChanged(nameof(EffectsAbStatusLabel));
        ResetLiveMasterAnalysis();
        StatusMessage = _selectedTrackEffectsBypassed
            ? $"A/B: {SelectedTrack.Name} trocken · alle Spur-Effekte nur fürs Monitoring umgangen."
            : $"A/B: {SelectedTrack.Name} mit Effektkette.";
    }

    private void CopyTrackEffects()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        _effectsClipboard = CloneTrackEffects(SelectedTrack.CreateEffectsDocument());
        _effectsClipboard.NoiseReductionListenRemoved = false;
        OnPropertyChanged(nameof(EffectsClipboardLabel));
        PasteTrackEffectsCommand.NotifyCanExecuteChanged();
        StatusMessage = $"FX von {SelectedTrack.Name} kopiert · können jetzt auf eine andere Spur eingefügt werden.";
    }

    private void PasteTrackEffects()
    {
        if (SelectedTrack is null || _effectsClipboard is null)
        {
            return;
        }

        SelectedTrack.ApplyEffectsDocument(CloneTrackEffects(_effectsClipboard));
        ApplySelectedTrackFxMonitorState();
        OnPropertyChanged(nameof(SelectedTrackEffectsSummaryLabel));
        ResetLiveMasterAnalysis();
        InvalidateOfflineMasterAnalysis();
        MarkDirty();
        StatusMessage = $"FX-Zwischenablage auf {SelectedTrack.Name} angewendet.";
    }

    private void ResetAllTrackEffects()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        // Keep a learned noise profile; reset means neutral processing, not deleting analysis data.
        SelectedTrack.ApplyEffectsDocument(new AudioTrackEffectsDocument(), includeNoiseProfile: false);
        ApplySelectedTrackFxMonitorState();
        OnPropertyChanged(nameof(SelectedTrackEffectsSummaryLabel));
        ResetLiveMasterAnalysis();
        InvalidateOfflineMasterAnalysis();
        MarkDirty();
        StatusMessage = $"Alle Spur-Effekte von {SelectedTrack.Name} auf neutrale Werte zurückgesetzt.";
    }

    private void ResetTrackEffect(string? effectKey)
    {
        if (SelectedTrack is null || string.IsNullOrWhiteSpace(effectKey))
        {
            return;
        }

        var track = SelectedTrack;
        switch (effectKey)
        {
            case "noise":
                track.NoiseReductionEnabled = false;
                track.NoiseReductionStrength = 30;
                track.NoiseReductionListenRemoved = false;
                break;
            case "expander":
                track.ExpanderEnabled = false;
                track.ExpanderThresholdDb = -45;
                track.ExpanderReductionDb = 18;
                break;
            case "notch":
                track.NotchEnabled = false;
                track.NotchFrequencyHz = 6_500;
                track.NotchWidthPercent = 30;
                break;
            case "hum":
                track.HumRemovalEnabled = false;
                track.HumFrequencyHz = 50;
                track.HumHarmonics = 4;
                track.HumReductionDb = 18;
                break;
            case "deesser":
                track.DeEsserEnabled = false;
                track.DeEsserFrequencyHz = 6_500;
                track.DeEsserStrength = 35;
                break;
            case "eq":
                track.EqualizerEnabled = false;
                track.BassDb = 0;
                track.MidDb = 0;
                track.TrebleDb = 0;
                track.MidFrequencyHz = 1_000;
                track.HighPassEnabled = false;
                track.HighPassFrequencyHz = 80;
                track.LowPassEnabled = false;
                track.LowPassFrequencyHz = 18_000;
                break;
            case "compressor":
                track.CompressorEnabled = false;
                track.CompressorStrength = 35;
                break;
            case "limiter":
                track.LimiterEnabled = false;
                track.LimiterCeilingDb = -1;
                break;
            case "channel":
                track.ChannelModeIndex = 0;
                track.PhaseInvertLeft = false;
                track.PhaseInvertRight = false;
                break;
            case "saturation":
                track.SaturationEnabled = false;
                track.SaturationDriveDb = 4;
                track.SaturationMixPercent = 25;
                break;
            case "chorus":
                track.ChorusEnabled = false;
                track.ChorusRateHz = 0.35;
                track.ChorusDepthPercent = 35;
                track.ChorusMixPercent = 18;
                break;
            case "width":
                track.StereoWidthEnabled = false;
                track.StereoWidthPercent = 100;
                break;
            case "delay":
                track.DelayEnabled = false;
                track.DelayTimeMs = 240;
                track.DelayFeedbackPercent = 24;
                track.DelayMixPercent = 12;
                track.DelayPingPong = false;
                break;
            case "reverb":
                track.ReverbEnabled = false;
                track.ReverbMixPercent = 12;
                track.ReverbDecayPercent = 35;
                track.ReverbDampingPercent = 45;
                track.ReverbPreDelayMs = 18;
                break;
            default:
                return;
        }

        ApplySelectedTrackFxMonitorState();
        OnPropertyChanged(nameof(SelectedTrackEffectsSummaryLabel));
        StatusMessage = $"{effectKey} auf Standardwerte zurückgesetzt.";
    }

    private void ApplySelectedTrackFxMonitorState()
    {
        if (SelectedTrack is null)
        {
            return;
        }

        _playbackService.UpdateTrackEffects(
            SelectedTrack.Id,
            IsSelectedTrackEffectsBypassed
                ? CreateNeutralTrackEffectsDocument()
                : SelectedTrack.CreateEffectsDocument());
    }

    private static AudioTrackEffectsDocument CreateNeutralTrackEffectsDocument() => new();

    private static AudioTrackEffectsDocument CloneTrackEffects(AudioTrackEffectsDocument source) => new()
    {
        NoiseReductionEnabled = source.NoiseReductionEnabled,
        NoiseReductionStrength = source.NoiseReductionStrength,
        NoiseProfileSampleRate = source.NoiseProfileSampleRate,
        NoiseProfileFftSize = source.NoiseProfileFftSize,
        NoiseProfileDb = source.NoiseProfileDb?.ToArray() ?? [],
        NoiseReductionListenRemoved = false,
        ExpanderEnabled = source.ExpanderEnabled,
        ExpanderThresholdDb = source.ExpanderThresholdDb,
        ExpanderReductionDb = source.ExpanderReductionDb,
        NotchEnabled = source.NotchEnabled,
        NotchFrequencyHz = source.NotchFrequencyHz,
        NotchWidthPercent = source.NotchWidthPercent,
        HumRemovalEnabled = source.HumRemovalEnabled,
        HumFrequencyHz = source.HumFrequencyHz,
        HumHarmonics = source.HumHarmonics,
        HumReductionDb = source.HumReductionDb,
        DeEsserEnabled = source.DeEsserEnabled,
        DeEsserFrequencyHz = source.DeEsserFrequencyHz,
        DeEsserStrength = source.DeEsserStrength,
        ChannelMode = source.ChannelMode,
        PhaseInvertLeft = source.PhaseInvertLeft,
        PhaseInvertRight = source.PhaseInvertRight,
        SaturationEnabled = source.SaturationEnabled,
        SaturationDriveDb = source.SaturationDriveDb,
        SaturationMixPercent = source.SaturationMixPercent,
        ChorusEnabled = source.ChorusEnabled,
        ChorusRateHz = source.ChorusRateHz,
        ChorusDepthPercent = source.ChorusDepthPercent,
        ChorusMixPercent = source.ChorusMixPercent,
        StereoWidthEnabled = source.StereoWidthEnabled,
        StereoWidthPercent = source.StereoWidthPercent,
        DelayEnabled = source.DelayEnabled,
        DelayTimeMs = source.DelayTimeMs,
        DelayFeedbackPercent = source.DelayFeedbackPercent,
        DelayMixPercent = source.DelayMixPercent,
        DelayPingPong = source.DelayPingPong,
        ReverbEnabled = source.ReverbEnabled,
        ReverbMixPercent = source.ReverbMixPercent,
        ReverbDecayPercent = source.ReverbDecayPercent,
        ReverbDampingPercent = source.ReverbDampingPercent,
        ReverbPreDelayMs = source.ReverbPreDelayMs,
        EqualizerEnabled = source.EqualizerEnabled,
        BassDb = source.BassDb,
        MidDb = source.MidDb,
        TrebleDb = source.TrebleDb,
        MidFrequencyHz = source.MidFrequencyHz,
        HighPassEnabled = source.HighPassEnabled,
        HighPassFrequencyHz = source.HighPassFrequencyHz,
        LowPassEnabled = source.LowPassEnabled,
        LowPassFrequencyHz = source.LowPassFrequencyHz,
        CompressorEnabled = source.CompressorEnabled,
        CompressorStrength = source.CompressorStrength,
        LimiterEnabled = source.LimiterEnabled,
        LimiterCeilingDb = source.LimiterCeilingDb
    };

    private void RefreshFxRackCommandState()
    {
        ToggleTrackEffectsBypassCommand.NotifyCanExecuteChanged();
        CopyTrackEffectsCommand.NotifyCanExecuteChanged();
        PasteTrackEffectsCommand.NotifyCanExecuteChanged();
        ResetAllTrackEffectsCommand.NotifyCanExecuteChanged();
        ResetTrackEffectCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadProjectWaveformsAsync()
    {
        foreach (var clip in Tracks.SelectMany(track => track.Clips))
        {
            if (clip.IsMissing)
            {
                clip.SetWaveformUnavailable("Wellenform nicht verfügbar · Quelldatei fehlt");
                continue;
            }

            await LoadWaveformAsync(clip);
        }
    }

    private async Task LoadWaveformAsync(AudioClipViewModel clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (clip.IsMissing)
        {
            clip.SetWaveformUnavailable("Wellenform nicht verfügbar · Quelldatei fehlt");
            return;
        }

        clip.BeginWaveformLoad();

        try
        {
            var waveform = await _waveformCache.GetAsync(clip.SourcePath);
            clip.SetWaveform(waveform);
            RebuildTrackWaveforms();
            RebuildMasterWaveform();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            NotSupportedException)
        {
            clip.SetWaveformUnavailable($"Wellenform nicht verfügbar · {exception.Message}");
            RebuildTrackWaveforms();
            RebuildMasterWaveform();
        }
    }

    private void RebuildTrackWaveforms()
    {
        var durationSeconds = ProjectDurationSeconds;
        foreach (var track in Tracks)
        {
            RebuildTrackWaveform(track, durationSeconds);
        }
    }

    private void RebuildTrackWaveform(AudioTrackViewModel track, double durationSeconds)
    {
        if (durationSeconds <= 0 || track.ClipCount == 0)
        {
            track.SetTrackWaveform(null);
            track.SetTrackDisplayWaveform(null);
            return;
        }

        // Keep enough horizontal detail for the detachable waveform inspector.
        // The source cache itself is capped, so going far beyond 8192 would not
        // add real information while increasing memory usage.
        var peakCount = Math.Clamp(
            (int)Math.Ceiling(durationSeconds * 40d),
            512,
            8192);
        var leftMinimum = new float[peakCount];
        var leftMaximum = new float[peakCount];
        var rightMinimum = new float[peakCount];
        var rightMaximum = new float[peakCount];
        var hasWaveform = false;

        foreach (var clip in track.Clips)
        {
            var envelope = clip.Waveform;
            if (envelope is null || envelope.PeakCount == 0 || clip.DurationSeconds <= 0)
            {
                continue;
            }

            hasWaveform = true;
            var clipStart = Math.Max(0, clip.TimelineStartSeconds);
            var clipEnd = Math.Min(durationSeconds, clip.TimelineEndSeconds);
            if (clipEnd <= clipStart)
            {
                continue;
            }

            var firstTrackPeak = Math.Clamp(
                (int)Math.Floor(clipStart / durationSeconds * peakCount),
                0,
                peakCount - 1);
            var lastTrackPeakExclusive = Math.Clamp(
                (int)Math.Ceiling(clipEnd / durationSeconds * peakCount),
                firstTrackPeak + 1,
                peakCount);
            var clipGain = DbToLinear(clip.GainDb);

            for (var trackPeakIndex = firstTrackPeak;
                 trackPeakIndex < lastTrackPeakExclusive;
                 trackPeakIndex++)
            {
                var binStart = trackPeakIndex / (double)peakCount * durationSeconds;
                var binEnd = (trackPeakIndex + 1) / (double)peakCount * durationSeconds;
                var overlapStart = Math.Max(binStart, clipStart);
                var overlapEnd = Math.Min(binEnd, clipEnd);
                if (overlapEnd <= overlapStart)
                {
                    continue;
                }

                var localStart = overlapStart - clipStart;
                var localEnd = overlapEnd - clipStart;
                var sourceStart = clip.SourceStartSeconds + localStart;
                var sourceEnd = clip.SourceStartSeconds + localEnd;
                var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);

                var sourcePeakStart = Math.Clamp(
                    (int)Math.Floor(sourceStart / sourceDuration * envelope.PeakCount),
                    0,
                    envelope.PeakCount - 1);
                var sourcePeakEnd = Math.Clamp(
                    (int)Math.Ceiling(sourceEnd / sourceDuration * envelope.PeakCount),
                    sourcePeakStart + 1,
                    envelope.PeakCount);

                var leftMin = 0f;
                var leftMax = 0f;
                var rightMin = 0f;
                var rightMax = 0f;

                for (var sourcePeakIndex = sourcePeakStart;
                     sourcePeakIndex < sourcePeakEnd;
                     sourcePeakIndex++)
                {
                    var leftPeak = envelope.Left[sourcePeakIndex];
                    leftMin = Math.Min(leftMin, leftPeak.Minimum);
                    leftMax = Math.Max(leftMax, leftPeak.Maximum);

                    var rightPeak = envelope.Right is null
                        ? leftPeak
                        : envelope.Right[sourcePeakIndex];
                    rightMin = Math.Min(rightMin, rightPeak.Minimum);
                    rightMax = Math.Max(rightMax, rightPeak.Maximum);
                }

                var centerSeconds = (localStart + localEnd) * 0.5;
                if (clip.IsMutedAtLocalSeconds(centerSeconds))
                {
                    continue;
                }

                var gain = clipGain * CalculateClipFade(clip, centerSeconds);
                leftMinimum[trackPeakIndex] += leftMin * gain;
                leftMaximum[trackPeakIndex] += leftMax * gain;
                rightMinimum[trackPeakIndex] += rightMin * gain;
                rightMaximum[trackPeakIndex] += rightMax * gain;
            }
        }

        if (!hasWaveform)
        {
            track.SetTrackWaveform(null);
            track.SetTrackDisplayWaveform(null);
            return;
        }

        var left = new WaveformPeak[peakCount];
        var right = new WaveformPeak[peakCount];
        for (var index = 0; index < peakCount; index++)
        {
            left[index] = new WaveformPeak(leftMinimum[index], leftMaximum[index]);
            right[index] = new WaveformPeak(rightMinimum[index], rightMaximum[index]);
        }

        var sampleFrames = checked((long)Math.Ceiling(durationSeconds * ProjectSampleRate));
        track.SetTrackWaveform(new WaveformEnvelope(
            Math.Max(1, ProjectSampleRate),
            2,
            sampleFrames,
            left,
            right));
        RebuildTrackDisplayWaveform(track);
    }

    private static void RebuildTrackDisplayWaveform(AudioTrackViewModel track)
    {
        var source = track.TrackWaveform;
        if (source is null || source.PeakCount == 0)
        {
            track.SetTrackDisplayWaveform(null);
            return;
        }

        var volume = DbToLinear(track.VolumeDb);
        var normalizedPan = Math.Clamp(track.Pan / 100d, -1d, 1d);
        var leftPan = normalizedPan > 0 ? 1d - normalizedPan : 1d;
        var rightPan = normalizedPan < 0 ? 1d + normalizedPan : 1d;
        var leftGain = (float)(volume * leftPan);
        var rightGain = (float)(volume * rightPan);
        var left = new WaveformPeak[source.PeakCount];
        var right = new WaveformPeak[source.PeakCount];

        for (var index = 0; index < source.PeakCount; index++)
        {
            var leftPeak = source.Left[index];
            var rightPeak = source.Right is null ? leftPeak : source.Right[index];
            left[index] = new WaveformPeak(leftPeak.Minimum * leftGain, leftPeak.Maximum * leftGain);
            right[index] = new WaveformPeak(rightPeak.Minimum * rightGain, rightPeak.Maximum * rightGain);
        }

        track.SetTrackDisplayWaveform(new WaveformEnvelope(
            source.SampleRate,
            2,
            source.SampleFrames,
            left,
            right));
    }

    private void RebuildMasterWaveform()
    {
        var durationSeconds = ProjectDurationSeconds;
        if (durationSeconds <= 0 || ClipCount == 0)
        {
            MasterWaveform = null;
            MasterWaveformStateLabel = "Master-Summe noch leer";
            OnPropertyChanged(nameof(MasterTrackSummary));
            OnPropertyChanged(nameof(InspectorContextSubtitle));
            return;
        }

        var peakCount = Math.Clamp(
            (int)Math.Ceiling(durationSeconds * 12d),
            256,
            2048);
        var leftMinimum = new float[peakCount];
        var leftMaximum = new float[peakCount];
        var rightMinimum = new float[peakCount];
        var rightMaximum = new float[peakCount];

        var hasSolo = Tracks.Any(track => track.IsSolo);
        var includedClips = 0;
        var availableWaveforms = 0;

        foreach (var track in Tracks)
        {
            if (track.IsMuted || (hasSolo && !track.IsSolo))
            {
                continue;
            }

            var trackVolume = DbToLinear(track.VolumeDb);
            var normalizedPan = Math.Clamp(track.Pan / 100d, -1d, 1d);
            var leftPan = normalizedPan > 0 ? 1d - normalizedPan : 1d;
            var rightPan = normalizedPan < 0 ? 1d + normalizedPan : 1d;
            var trackLeftGain = (float)(trackVolume * leftPan);
            var trackRightGain = (float)(trackVolume * rightPan);

            foreach (var clip in track.Clips)
            {
                if (clip.DurationSeconds <= 0)
                {
                    continue;
                }

                includedClips++;
                var envelope = clip.Waveform;
                if (envelope is null || envelope.PeakCount == 0)
                {
                    continue;
                }

                availableWaveforms++;
                var clipStart = Math.Max(0, clip.TimelineStartSeconds);
                var clipEnd = Math.Min(durationSeconds, clip.TimelineEndSeconds);
                if (clipEnd <= clipStart)
                {
                    continue;
                }

                var firstMasterPeak = Math.Clamp(
                    (int)Math.Floor(clipStart / durationSeconds * peakCount),
                    0,
                    peakCount - 1);
                var lastMasterPeakExclusive = Math.Clamp(
                    (int)Math.Ceiling(clipEnd / durationSeconds * peakCount),
                    firstMasterPeak + 1,
                    peakCount);

                var clipGain = DbToLinear(clip.GainDb);

                for (var masterPeakIndex = firstMasterPeak;
                     masterPeakIndex < lastMasterPeakExclusive;
                     masterPeakIndex++)
                {
                    var binStart = masterPeakIndex / (double)peakCount * durationSeconds;
                    var binEnd = (masterPeakIndex + 1) / (double)peakCount * durationSeconds;
                    var overlapStart = Math.Max(binStart, clipStart);
                    var overlapEnd = Math.Min(binEnd, clipEnd);
                    if (overlapEnd <= overlapStart)
                    {
                        continue;
                    }

                    var localStart = overlapStart - clipStart;
                    var localEnd = overlapEnd - clipStart;
                    var sourceStart = clip.SourceStartSeconds + localStart;
                    var sourceEnd = clip.SourceStartSeconds + localEnd;
                    var sourceDuration = Math.Max(envelope.DurationSeconds, double.Epsilon);

                    var sourcePeakStart = Math.Clamp(
                        (int)Math.Floor(sourceStart / sourceDuration * envelope.PeakCount),
                        0,
                        envelope.PeakCount - 1);
                    var sourcePeakEnd = Math.Clamp(
                        (int)Math.Ceiling(sourceEnd / sourceDuration * envelope.PeakCount),
                        sourcePeakStart + 1,
                        envelope.PeakCount);

                    var leftMin = 0f;
                    var leftMax = 0f;
                    var rightMin = 0f;
                    var rightMax = 0f;

                    for (var sourcePeakIndex = sourcePeakStart;
                         sourcePeakIndex < sourcePeakEnd;
                         sourcePeakIndex++)
                    {
                        var leftPeak = envelope.Left[sourcePeakIndex];
                        leftMin = Math.Min(leftMin, leftPeak.Minimum);
                        leftMax = Math.Max(leftMax, leftPeak.Maximum);

                        var rightPeak = envelope.Right is null
                            ? leftPeak
                            : envelope.Right[sourcePeakIndex];
                        rightMin = Math.Min(rightMin, rightPeak.Minimum);
                        rightMax = Math.Max(rightMax, rightPeak.Maximum);
                    }

                    var centerSeconds = (localStart + localEnd) * 0.5;
                    if (clip.IsMutedAtLocalSeconds(centerSeconds))
                    {
                        continue;
                    }

                    var fadeGain = CalculateClipFade(clip, centerSeconds);
                    var commonGain = clipGain * fadeGain;

                    leftMinimum[masterPeakIndex] += leftMin * commonGain * trackLeftGain;
                    leftMaximum[masterPeakIndex] += leftMax * commonGain * trackLeftGain;
                    rightMinimum[masterPeakIndex] += rightMin * commonGain * trackRightGain;
                    rightMaximum[masterPeakIndex] += rightMax * commonGain * trackRightGain;
                }
            }
        }

        var masterGain = DbToLinear(MasterVolumeDb);
        var left = new WaveformPeak[peakCount];
        var right = new WaveformPeak[peakCount];

        for (var index = 0; index < peakCount; index++)
        {
            left[index] = new WaveformPeak(
                leftMinimum[index] * masterGain,
                leftMaximum[index] * masterGain);
            right[index] = new WaveformPeak(
                rightMinimum[index] * masterGain,
                rightMaximum[index] * masterGain);
        }

        var sampleFrames = checked((long)Math.Ceiling(durationSeconds * ProjectSampleRate));
        MasterWaveform = new WaveformEnvelope(
            Math.Max(1, ProjectSampleRate),
            2,
            sampleFrames,
            left,
            right);

        var hasActiveFrequencyProcessing = Tracks.Any(track =>
            track.EqualizerEnabled || track.HighPassEnabled || track.LowPassEnabled);
        var responseHint = hasActiveFrequencyProcessing ? " · Hüllkurve vor EQ" : string.Empty;

        MasterWaveformStateLabel = includedClips == 0
            ? "Master-Summe · keine aktive Spur"
            : availableWaveforms == 0
                ? "Master-Summe wartet auf Wellenformen"
                : availableWaveforms < includedClips
                    ? $"Master-Summe · {availableWaveforms}/{includedClips} Clips analysiert{responseHint}"
                    : $"Master-Summe · {includedClips} Clips kombiniert{responseHint}";

        OnPropertyChanged(nameof(MasterTrackSummary));
        OnPropertyChanged(nameof(InspectorContextSubtitle));
    }

    private static float CalculateClipFade(AudioClipViewModel clip, double localSeconds)
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

    private static float DbToLinear(double db) =>
        db <= -59.9 ? 0f : (float)Math.Pow(10, db / 20d);

    private void RefreshMasterLoudness(AudioPlaybackState state)
    {
        var active = state is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering;
        var justFinished = state == AudioPlaybackState.Stopped &&
                           (_lastPlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Buffering);
        if (!active && !justFinished)
        {
            return;
        }

        var snapshot = _playbackService.GetMasterLoudnessSnapshot();
        if (!snapshot.HasData || snapshot.Equals(_liveMasterLoudness))
        {
            return;
        }

        _liveMasterLoudness = snapshot;
        NotifyMasterAnalysisDisplayChanged();
    }

    private void RefreshMasterLimiterReduction(AudioPlaybackState state)
    {
        var active = state is AudioPlaybackState.Playing or AudioPlaybackState.Paused or AudioPlaybackState.Buffering;
        var justFinished = state == AudioPlaybackState.Stopped &&
                           (_lastPlaybackState is AudioPlaybackState.Playing or AudioPlaybackState.Buffering);
        if (!active && !justFinished)
        {
            if (_liveMasterLimiterReduction.HasData)
            {
                _liveMasterLimiterReduction = MasterLimiterReductionSnapshot.Empty;
                OnPropertyChanged(nameof(LiveLimiterReductionReadout));
            }
            return;
        }

        var snapshot = _playbackService.GetMasterLimiterReductionSnapshot();
        if (snapshot.Equals(_liveMasterLimiterReduction))
        {
            return;
        }

        _liveMasterLimiterReduction = snapshot;
        OnPropertyChanged(nameof(LiveLimiterReductionReadout));
    }

    private void ResetLiveMasterAnalysis()
    {
        _playbackService.ResetMasterLoudnessAnalysis();
        var hadLoudness = _liveMasterLoudness.HasData;
        var hadLimiterReduction = _liveMasterLimiterReduction.HasData;
        _liveMasterLoudness = MasterLoudnessSnapshot.Empty;
        _liveMasterLimiterReduction = MasterLimiterReductionSnapshot.Empty;

        if (hadLimiterReduction)
        {
            OnPropertyChanged(nameof(LiveLimiterReductionReadout));
        }

        if (hadLoudness)
        {
            NotifyMasterAnalysisDisplayChanged();
        }
    }

    private void InvalidateOfflineMasterAnalysis()
    {
        _masterAnalysisRevision++;
        DisableMasteringPreview();
        if (_offlineMasterAnalysis is null)
        {
            return;
        }

        _offlineMasterAnalysis = null;
        MasterAnalysisProgress = 0;
        NotifyMasterAnalysisDisplayChanged();
    }

    private MasteringAssistantPlan? BuildMasteringAssistantPlan()
    {
        if (_offlineMasterAnalysis is null)
        {
            return null;
        }

        var requestedGain = TargetLufs - _offlineMasterAnalysis.PreLimiterIntegratedLufs;
        var proposedMasterVolume = Math.Clamp(MasterVolumeDb + requestedGain, -60, 12);
        var actualGain = proposedMasterVolume - MasterVolumeDb;
        var projectedTruePeak = _offlineMasterAnalysis.PreLimiterTruePeakMaxDbtp + actualGain;
        var requiredPeakReduction = Math.Max(0, projectedTruePeak - MasterLimiterCeilingDb);
        var gainWasClamped = Math.Abs(actualGain - requestedGain) > 0.05;
        var canAutoApply = !gainWasClamped && requestedGain <= 4.0 && requiredPeakReduction <= 4.5;
        var enableLimiter = requiredPeakReduction > 0.05 || MasterLimiterEnabled;

        string level;
        string headline;
        string summary;
        if (gainWasClamped)
        {
            level = "Grenze erreicht";
            headline = "Ziel liegt außerhalb des sicheren Master-Gain-Bereichs";
            summary = "Elbwald würde den zulässigen Master-Gain überschreiten. Das Ziel sollte über Mix- oder Dynamikbearbeitung vorbereitet werden.";
        }
        else if (requestedGain >= 6 || requiredPeakReduction >= 6)
        {
            level = "Sehr aggressiv";
            headline = "Nicht automatisch erzwingen";
            summary = $"Für {TargetLufs:0.0} LUFS wären {requestedGain:+0.0;-0.0;0.0} dB Gain und etwa {requiredPeakReduction:0.0} dB Peak-Kontrolle nötig. Das ist zu viel für einen blinden Master-Limiter.";
        }
        else if (requestedGain > 4.0 || requiredPeakReduction > 4.5)
        {
            level = "Deutlich";
            headline = "Mix zuerst etwas vorbereiten";
            summary = $"Das Ziel ist erreichbar, aber {requiredPeakReduction:0.0} dB Peak-Kontrolle bzw. {requestedGain:+0.0;-0.0;0.0} dB Gain wären hörbar. Die Automatik stoppt hier bewusst.";
        }
        else if (requestedGain >= 2 || requiredPeakReduction >= 2.5)
        {
            level = "Moderat";
            headline = "Automatisch machbar – A/B gegenhören";
            summary = $"Elbwald kann das Ziel mit {actualGain:+0.0;-0.0;0.0} dB Master-Gain und bis zu etwa {requiredPeakReduction:0.0} dB Peak-Kontrolle vorbereiten.";
        }
        else if (Math.Abs(requestedGain) <= 0.5 && requiredPeakReduction <= 0.75)
        {
            level = "Sehr nah am Ziel";
            headline = "Nur kleine Korrektur nötig";
            summary = "Lautheit und Peaks liegen bereits nahe am gewählten Ziel. Der Assistent greift nur sehr wenig ein.";
        }
        else
        {
            level = "Leicht";
            headline = "Gut automatisch erreichbar";
            summary = $"Der Mix braucht nur {actualGain:+0.0;-0.0;0.0} dB Gesamtanpassung und etwa {requiredPeakReduction:0.0} dB Peak-Kontrolle.";
        }

        var step1 = Math.Abs(actualGain) < 0.05
            ? "1 · Master-Gain unverändert lassen"
            : $"1 · Master-Gain um {actualGain:+0.0;-0.0;0.0} dB anpassen";
        var step2 = enableLimiter
            ? $"2 · True-Peak-Limiter auf {MasterLimiterCeilingDb:0.0} dBTP absichern"
            : "2 · Kein zusätzlicher Limiter-Eingriff erforderlich";
        var step3 = requiredPeakReduction > 0.5
            ? $"3 · Spitzen bis ca. {requiredPeakReduction:0.0} dB kontrollieren und danach neu analysieren"
            : "3 · Ergebnis neu analysieren und per A/B gegenhören";
        var autoApplyHint = canAutoApply
            ? "Automatik verfügbar: Elbwald setzt nur Master-Gain und True-Peak-Limiter, analysiert danach erneut und erzwingt keine weitere Lautheit."
            : "Automatik bewusst gesperrt: Erst Peaks/Dynamik im Mix vorbereiten. Die A/B-Vorschau bleibt zum Beurteilen verfügbar.";

        return new MasteringAssistantPlan(
            level,
            headline,
            summary,
            step1,
            step2,
            step3,
            actualGain,
            proposedMasterVolume,
            projectedTruePeak,
            requiredPeakReduction,
            enableLimiter,
            canAutoApply,
            autoApplyHint);
    }

    private void ApplyMasteringPreviewToPlayback(MasteringAssistantPlan plan)
    {
        _playbackService.UpdateMasterMixer(
            plan.ProposedMasterVolumeDb,
            plan.EnableLimiter,
            MasterLimiterCeilingDb);
    }

    private void DisableMasteringPreview(bool updatePlayback = true)
    {
        if (!IsMasteringPreviewActive)
        {
            return;
        }

        IsMasteringPreviewActive = false;
        if (updatePlayback)
        {
            _playbackService.UpdateMasterMixer(
                MasterVolumeDb,
                MasterLimiterEnabled,
                MasterLimiterCeilingDb);
            ResetLiveMasterAnalysis();
        }

        RefreshMasteringAssistantCommandState();
    }

    private void RefreshMasteringPreviewIfActive()
    {
        if (!IsMasteringPreviewActive)
        {
            return;
        }

        var plan = BuildMasteringAssistantPlan();
        if (plan is null)
        {
            DisableMasteringPreview();
            return;
        }

        ApplyMasteringPreviewToPlayback(plan);
        ResetLiveMasterAnalysis();
    }

    private AudioProjectDocument CreatePlaybackDocument()
    {
        var document = CreateDocument();
        if (!IsMasteringPreviewActive)
        {
            return document;
        }

        var plan = BuildMasteringAssistantPlan();
        if (plan is null)
        {
            return document;
        }

        document.Master.VolumeDb = plan.ProposedMasterVolumeDb;
        document.Master.LimiterEnabled = plan.EnableLimiter;
        document.Master.LimiterCeilingDb = MasterLimiterCeilingDb;
        return document;
    }

    private void RefreshMasteringAssistantCommandState()
    {
        ToggleMasteringPreviewCommand.NotifyCanExecuteChanged();
        ApplyMasteringAssistantCommand.NotifyCanExecuteChanged();
        UndoMasteringAssistantCommand.NotifyCanExecuteChanged();
    }

    private void SyncMasteringPresetFromTargets()
    {
        var matching = MasteringPresets.FirstOrDefault(preset =>
            !preset.IsCustom &&
            Math.Abs(preset.TargetLufs - TargetLufs) < 0.05 &&
            Math.Abs(preset.TruePeakCeilingDbtp - MasterLimiterCeilingDb) < 0.05)
            ?? MasteringPresets.First(preset => preset.IsCustom);

        if (ReferenceEquals(_selectedMasteringPreset, matching))
        {
            return;
        }

        _updatingMasteringPreset = true;
        try
        {
            SelectedMasteringPreset = matching;
        }
        finally
        {
            _updatingMasteringPreset = false;
        }
    }

    private void NotifyMasterRecommendationChanged()
    {
        OnPropertyChanged(nameof(RecommendedMasterGainDb));
        OnPropertyChanged(nameof(RecommendedMasterGainLabel));
        OnPropertyChanged(nameof(PreLimiterLoudnessReadout));
        OnPropertyChanged(nameof(ProjectedTruePeakLabel));
        OnPropertyChanged(nameof(RequiredPeakReductionDb));
        OnPropertyChanged(nameof(RequiredPeakReductionLabel));
        OnPropertyChanged(nameof(ProjectTruePeakStatusLabel));
        OnPropertyChanged(nameof(MasterLimiterStateLabel));
        OnPropertyChanged(nameof(MasterTargetAssessmentLabel));
        OnPropertyChanged(nameof(HasMasteringAssistantPlan));
        OnPropertyChanged(nameof(MasteringAssistantLevelLabel));
        OnPropertyChanged(nameof(MasteringAssistantHeadline));
        OnPropertyChanged(nameof(MasteringAssistantSummary));
        OnPropertyChanged(nameof(MasteringAssistantStep1));
        OnPropertyChanged(nameof(MasteringAssistantStep2));
        OnPropertyChanged(nameof(MasteringAssistantStep3));
        OnPropertyChanged(nameof(MasteringAssistantAutoApplyHint));
        OnPropertyChanged(nameof(CanAutoApplyMasteringAssistant));
        OnPropertyChanged(nameof(PeakContributors));
        OnPropertyChanged(nameof(HasPeakContributors));
        OnPropertyChanged(nameof(PeakCauseHeadline));
        OnPropertyChanged(nameof(PeakCauseSummary));
        OnPropertyChanged(nameof(PeakCauseActionLabel));
        OnPropertyChanged(nameof(PeakEvents));
        OnPropertyChanged(nameof(HasPeakEvents));
        OnPropertyChanged(nameof(PeakEventHeadline));
        OnPropertyChanged(nameof(PeakEventSummary));
        ApplyLoudnessTargetCommand.NotifyCanExecuteChanged();
        RefreshMasteringAssistantCommandState();
    }

    private void NotifyMasterAnalysisDisplayChanged()
    {
        OnPropertyChanged(nameof(HasMasterAnalysis));
        OnPropertyChanged(nameof(HasLiveMasterAnalysis));
        OnPropertyChanged(nameof(MasterAnalysisSourceLabel));
        OnPropertyChanged(nameof(MasterAnalysisProgressLabel));
        OnPropertyChanged(nameof(ProjectIntegratedReadout));
        OnPropertyChanged(nameof(ProjectShortTermReadout));
        OnPropertyChanged(nameof(ProjectMomentaryReadout));
        OnPropertyChanged(nameof(ProjectTruePeakReadout));
        OnPropertyChanged(nameof(ProjectTruePeakLeftReadout));
        OnPropertyChanged(nameof(ProjectTruePeakRightReadout));
        OnPropertyChanged(nameof(ProjectTruePeakStatusLabel));
        OnPropertyChanged(nameof(ProjectLimiterMaxReductionReadout));
        OnPropertyChanged(nameof(ProjectLimiterAverageReductionReadout));
        OnPropertyChanged(nameof(ProjectLimiterAbove3Readout));
        OnPropertyChanged(nameof(ProjectLimiterAbove5Readout));
        OnPropertyChanged(nameof(ProjectLimiterTimeAboveThresholdsReadout));
        OnPropertyChanged(nameof(ProjectLimiterReductionAssessmentLabel));
        OnPropertyChanged(nameof(PeakWarningEvents));
        OnPropertyChanged(nameof(ProjectMasterPreLimiterLevelHistory));
        OnPropertyChanged(nameof(ProjectMasterLevelHistory));
        OnPropertyChanged(nameof(PeakWarningOnlyCount));
        OnPropertyChanged(nameof(CriticalTransientCount));
        OnPropertyChanged(nameof(SustainedOverloadCount));
        OnPropertyChanged(nameof(HasMasterPeakWarnings));
        OnPropertyChanged(nameof(MasterWaveformWarningMarkers));
        OnPropertyChanged(nameof(MasterPeakWarningSummaryLabel));
        OnPropertyChanged(nameof(MasterPeakWarningDetailLabel));
        OnPropertyChanged(nameof(LiveIntegratedReadout));
        OnPropertyChanged(nameof(LiveShortTermReadout));
        OnPropertyChanged(nameof(LiveMomentaryReadout));
        OnPropertyChanged(nameof(LiveTruePeakReadout));
        OnPropertyChanged(nameof(LiveTruePeakLeftReadout));
        OnPropertyChanged(nameof(LiveTruePeakRightReadout));
        OnPropertyChanged(nameof(LiveAnalysisStatusLabel));
        NotifyMasterRecommendationChanged();
    }

    private static string FormatLufs(double? value) =>
        value is null ? "– LUFS" : $"{value.Value:0.0} LUFS";

    private static string FormatDbtp(double value) =>
        value <= -119.9 ? "–" : $"{value:0.0}";

    private void RefreshMeters(AudioPlaybackState state)
    {
        var active = state is AudioPlaybackState.Playing or AudioPlaybackState.Buffering;
        var snapshot = active
            ? _playbackService.GetMeterSnapshot()
            : AudioMeterSnapshot.Empty;

        foreach (var track in Tracks)
        {
            if (active && snapshot.Tracks.TryGetValue(track.Id, out var meter))
            {
                track.SetMeterLevels(meter.LeftDb, meter.RightDb);
            }
            else
            {
                track.SetMeterLevels(-60, -60);
            }
        }

        if (active)
        {
            SetMasterMeterLevels(snapshot.Master.LeftDb, snapshot.Master.RightDb);
        }
        else
        {
            SetMasterMeterLevels(-60, -60);
        }
    }

    private void SetMasterMeterLevels(double leftDb, double rightDb, bool immediate = false)
    {
        var nextLeft = ResolveMeterValue(_masterMeterLeftDb, leftDb, immediate);
        var nextRight = ResolveMeterValue(_masterMeterRightDb, rightDb, immediate);
        var changed = false;

        if (Math.Abs(_masterMeterLeftDb - nextLeft) > 0.01)
        {
            _masterMeterLeftDb = nextLeft;
            OnPropertyChanged(nameof(MasterMeterLeftDb));
            changed = true;
        }

        if (Math.Abs(_masterMeterRightDb - nextRight) > 0.01)
        {
            _masterMeterRightDb = nextRight;
            OnPropertyChanged(nameof(MasterMeterRightDb));
            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(MasterPeakReadout));
        }
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

    private static string FormatTimelineTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss\.f")
            : time.ToString(@"mm\:ss\.f");
    }

    private void RefreshProjectState()
    {
        InvalidateOfflineMasterAnalysis();
        OnPropertyChanged(nameof(TrackCount));
        OnPropertyChanged(nameof(ClipCount));
        OnPropertyChanged(nameof(MissingSourceCount));
        OnPropertyChanged(nameof(HasMissingSources));
        OnPropertyChanged(nameof(CanAddTrack));
        OnPropertyChanged(nameof(TrackCountLabel));
        OnPropertyChanged(nameof(MissingSourceLabel));
        OnPropertyChanged(nameof(SelectedTrackClipSummary));
        OnPropertyChanged(nameof(ProjectDurationSeconds));
        OnPropertyChanged(nameof(TimelineMaximumSeconds));
        OnPropertyChanged(nameof(TimelineCursorLabel));
        OnPropertyChanged(nameof(SelectedClipWaveformWidth));
        OnPropertyChanged(nameof(SelectedClipCursorSourceSeconds));
        OnPropertyChanged(nameof(HasPlayableAudio));
        OnPropertyChanged(nameof(MasterTrackSummary));
        OnPropertyChanged(nameof(InspectorContextSubtitle));
        RebuildTrackWaveforms();
        RebuildMasterWaveform();

        if (_timelineCursorSeconds > TimelineMaximumSeconds)
        {
            TimelineCursorSeconds = TimelineMaximumSeconds;
        }

        RefreshCommandState();
        RefreshPlaybackState();
    }

    private void RefreshCommandState()
    {
        AddMonoTrackCommand.NotifyCanExecuteChanged();
        AddStereoTrackCommand.NotifyCanExecuteChanged();
        NewProjectCommand.NotifyCanExecuteChanged();
        ImportWavCommand.NotifyCanExecuteChanged();
        OpenProjectCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SaveProjectAsCommand.NotifyCanExecuteChanged();
        ExportWavCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        SeekToCursorCommand.NotifyCanExecuteChanged();
        AnalyzeMasterCommand.NotifyCanExecuteChanged();
        ApplyLoudnessTargetCommand.NotifyCanExecuteChanged();
        ToggleMasteringPreviewCommand.NotifyCanExecuteChanged();
        ApplyMasteringAssistantCommand.NotifyCanExecuteChanged();
        UndoMasteringAssistantCommand.NotifyCanExecuteChanged();
        JumpToPeakEventCommand.NotifyCanExecuteChanged();
        ListenPeakEventCommand.NotifyCanExecuteChanged();
        OpenEqAnalysisCommand.NotifyCanExecuteChanged();
        RefreshClipEditCommandState();
        RefreshFxRackCommandState();
    }

    private void MarkDirty()
    {
        if (_suppressDirtyTracking)
        {
            return;
        }

        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(ProjectTitle));
    }

    private sealed record ClipEditUndoEntry(
        Guid TrackId,
        IReadOnlyList<AudioClipDocument> BeforeClips,
        IReadOnlyList<AudioClipDocument> AfterClips,
        Guid? SelectedClipBeforeId,
        Guid? SelectedClipAfterId,
        string Description);

    private sealed record MasteringAssistantUndoState(
        double MasterVolumeDb,
        bool LimiterEnabled,
        double LimiterCeilingDb);
}
