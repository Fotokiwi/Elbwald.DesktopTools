using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

public sealed class StemRepairWindowViewModel : ObservableObject, IDisposable
{
    private readonly AudioEditorViewModel _editor;
    private readonly StemRepairAnalysisService _analysisService;
    private AudioTrackViewModel _track;
    private bool _isAnalyzing;
    private double _analysisProgress;
    private string _statusLabel = "Noch nicht analysiert.";
    private string _summaryLabel = "Analyse starten, um technische Reparaturhinweise für diesen Stem zu erhalten.";
    private string _measurementLabel = "–";
    private bool _isTopmost;
    private CancellationTokenSource? _analysisCts;

    public StemRepairWindowViewModel(
        AudioEditorViewModel editor,
        AudioTrackViewModel track,
        StemRepairAnalysisService analysisService)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _track = track ?? throw new ArgumentNullException(nameof(track));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));

        _track.PropertyChanged += OnTrackPropertyChanged;

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && Track.HasClips);
        OpenEqCommand = new RelayCommand(OpenEqWindow);
    }

    public AudioTrackViewModel Track => _track;

    public ObservableCollection<StemRepairFindingViewModel> Findings { get; } = [];

    public IAsyncRelayCommand AnalyzeCommand { get; }

    public IRelayCommand OpenEqCommand { get; }

    public string TrackTitle => $"{Track.TrackLabel} · {Track.Name}";

    public string TrackSubtitle =>
        $"{Track.ChannelLabel} · {Track.ClipCountLabel} · {Track.ProjectDurationLabel}";

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!SetProperty(ref _isAnalyzing, value))
            {
                return;
            }

            AnalyzeCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(AnalyzeButtonLabel));
        }
    }

    public double AnalysisProgress
    {
        get => _analysisProgress;
        private set => SetProperty(ref _analysisProgress, Math.Clamp(value, 0d, 100d));
    }

    public string AnalyzeButtonLabel => IsAnalyzing ? "Analysiere …" : "Stem analysieren";

    public string StatusLabel
    {
        get => _statusLabel;
        private set => SetProperty(ref _statusLabel, value);
    }

    public string SummaryLabel
    {
        get => _summaryLabel;
        private set => SetProperty(ref _summaryLabel, value);
    }

    public string MeasurementLabel
    {
        get => _measurementLabel;
        private set => SetProperty(ref _measurementLabel, value);
    }

    public bool HasFindings => Findings.Count > 0;

    public bool IsTopmost
    {
        get => _isTopmost;
        set => SetProperty(ref _isTopmost, value);
    }

    public void SetTrack(AudioTrackViewModel track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (ReferenceEquals(_track, track))
        {
            return;
        }

        _analysisCts?.Cancel();
        _track.PropertyChanged -= OnTrackPropertyChanged;
        _track = track;
        _track.PropertyChanged += OnTrackPropertyChanged;

        Findings.Clear();
        AnalysisProgress = 0;
        StatusLabel = "Noch nicht analysiert.";
        SummaryLabel = "Analyse starten, um technische Reparaturhinweise für diesen Stem zu erhalten.";
        MeasurementLabel = "–";

        OnPropertyChanged(nameof(Track));
        NotifyTrackBindings();
        AnalyzeCommand.NotifyCanExecuteChanged();
    }

    private async Task AnalyzeAsync()
    {
        if (IsAnalyzing)
        {
            return;
        }

        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _analysisCts = new CancellationTokenSource();

        IsAnalyzing = true;
        AnalysisProgress = 0;
        StatusLabel = "Stem wird technisch untersucht …";
        Findings.Clear();
        OnPropertyChanged(nameof(HasFindings));

        try
        {
            var progress = new Progress<double>(value => AnalysisProgress = value * 100d);
            var result = await _analysisService.AnalyzeAsync(
                Track,
                progress,
                _analysisCts.Token);

            foreach (var finding in result.Findings)
            {
                Findings.Add(new StemRepairFindingViewModel(Track, finding, _editor));
            }

            OnPropertyChanged(nameof(HasFindings));
            MeasurementLabel =
                $"Aktiv ≈ {result.ActiveLevelDbfs:0.0} dBFS · leise Bereiche ≈ {result.NoiseFloorDbfs:0.0} dBFS · " +
                $"{result.AnalyzedDurationSeconds:0.#} s analysiert @ {result.AnalysisSampleRate / 1000d:0.#} kHz";

            if (result.Findings.Count == 0)
            {
                StatusLabel = "Keine eindeutige technische Reparaturstelle erkannt.";
                SummaryLabel =
                    "Die automatische Analyse sieht aktuell keinen belastbaren Grund für Gate, Brummfilter, Notch oder De-Esser. Das bedeutet nicht, dass keine kreative Klangbearbeitung sinnvoll sein kann.";
            }
            else
            {
                var high = result.Findings.Count(item => item.Severity == StemRepairFindingSeverity.High);
                var medium = result.Findings.Count(item => item.Severity == StemRepairFindingSeverity.Medium);
                StatusLabel = $"{result.Findings.Count} Reparaturhinweis(e) gefunden.";
                SummaryLabel =
                    $"Davon {high} hoch und {medium} mittel priorisiert. Empfehlungen werden nie automatisch aktiviert – erst „Anwenden“ ändert die Spur.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel = "Analyse abgebrochen.";
        }
        catch (Exception exception)
        {
            StatusLabel = $"Analyse fehlgeschlagen: {exception.Message}";
            SummaryLabel = "Die Spur wurde nicht verändert.";
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void OpenEqWindow()
    {
        _editor.SelectedTrack = Track;
        _editor.OpenEqAnalysisCommand.Execute(null);
    }

    private void OnTrackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioTrackViewModel.Name) or
            nameof(AudioTrackViewModel.TrackNumber) or
            nameof(AudioTrackViewModel.ChannelLayout) or
            nameof(AudioTrackViewModel.ClipCount) or
            nameof(AudioTrackViewModel.ProjectDurationLabel))
        {
            NotifyTrackBindings();
        }

        if (e.PropertyName is nameof(AudioTrackViewModel.ExpanderEnabled) or
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
            nameof(AudioTrackViewModel.DeEsserStrength))
        {
            OnPropertyChanged(nameof(Track));
        }
    }

    private void NotifyTrackBindings()
    {
        OnPropertyChanged(nameof(TrackTitle));
        OnPropertyChanged(nameof(TrackSubtitle));
    }

    public void Dispose()
    {
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _track.PropertyChanged -= OnTrackPropertyChanged;
    }
}

