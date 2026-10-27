using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;
using Elbwald.DesktopTools.Audio.Editor.Basic.Services;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.ViewModels;

public sealed class AudioExportWindowViewModel : ObservableObject, IDisposable
{
    private readonly AudioProjectDocument _document;
    private readonly AudioWavExportService _exportService;
    private readonly AudioExportCheckService _exportCheckService;
    private readonly IAudioStudioStoragePicker _storagePicker;
    private readonly string _suggestedFileName;

    private AudioExportSampleRateOption _selectedSampleRate;
    private AudioExportEncodingOption _selectedEncoding;
    private bool _dither = true;
    private bool _includeEffectTail = true;
    private bool _isBusy;
    private double _progressPercent;
    private string _statusMessage = "Bereit für Master-Check oder WAV-Mixdown.";
    private string? _exportedFilePath;
    private AudioExportCheckResult? _exportCheckResult;
    private CancellationTokenSource? _cts;

    public AudioExportWindowViewModel(
        AudioProjectDocument document,
        string suggestedFileName,
        AudioWavExportService exportService,
        AudioExportCheckService exportCheckService,
        IAudioStudioStoragePicker storagePicker)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _suggestedFileName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? "Mixdown"
            : suggestedFileName;
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _exportCheckService = exportCheckService ?? throw new ArgumentNullException(nameof(exportCheckService));
        _storagePicker = storagePicker ?? throw new ArgumentNullException(nameof(storagePicker));

        var projectRate = document.SampleRate;
        var rates = new List<AudioExportSampleRateOption>
        {
            new(projectRate, $"Projekt · {projectRate / 1000d:0.#} kHz")
        };

        foreach (var rate in new[] { 44_100, 48_000, 96_000 })
        {
            if (rate != projectRate)
            {
                rates.Add(new AudioExportSampleRateOption(rate, $"{rate / 1000d:0.#} kHz"));
            }
        }

        SampleRateOptions = new ObservableCollection<AudioExportSampleRateOption>(rates);
        EncodingOptions =
        [
            new(AudioWavEncoding.Pcm16, "16-Bit PCM", "Kompatibel und klein · Dither empfohlen"),
            new(AudioWavEncoding.Pcm24, "24-Bit PCM", "Empfohlener Standard für hochwertigen Mixdown"),
            new(AudioWavEncoding.Float32, "32-Bit Float", "Maximaler Headroom · ideal für Weiterbearbeitung")
        ];

        _selectedSampleRate = SampleRateOptions[0];
        _selectedEncoding = EncodingOptions[1];

