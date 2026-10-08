using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.FileOperations;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Dates;
using Elbwald.DesktopTools.Contracts.Media.Sorting;
using Elbwald.DesktopTools.UI.Formatting;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortViewModel
    : ObservableObject
{
    private const int PreviewItemLimit = 500;
    private const int IssueItemLimit = 100;

    private readonly IMediaAnalyzer _mediaAnalyzer;
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaSortPlanner _sortPlanner;

    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _durationTimer;

    private CancellationTokenSource? _cancellation;

    private string _sourcePath =
        string.Empty;

    private string _destinationPath =
        string.Empty;

    private bool _includeSubdirectories = true;
    private bool _isBusy;
    private bool _hasPlan;
    private bool _isProgressIndeterminate;
    private double _progressPercent;

    private string _selectedRule =
        "Jahr / Monat";

    private string _selectedOperation =
        "Kopieren";

    private string _selectedDateConfidence =
        "Hoch (empfohlen)";

    private string _statusMessage =
        "Quelle und Ziel auswählen. 0032 bewertet Zeitquellen abgestuft und erklärt Abweichungen.";

    private string _elapsedTimeText =
        "Dauer: 00:00:00";

    private string _plannedFiles =
        "0";

    private string _plannedSize =
        "0 B";

    private string _missingDateCount =
        "0";

    private string _conflictCount =
        "0";

    private string _ignoredCount =
        "0";

    private string _planState =
        "Noch keine Vorschau";

    private string _previewSummary =
        "Noch kein Dry Run vorhanden.";

    private IReadOnlyList<PhotoSortPlanItemViewModel> _previewItems =
        Array.Empty<PhotoSortPlanItemViewModel>();

    private IReadOnlyList<PhotoSortIssueViewModel> _issues =
        Array.Empty<PhotoSortIssueViewModel>();

    public PhotoSortViewModel(
        IMediaAnalyzer mediaAnalyzer,
        IMediaSourcePicker sourcePicker,
        IMediaSortPlanner sortPlanner)
    {
        ArgumentNullException.ThrowIfNull(mediaAnalyzer);
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(sortPlanner);

        _mediaAnalyzer = mediaAnalyzer;
        _sourcePicker = sourcePicker;
        _sortPlanner = sortPlanner;

        _durationTimer =
            new DispatcherTimer
            {
                Interval =
                    TimeSpan.FromSeconds(1)
            };

        _durationTimer.Tick +=
            (_, _) => UpdateDuration();

        ChooseSourceCommand =
            new AsyncRelayCommand(
                ChooseSourceAsync,
                CanChoosePath);

        ChooseDestinationCommand =
            new AsyncRelayCommand(
                ChooseDestinationAsync,
                CanChoosePath);

        BuildPreviewCommand =
            new AsyncRelayCommand(
                BuildPreviewAsync,
                CanBuildPreview);

        CancelCommand =
            new RelayCommand(
                Cancel,
                () => IsBusy);
    }

    public IAsyncRelayCommand ChooseSourceCommand { get; }

    public IAsyncRelayCommand ChooseDestinationCommand { get; }

    public IAsyncRelayCommand BuildPreviewCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IReadOnlyList<string> RuleOptions { get; } =
        new[]
        {
            "Jahr / Monat",
            "Jahr / Kamera / Monat"
        };

    public IReadOnlyList<string> OperationOptions { get; } =
        new[]
        {
            "Kopieren",
            "Verschieben"
        };

    public IReadOnlyList<string> DateConfidenceOptions { get; } =
        new[]
        {
            "Sehr hoch",
            "Hoch (empfohlen)",
            "Mittel"
        };

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (SetProperty(
                    ref _sourcePath,
                    value))
            {
                BuildPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(
                    ref _destinationPath,
                    value))
            {
                BuildPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set => SetProperty(
            ref _includeSubdirectories,
            value);
    }

    public string SelectedRule
    {
        get => _selectedRule;
        set => SetProperty(
            ref _selectedRule,
            value);
    }

    public string SelectedOperation
    {
        get => _selectedOperation;
        set => SetProperty(
            ref _selectedOperation,
            value);
    }

    public string SelectedDateConfidence
    {
        get => _selectedDateConfidence;
        set => SetProperty(
            ref _selectedDateConfidence,
            value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(
                    ref _isBusy,
                    value))
            {
                return;
            }

            ChooseSourceCommand.NotifyCanExecuteChanged();
            ChooseDestinationCommand.NotifyCanExecuteChanged();
            BuildPreviewCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasPlan
    {
        get => _hasPlan;
        private set => SetProperty(
            ref _hasPlan,
            value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(
            ref _isProgressIndeterminate,
            value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(
            ref _progressPercent,
            value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(
            ref _statusMessage,
            value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        private set => SetProperty(
            ref _elapsedTimeText,
            value);
    }

    public string PlannedFiles
    {
        get => _plannedFiles;
        private set => SetProperty(
            ref _plannedFiles,
            value);
    }

    public string PlannedSize
    {
        get => _plannedSize;
        private set => SetProperty(
            ref _plannedSize,
            value);
    }

    public string MissingDateCount
    {
        get => _missingDateCount;
        private set => SetProperty(
            ref _missingDateCount,
            value);
    }

    public string ConflictCount
    {
        get => _conflictCount;
        private set => SetProperty(
            ref _conflictCount,
            value);
    }

    public string IgnoredCount
    {
        get => _ignoredCount;
        private set => SetProperty(
            ref _ignoredCount,
            value);
    }

    public string PlanState
    {
        get => _planState;
        private set => SetProperty(
            ref _planState,
            value);
    }

    public string PreviewSummary
    {
        get => _previewSummary;
        private set => SetProperty(
            ref _previewSummary,
            value);
    }

    public IReadOnlyList<PhotoSortPlanItemViewModel> PreviewItems
    {
        get => _previewItems;
        private set => SetProperty(
            ref _previewItems,
            value);
    }

    public IReadOnlyList<PhotoSortIssueViewModel> Issues
    {
        get => _issues;
        private set
        {
            if (SetProperty(
                    ref _issues,
                    value))
            {
                OnPropertyChanged(
                    nameof(HasIssues));
            }
        }
    }

    public bool HasIssues =>
        Issues.Count > 0;

    private bool CanChoosePath()
    {
        return !IsBusy;
    }

    private bool CanBuildPreview()
    {
        return !IsBusy
               && !string.IsNullOrWhiteSpace(SourcePath)
               && !string.IsNullOrWhiteSpace(DestinationPath);
    }

    private async Task ChooseSourceAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePath =
                path;

            StatusMessage =
                "Quelle gewählt. Zielordner auswählen und Dry Run erzeugen.";
        }
    }

    private async Task ChooseDestinationAsync()
    {
        var path =
            await _sourcePicker.PickFolderAsync();

        if (!string.IsNullOrWhiteSpace(path))
        {
            DestinationPath =
                path;

            StatusMessage =
                "Ziel gewählt. Der Dry Run verändert weiterhin keine Dateien.";
        }
    }

    private async Task BuildPreviewAsync()
    {
        if (!CanBuildPreview())
        {
            return;
        }

        _cancellation?.Dispose();
        _cancellation =
            new CancellationTokenSource();

        IsBusy = true;
        HasPlan = false;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;

        PreviewItems =
            Array.Empty<PhotoSortPlanItemViewModel>();

        Issues =
            Array.Empty<PhotoSortIssueViewModel>();

        PlannedFiles = "0";
        PlannedSize = "0 B";
        MissingDateCount = "0";
        ConflictCount = "0";
        IgnoredCount = "0";
        PlanState = "Analyse läuft";
        PreviewSummary =
            "Dry Run wird vorbereitet …";

        StartDuration();

        try
        {
            var progress =
                new Progress<MediaAnalysisProgress>(
                    UpdateAnalysisProgress);

            var analysis =
                await _mediaAnalyzer.AnalyzeAsync(
                    SourcePath,
                    new MediaAnalysisOptions
                    {
                        Recursive =
                            IncludeSubdirectories,
                        IncludeUnknownFiles =
                            true
                    },
                    progress,
                    _cancellation.Token);

            _cancellation.Token.ThrowIfCancellationRequested();

            IsProgressIndeterminate = true;
            StatusMessage =
                "Sortierplan wird read-only berechnet …";
            PlanState =
                "Planung läuft";

            var sortOptions =
                new MediaSortOptions
                {
                    Rule =
                        SelectedRule == "Jahr / Kamera / Monat"
                            ? MediaSortRule.YearCameraMonth
                            : MediaSortRule.YearMonth,

                    OperationKind =
                        SelectedOperation == "Verschieben"
                            ? FileOperationKind.Move
                            : FileOperationKind.Copy,

                    MinimumDateConfidence =
                        SelectedDateConfidence switch
                        {
                            "Sehr hoch" =>
                                MediaDateConfidence.VeryHigh,

                            "Mittel" =>
                                MediaDateConfidence.Medium,

                            _ =>
                                MediaDateConfidence.High
                        }
                };

            var plan =
                await Task.Run(
                    () =>
                        _sortPlanner.CreatePlan(
                            SourcePath,
                            DestinationPath,
                            analysis.Files,
                            sortOptions,
                            _cancellation.Token),
                    _cancellation.Token);

            ApplyPlan(
                plan);

            StatusMessage =
                plan.ProblemCount > 0
                    ? $"Dry Run abgeschlossen: {plan.ProblemCount:N0} Problem(e). Es wird nichts ausgeführt."
                    : plan.DateReviewCount > 0
                        ? $"Dry Run abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) geplant, "
                          + $"{plan.DateReviewCount:N0} Datumsfall/-fälle zur Prüfung. Es wird nichts ausgeführt."
                        : $"Dry Run abgeschlossen: {plan.PlannedFileCount:N0} Datei(en) konfliktfrei geplant. Es wird nichts ausgeführt.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage =
                "Dry Run abgebrochen. Es wurden keine Dateien verändert.";

            PlanState =
                "Abgebrochen";
        }
        catch (Exception exception)
        {
            StatusMessage =
                "Dry Run konnte nicht abgeschlossen werden: "
                + exception.GetBaseException().Message;

            PlanState =
                "Fehler";
        }
        finally
        {
            StopDuration();

            IsBusy = false;
            IsProgressIndeterminate = false;

            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void ApplyPlan(
        MediaSortPlan plan)
    {
        var conflictIndexes =
            plan.OperationPlan.Conflicts
                .Select(conflict =>
                    conflict.OperationIndex)
                .ToHashSet();

        PreviewItems =
            plan.Items
                .Take(
                    PreviewItemLimit)
                .Select(item =>
                    new PhotoSortPlanItemViewModel(
                        item,
                        conflictIndexes.Contains(
                            item.OperationIndex)))
                .ToArray();

        Issues =
            plan.Issues
                .OrderByDescending(issue =>
                    issue.Severity)
                .ThenBy(
                    issue => issue.Path,
                    StringComparer.CurrentCultureIgnoreCase)
                .Take(
                    IssueItemLimit)
                .Select(issue =>
                    new PhotoSortIssueViewModel(
                        issue))
                .ToArray();

        PlannedFiles =
            plan.PlannedFileCount.ToString("N0");

        PlannedSize =
            FormatBytes(
                plan.PlannedBytes);

        MissingDateCount =
            plan.DateReviewCount.ToString("N0");

        ConflictCount =
            plan.ConflictCount.ToString("N0");

        IgnoredCount =
            plan.IgnoredNonImageCount.ToString("N0");

        PlanState =
            !plan.CanExecute
                ? "Plan prüfen"
                : plan.DateReviewCount > 0
                    ? "Plan mit Datumshinweisen"
                    : "Konfliktfreier Plan";

        PreviewSummary =
            plan.PlannedFileCount > PreviewItemLimit
                ? $"{PreviewItemLimit:N0} von {plan.PlannedFileCount:N0} geplanten Dateien angezeigt."
                : $"{plan.PlannedFileCount:N0} geplante Datei(en) angezeigt.";

        HasPlan = true;
        ProgressPercent = 100;
    }

    private void UpdateAnalysisProgress(
        MediaAnalysisProgress progress)
    {
        switch (progress.Stage)
        {
            case MediaAnalysisStage.Scanning:
                IsProgressIndeterminate = true;
                ProgressPercent = 0;

                StatusMessage =
                    $"Quelle wird gescannt … {progress.FilesDiscovered:N0} Datei(en) gefunden.";
                break;

            case MediaAnalysisStage.ReadingMetadata:
                IsProgressIndeterminate = false;

                ProgressPercent =
                    progress.MetadataTotal <= 0
                        ? 100
                        : progress.MetadataProcessed
                          * 100d
                          / progress.MetadataTotal;

                StatusMessage =
                    $"Lese Aufnahmedaten … {progress.MetadataProcessed:N0} von {progress.MetadataTotal:N0} · "
                    + $"{progress.MetadataCacheHits:N0} Cache-Treffer.";
                break;

            case MediaAnalysisStage.Completed:
                IsProgressIndeterminate = false;
                ProgressPercent = 100;
                break;
        }
    }

    private void Cancel()
    {
        _cancellation?.Cancel();
    }

    private void StartDuration()
    {
        _stopwatch.Restart();

        UpdateDuration();

        _durationTimer.Start();
    }

    private void StopDuration()
    {
        _stopwatch.Stop();

        _durationTimer.Stop();

        UpdateDuration();
    }

    private void UpdateDuration()
    {
        ElapsedTimeText =
            "Dauer: "
            + ProcessingDurationFormatter.Format(
                _stopwatch.Elapsed);
    }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }
}