public sealed class StemRepairFindingViewModel : ObservableObject
{
    private readonly AudioTrackViewModel _track;
    private readonly StemRepairFinding _finding;
    private readonly AudioEditorViewModel _editor;
    private bool _wasApplied;
    private bool _isAuditioning;
    private string _auditionStatusLabel = string.Empty;

    public StemRepairFindingViewModel(
        AudioTrackViewModel track,
        StemRepairFinding finding,
        AudioEditorViewModel editor)
    {
        _track = track ?? throw new ArgumentNullException(nameof(track));
        _finding = finding ?? throw new ArgumentNullException(nameof(finding));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        ApplyCommand = new RelayCommand(ApplyRecommendation);
        ListenRemovedCommand = new AsyncRelayCommand(ListenRemovedAsync, () => !IsAuditioning);
    }

    public string Title => _finding.Title;

    public string Detail => _finding.Detail;

    public string Recommendation => _finding.Recommendation;

    public string SeverityLabel => _finding.Severity switch
    {
        StemRepairFindingSeverity.High => "HOCH",
        StemRepairFindingSeverity.Medium => "MITTEL",
        StemRepairFindingSeverity.Low => "NIEDRIG",
        _ => "INFO"
    };

    public string ConfidenceLabel => $"{ConfidenceBandLabel} · {_finding.Confidence * 100d:0}%";

    public string ConfidenceBandLabel => _finding.Confidence switch
    {
        >= 0.80d => "HOHE SICHERHEIT",
        >= 0.60d => "PLAUSIBEL",
        _ => "PRÜFEN"
    };

    public string PersistenceLabel => _finding.Persistence is double persistence
        ? $"zeitlich {persistence * 100d:0}%"
        : string.Empty;

    public string ApplyLabel => _wasApplied ? "Angewendet" : "Empfehlung anwenden";

    public bool IsAuditioning
    {
        get => _isAuditioning;
        private set
        {
            if (!SetProperty(ref _isAuditioning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ListenRemovedLabel));
            ListenRemovedCommand.NotifyCanExecuteChanged();
        }
    }

    public string ListenRemovedLabel => IsAuditioning ? "Höre entferntes Signal …" : "Störung vorhören";

    public string AuditionStatusLabel
    {
        get => _auditionStatusLabel;
        private set => SetProperty(ref _auditionStatusLabel, value);
    }

    public IRelayCommand ApplyCommand { get; }

    public IAsyncRelayCommand ListenRemovedCommand { get; }

    private async Task ListenRemovedAsync()
    {
        if (IsAuditioning)
        {
            return;
        }

        IsAuditioning = true;
        AuditionStatusLabel = "Rendere Dry − Wet …";
        try
        {
            var clip = await _editor.AuditionStemRepairAsync(_track, _finding);
            AuditionStatusLabel = clip.MonitorGainDb > 0.05d
                ? $"Nur entfernter Anteil · Monitor +{clip.MonitorGainDb:0.#} dB · ab {FormatTime(clip.ProjectStartSeconds)}"
                : $"Nur entfernter Anteil · ab {FormatTime(clip.ProjectStartSeconds)}";
        }
        catch (OperationCanceledException)
        {
            AuditionStatusLabel = "Abhörung beendet.";
        }
        catch (Exception exception) when (
            exception is IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            DllNotFoundException)
        {
            AuditionStatusLabel = $"Vorhören nicht möglich: {exception.Message}";
        }
        finally
        {
            IsAuditioning = false;
        }
    }

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return span.TotalHours >= 1d
            ? $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private void ApplyRecommendation()
    {
        switch (_finding.Type)
        {
            case StemRepairFindingType.NoiseFloor:
                _track.ExpanderThresholdDb = _finding.SuggestedThresholdDb ?? -45d;
                _track.ExpanderReductionDb = _finding.SuggestedReductionDb ?? 18d;
                _track.ExpanderEnabled = true;
                break;

            case StemRepairFindingType.Hum:
                _track.HumFrequencyHz = _finding.FrequencyHz ?? 50d;
                _track.HumHarmonics = _finding.SuggestedHarmonics ?? 4;
                _track.HumReductionDb = _finding.SuggestedStrength ?? 18d;
                _track.HumRemovalEnabled = true;
                break;

            case StemRepairFindingType.NarrowbandInterference:
                _track.NotchFrequencyHz = _finding.FrequencyHz ?? 6_500d;
                _track.NotchWidthPercent = _finding.SuggestedWidthPercent ?? 15d;
                _track.NotchEnabled = true;
                break;

            case StemRepairFindingType.Sibilance:
                _track.DeEsserFrequencyHz = _finding.FrequencyHz ?? 7_000d;
                _track.DeEsserStrength = _finding.SuggestedStrength ?? 35d;
                _track.DeEsserEnabled = true;
                break;
        }

        _wasApplied = true;
        OnPropertyChanged(nameof(ApplyLabel));
    }
}