        ExportCheckCommand = new AsyncRelayCommand(RunExportCheckAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => !IsBusy);
        CancelOrCloseCommand = new RelayCommand(CancelOrClose);
    }

    public ObservableCollection<AudioExportSampleRateOption> SampleRateOptions { get; }

    public ObservableCollection<AudioExportEncodingOption> EncodingOptions { get; }

    public IAsyncRelayCommand ExportCheckCommand { get; }

    public IAsyncRelayCommand ExportCommand { get; }

    public IRelayCommand CancelOrCloseCommand { get; }

    public event EventHandler? CloseRequested;

    public string ProjectLabel =>
        $"{_document.Name} · {_document.Tracks.Count} Spuren · {_document.SampleRate / 1000d:0.#} kHz";

    public AudioExportSampleRateOption SelectedSampleRate
    {
        get => _selectedSampleRate;
        set
        {
            if (!SetProperty(ref _selectedSampleRate, value))
            {
                return;
            }

            InvalidateExportCheck();
        }
    }

    public AudioExportEncodingOption SelectedEncoding
    {
        get => _selectedEncoding;
        set
        {
            if (!SetProperty(ref _selectedEncoding, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDitherAvailable));
            OnPropertyChanged(nameof(DitherHint));
        }
    }

    public bool Dither
    {
        get => _dither;
        set => SetProperty(ref _dither, value);
    }

    public bool IsDitherAvailable => SelectedEncoding.Encoding != AudioWavEncoding.Float32;

    public string DitherHint => IsDitherAvailable
        ? "TPDF-Dither wird erst bei der finalen Integer-Quantisierung hinzugefügt."
        : "32-Bit Float benötigt kein Dither.";

    public bool IncludeEffectTail
    {
        get => _includeEffectTail;
        set
        {
            if (!SetProperty(ref _includeEffectTail, value))
            {
                return;
            }

            OnPropertyChanged(nameof(EffectTailLabel));
            InvalidateExportCheck();
        }
    }

    public string EffectTailLabel
    {
        get
        {
            if (!IncludeEffectTail)
            {
                return "Ausklang wird am Projektende abgeschnitten.";
            }

            var seconds = _exportService.EstimateEffectTailSeconds(_document);
            return seconds <= 0.01
                ? "Kein zusätzlicher Delay-/Hall-Ausklang erforderlich."
                : $"Bis zu {seconds:0.0} s zusätzlicher Delay-/Hall-Ausklang.";
        }
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

            ExportCheckCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CancelButtonLabel));
            OnPropertyChanged(nameof(CanChangeSettings));
        }
    }

    public bool CanChangeSettings => !IsBusy;

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, Math.Clamp(value, 0d, 100d));
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CancelButtonLabel => IsBusy ? "Abbrechen" : "Schließen";

    public string? ExportedFilePath
    {
        get => _exportedFilePath;
        private set => SetProperty(ref _exportedFilePath, value);
    }

    public string ExportCheckButtonLabel => _exportCheckResult is null ? "Master prüfen" : "Erneut prüfen";

    public string ExportReadinessLabel => _exportCheckResult?.Readiness switch
    {
        AudioExportReadiness.Ready => "Bereit zum Export",
        AudioExportReadiness.Review => "Export möglich · Hinweise prüfen",
        AudioExportReadiness.NotReady => "Noch nicht bereit",
        _ => "Noch nicht geprüft"
    };

    public string ExportReadinessSummary => _exportCheckResult?.Readiness switch
    {
        AudioExportReadiness.Ready =>
            "Keine technischen Blocker erkannt. LUFS bleibt eine musikalische Zielentscheidung.",
        AudioExportReadiness.Review =>
            "Kein hartes Clipping erkannt, aber mindestens ein Wert verdient vor dem finalen Export Aufmerksamkeit.",
        AudioExportReadiness.NotReady =>
            "Der gerenderte Master überschreitet die digitale Vollaussteuerung. Pegel oder Limiter prüfen.",
        _ =>
            "Der Check rendert den gewählten Mixdown offline und misst LUFS, True Peak, Clipping und Limiter-Gain-Reduction."
    };

    public string IntegratedLufsReadout => _exportCheckResult is null
        ? "– LUFS"
        : $"{_exportCheckResult.IntegratedLufs:0.0} LUFS";

    public string IntegratedLufsHint => _exportCheckResult is null
        ? "Orientierungswert, kein universeller Sollwert."
        : GetLoudnessHint(_exportCheckResult.IntegratedLufs);

    public string TruePeakReadout => _exportCheckResult is null
        ? "– dBTP"
        : $"{_exportCheckResult.TruePeakMaxDbtp:0.0} dBTP";

    public string TruePeakHint => _exportCheckResult is null
        ? "4× True-Peak-Messung des fertigen Master-Signals."
        : $"L {_exportCheckResult.TruePeakLeftDbtp:0.0} dBTP · R {_exportCheckResult.TruePeakRightDbtp:0.0} dBTP";

    public string ClippingReadout
    {
        get
        {
            if (_exportCheckResult is null)
            {
                return "–";
            }

            if (_exportCheckResult.SamplesAboveFullScale > 0)
            {
                return $"Ja · {_exportCheckResult.SamplesAboveFullScale:N0} Samples";
            }

            return _exportCheckResult.TruePeakMaxDbtp >= 0d
                ? "Ja · Inter-Sample-Peak"
                : "Nein";
        }
    }

    public string ClippingHint => _exportCheckResult is null
        ? "Berücksichtigt Sample- und Inter-Sample-Übersteuerung."
        : _exportCheckResult.HasClipping
            ? "Mindestens ein finaler Peak liegt bei/über 0 dBTP bzw. ein Sample über 0 dBFS."
            : "Keine Übersteuerung im final gerenderten Master erkannt.";

    public string LimiterReductionReadout
    {
        get
        {
            if (_exportCheckResult is null)
            {
                return "– dB GR";
            }

            if (!_exportCheckResult.LimiterEnabled)
            {
                return "Limiter aus";
            }

            var reduction = _exportCheckResult.LimiterReduction;
            return !reduction.HasData
                ? "0.0 dB GR"
                : $"max {reduction.MaxReductionDb:0.0} dB · Ø {reduction.AverageReductionDb:0.0} dB";
        }
    }

    public string LimiterReductionHint
    {
        get
        {
            if (_exportCheckResult is null)
            {
                return "Zeigt, wie stark der Master-Limiter tatsächlich eingreift.";
            }

            if (!_exportCheckResult.LimiterEnabled)
            {
                return "Master-Limiter ist für diesen Mix ausgeschaltet.";
            }

            var reduction = _exportCheckResult.LimiterReduction;
            return reduction.HasData
                ? $"{reduction.PercentAbove3Db:0.#}% der Laufzeit > 3 dB GR · Ceiling {_exportCheckResult.LimiterCeilingDbtp:0.0} dBTP"
                : $"Keine messbare Gain-Reduction · Ceiling {_exportCheckResult.LimiterCeilingDbtp:0.0} dBTP";
        }
    }

    public string ExportCheckHintsText => _exportCheckResult is null
        ? "Master-Check ausführen, bevor du den finalen Mixdown beurteilst."
        : string.Join(Environment.NewLine, _exportCheckResult.Hints.Select(hint => $"• {hint}"));

    private async Task RunExportCheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        StatusMessage = "Master-Check rendert den Mix …";

        try
        {
            var progress = new Progress<double>(value =>
            {
                ProgressPercent = value * 100d;
                StatusMessage = $"Master-Check läuft … {ProgressPercent:0}%";
            });

            var result = await _exportCheckService.AnalyzeAsync(
                _document,
                CreateExportSettings(),
                progress,
                _cts.Token);

            _exportCheckResult = result;
            ProgressPercent = 100;
            StatusMessage = result.Readiness switch
            {
                AudioExportReadiness.Ready => "Master-Check abgeschlossen · bereit zum Export.",
                AudioExportReadiness.Review => "Master-Check abgeschlossen · Hinweise prüfen.",
                AudioExportReadiness.NotReady => "Master-Check abgeschlossen · technisches Problem erkannt.",
                _ => "Master-Check abgeschlossen."
            };
            NotifyExportCheckChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Master-Check abgebrochen.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            ArgumentOutOfRangeException)
        {
            StatusMessage = $"Master-Check fehlgeschlagen: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var path = await _storagePicker.PickWavToSaveAsync(_suggestedFileName);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Export abgebrochen.";
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        ProgressPercent = 0;
        ExportedFilePath = null;
        StatusMessage = "Mixdown wird gerendert …";

        try
        {
            var progress = new Progress<double>(value =>
            {
                ProgressPercent = value * 100d;
                StatusMessage = $"Mixdown wird gerendert … {ProgressPercent:0}%";
            });

            var settings = CreateExportSettings();

            var result = await _exportService.ExportAsync(
                _document,
                path,
                settings,
                progress,
                _cts.Token);

            ExportedFilePath = result.FilePath;
            ProgressPercent = 100;

            var peakText = result.PeakDbfs <= -119
                ? "stumm"
                : $"{result.PeakDbfs:0.0} dBFS Peak";

            if (result.SamplesAboveFullScale > 0 && result.Encoding != AudioWavEncoding.Float32)
            {
                StatusMessage =
                    $"Fertig · {result.FormatLabel} · {result.SampleRate / 1000d:0.#} kHz · {peakText}. " +
                    $"WARNUNG: {result.SamplesAboveFullScale:N0} Samples lagen vor der PCM-Begrenzung über 0 dBFS.";
            }
            else if (result.SamplesAboveFullScale > 0)
            {
                StatusMessage =
                    $"Fertig · {result.FormatLabel} · {result.SampleRate / 1000d:0.#} kHz · {peakText}. " +
                    "Float-Headroom über 0 dBFS wurde erhalten.";
            }
            else
            {
                StatusMessage =
                    $"Fertig · {result.FormatLabel} · {result.SampleRate / 1000d:0.#} kHz · {peakText}.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Export abgebrochen. Unvollständige Datei wurde verworfen.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            ArgumentOutOfRangeException)
        {
            StatusMessage = $"Export fehlgeschlagen: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AudioWavExportSettings CreateExportSettings() => new(
        SelectedSampleRate.SampleRate,
        SelectedEncoding.Encoding,
        Dither && IsDitherAvailable,
        IncludeEffectTail);

    private void InvalidateExportCheck()
    {
        if (_exportCheckResult is null)
        {
            return;
        }

        _exportCheckResult = null;
        StatusMessage = "Export-Einstellungen geändert · Master-Check erneut ausführen.";
        NotifyExportCheckChanged();
    }

    private void NotifyExportCheckChanged()
    {
        OnPropertyChanged(nameof(ExportCheckButtonLabel));
        OnPropertyChanged(nameof(ExportReadinessLabel));
        OnPropertyChanged(nameof(ExportReadinessSummary));
        OnPropertyChanged(nameof(IntegratedLufsReadout));
        OnPropertyChanged(nameof(IntegratedLufsHint));
        OnPropertyChanged(nameof(TruePeakReadout));
        OnPropertyChanged(nameof(TruePeakHint));
        OnPropertyChanged(nameof(ClippingReadout));
        OnPropertyChanged(nameof(ClippingHint));
        OnPropertyChanged(nameof(LimiterReductionReadout));
        OnPropertyChanged(nameof(LimiterReductionHint));
        OnPropertyChanged(nameof(ExportCheckHintsText));
    }

    private static string GetLoudnessHint(double integratedLufs)
    {
        if (integratedLufs > -8d)
        {
            return "Sehr laut · Dynamik und Limiter-Arbeit kritisch gegenhören.";
        }

        if (integratedLufs > -11d)
        {
            return "Lautes modernes Master · kein technischer Fehler.";
        }

        if (integratedLufs >= -16d)
        {
            return "Typischer Bereich vieler moderner Musik-Master.";
        }

        if (integratedLufs >= -24d)
        {
            return "Dynamischer/leiser Mix · je nach Stil völlig passend.";
        }

        return "Sehr dynamisch/leise · mit passendem Referenzmaterial vergleichen.";
    }

    private void CancelOrClose()
    {
        if (IsBusy)
        {
            StatusMessage = "Vorgang wird abgebrochen …";
            _cts?.Cancel();
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}

public sealed record AudioExportSampleRateOption(int SampleRate, string Label)
{
    public override string ToString() => Label;
}

public sealed record AudioExportEncodingOption(
    AudioWavEncoding Encoding,
    string Label,
    string Description)
{
    public override string ToString() => Label;
}
