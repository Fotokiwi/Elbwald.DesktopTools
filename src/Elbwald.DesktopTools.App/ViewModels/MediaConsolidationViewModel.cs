using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Consolidation;
using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaConsolidationViewModel : ObservableObject
{
    private readonly IMediaSourcePicker _sourcePicker;
    private readonly IMediaConsolidationPlanner _planner;
    private readonly INavigationService _navigationService;
    private CancellationTokenSource? _cancellationTokenSource;
    private MediaConsolidationPlan? _plan;

    [ObservableProperty]
    private string targetPath = string.Empty;

    [ObservableProperty]
    private MediaConsolidationStructureOptionViewModel selectedStructure;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasPlan;

    [ObservableProperty]
    private string statusMessage = "Wähle mindestens eine indexierte Quelle und einen Zielordner. Der Dry-Run verändert keine Dateien.";

    [ObservableProperty]
    private string progressPhase = string.Empty;

    [ObservableProperty]
    private string progressDetail = string.Empty;

    [ObservableProperty]
    private int progressPercentage;

    [ObservableProperty]
    private bool progressIsIndeterminate;

    [ObservableProperty]
    private int sourceLocationCount;

    [ObservableProperty]
    private int uniqueContentCount;

    [ObservableProperty]
    private int duplicateSourceCount;

    [ObservableProperty]
    private int plannedCopyCount;

    [ObservableProperty]
    private int alreadyInTargetCount;

    [ObservableProperty]
    private int conflictCount;

    [ObservableProperty]
    private int issueCount;

    [ObservableProperty]
    private string plannedCopySize = "0 B";

    [ObservableProperty]
    private IReadOnlyList<MediaConsolidationPlanItemViewModel> previewItems = Array.Empty<MediaConsolidationPlanItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<string> issues = Array.Empty<string>();

    public MediaConsolidationViewModel(
        IMediaSourcePicker sourcePicker,
        IMediaConsolidationPlanner planner,
        INavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(sourcePicker);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(navigationService);

        _sourcePicker = sourcePicker;
        _planner = planner;
        _navigationService = navigationService;

        StructureOptions =
        [
            new MediaConsolidationStructureOptionViewModel(
                MediaConsolidationStructureMode.YearMonthOriginalName,
                "Jahr / Monat / Originalname",
                "2025 / 08 / IMG_1234.jpg"),
            new MediaConsolidationStructureOptionViewModel(
                MediaConsolidationStructureMode.YearOriginalName,
                "Jahr / Originalname",
                "2025 / IMG_1234.jpg"),
            new MediaConsolidationStructureOptionViewModel(
                MediaConsolidationStructureMode.PreserveRelativeStructure,
                "Quellstruktur erhalten",
                "Urlaub / Ostsee / IMG_1234.jpg")
        ];
        selectedStructure = StructureOptions[0];

        AddSourceCommand = new AsyncRelayCommand(AddSourceAsync, () => !IsBusy);
        ChooseTargetCommand = new AsyncRelayCommand(ChooseTargetAsync, () => !IsBusy);
        CreatePlanCommand = new AsyncRelayCommand(CreatePlanAsync, CanCreatePlan);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearPlanCommand = new RelayCommand(ClearPlan, () => HasPlan && !IsBusy);
        OpenMediaIndexCommand = new RelayCommand(() => _navigationService.NavigateTo("media-index"));
    }

    public ObservableCollection<MediaConsolidationSourceViewModel> Sources { get; } = [];

    public IReadOnlyList<MediaConsolidationStructureOptionViewModel> StructureOptions { get; }

    public IAsyncRelayCommand AddSourceCommand { get; }

    public IAsyncRelayCommand ChooseTargetCommand { get; }

    public IAsyncRelayCommand CreatePlanCommand { get; }

    public IRelayCommand CancelCommand { get; }

    public IRelayCommand ClearPlanCommand { get; }

    public IRelayCommand OpenMediaIndexCommand { get; }

    public bool HasSources => Sources.Count > 0;

    public bool HasIssues => Issues.Count > 0;

    public bool HasPreviewItems => PreviewItems.Count > 0;

    public string SourceSummary => Sources.Count switch
    {
        0 => "Noch keine Quelle ausgewählt",
        1 => "1 Quelle ausgewählt",
        _ => $"{Sources.Count:N0} Quellen ausgewählt"
    };

    public string PlanSummary => !HasPlan
        ? string.Empty
        : $"{UniqueContentCount:N0} eindeutige Inhalte aus {SourceLocationCount:N0} indexierten Fundorten · {PlannedCopyCount:N0} geplante Kopien";

    partial void OnIsBusyChanged(bool value)
    {
        AddSourceCommand.NotifyCanExecuteChanged();
        ChooseTargetCommand.NotifyCanExecuteChanged();
        CreatePlanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ClearPlanCommand.NotifyCanExecuteChanged();
    }

    partial void OnTargetPathChanged(string value)
    {
        if (!IsBusy)
        {
            ResetPlanResult();
        }

        CreatePlanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedStructureChanged(MediaConsolidationStructureOptionViewModel value)
    {
        if (!IsBusy)
        {
            ResetPlanResult();
        }
    }

    partial void OnHasPlanChanged(bool value)
    {
        ClearPlanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PlanSummary));
    }

    partial void OnIssuesChanged(IReadOnlyList<string> value) =>
        OnPropertyChanged(nameof(HasIssues));

    partial void OnPreviewItemsChanged(IReadOnlyList<MediaConsolidationPlanItemViewModel> value) =>
        OnPropertyChanged(nameof(HasPreviewItems));

    partial void OnSourceLocationCountChanged(int value) => OnPropertyChanged(nameof(PlanSummary));
    partial void OnUniqueContentCountChanged(int value) => OnPropertyChanged(nameof(PlanSummary));
    partial void OnPlannedCopyCountChanged(int value) => OnPropertyChanged(nameof(PlanSummary));

    private async Task AddSourceAsync()
    {
        var selected = await _sourcePicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            StatusMessage = "Der ausgewählte Quellpfad ist ungültig.";
            return;
        }

        if (Sources.Any(source => PathsEqual(source.Path, normalized)))
        {
            StatusMessage = "Diese Quelle ist bereits in der Konsolidierungsplanung enthalten.";
            return;
        }

        var source = new MediaConsolidationSourceViewModel(
            normalized,
            RemoveSource);
        Sources.Add(source);
        OnSourcesChanged();
        ResetPlanResult();
        StatusMessage = "Quelle hinzugefügt. Für den Dry-Run werden ausschließlich bereits indexierte Medien berücksichtigt.";
    }

    private async Task ChooseTargetAsync()
    {
        var selected = await _sourcePicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        TargetPath = selected;
        StatusMessage = "Ziel gewählt. Du kannst den Pfad im Feld noch auf einen neuen Unterordner erweitern.";
    }

    private void RemoveSource(MediaConsolidationSourceViewModel source)
    {
        if (IsBusy || !Sources.Remove(source))
        {
            return;
        }

        OnSourcesChanged();
        ResetPlanResult();
        StatusMessage = HasSources
            ? "Quelle entfernt. Dry-Run erneut erstellen, wenn die Auswahl vollständig ist."
            : "Wähle mindestens eine indexierte Quelle.";
    }

    private void OnSourcesChanged()
    {
        OnPropertyChanged(nameof(HasSources));
        OnPropertyChanged(nameof(SourceSummary));
        CreatePlanCommand.NotifyCanExecuteChanged();
    }

    private bool CanCreatePlan() =>
        !IsBusy
        && Sources.Count > 0
        && !string.IsNullOrWhiteSpace(TargetPath);

    private async Task CreatePlanAsync()
    {
        if (!CanCreatePlan())
        {
            return;
        }

        ResetPlanResult();
        IsBusy = true;
        ProgressPhase = "Dry-Run wird vorbereitet …";
        ProgressDetail = string.Empty;
        ProgressPercentage = 0;
        ProgressIsIndeterminate = true;
        StatusMessage = "Der Medienindex wird ausgewertet. Es werden keine Dateien kopiert, verschoben oder gelöscht.";

        using var cancellation = new CancellationTokenSource();
        _cancellationTokenSource = cancellation;

        var progress = new Progress<MediaConsolidationProgress>(UpdateProgress);
        var request = new MediaConsolidationPlanRequest(
            Sources.Select(source => source.Path).ToArray(),
            TargetPath,
            SelectedStructure.Mode);

        try
        {
            // Microsoft.Data.Sqlite does not guarantee that its async reader work
            // leaves the caller context. Keep the complete planner off the UI thread.
            _plan = await Task.Run(
                () => _planner.PlanAsync(request, progress, cancellation.Token),
                cancellation.Token);

            ApplyPlan(_plan);
            StatusMessage = _plan.ConflictCount > 0 || _plan.IssueCount > 0
                ? "Dry-Run abgeschlossen. Konflikte und Hinweise bitte prüfen; 0133 führt bewusst noch keine Kopieraktion aus."
                : "Dry-Run abgeschlossen. Der Plan verändert noch keine Dateien.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Dry-Run abgebrochen. Es wurden keine Dateien verändert.";
            ProgressPhase = "Abgebrochen";
            ProgressDetail = string.Empty;
        }
        catch (Exception exception)
        {
            StatusMessage = "Der Konsolidierungsplan konnte nicht erstellt werden: " + exception.GetBaseException().Message;
            Issues = [exception.GetBaseException().Message];
        }
        finally
        {
            if (ReferenceEquals(_cancellationTokenSource, cancellation))
            {
                _cancellationTokenSource = null;
            }

            IsBusy = false;
            ProgressIsIndeterminate = false;
        }
    }

    private void UpdateProgress(MediaConsolidationProgress progress)
    {
        ProgressPhase = progress.Phase;
        ProgressDetail = progress.Total > 0
            ? $"{progress.Processed:N0} von {progress.Total:N0}" +
              (string.IsNullOrWhiteSpace(progress.CurrentItem) ? string.Empty : $" · {progress.CurrentItem}")
            : progress.CurrentItem ?? string.Empty;
        ProgressPercentage = progress.Percentage;
        ProgressIsIndeterminate = progress.Total <= 0;
    }

    private void ApplyPlan(MediaConsolidationPlan plan)
    {
        SourceLocationCount = plan.IndexedSourceLocationCount;
        UniqueContentCount = plan.UniqueContentCount;
        DuplicateSourceCount = plan.DuplicateSourceLocationCount;
        PlannedCopyCount = plan.CopyCount;
        AlreadyInTargetCount = plan.AlreadyInTargetCount;
        ConflictCount = plan.ConflictCount;
        IssueCount = plan.IssueCount;
        PlannedCopySize = FormatBytes(plan.PlannedCopyBytes);
        Issues = plan.Issues;

        PreviewItems = plan.Items
            .OrderBy(item => StatusOrder(item.Status))
            .ThenBy(item => item.TargetRelativePath, StringComparer.CurrentCultureIgnoreCase)
            .Take(500)
            .Select(item => new MediaConsolidationPlanItemViewModel(item))
            .ToArray();

        HasPlan = true;
    }

    private void ClearPlan()
    {
        ResetPlanResult();
        StatusMessage = "Plan verworfen. Quellen und Ziel bleiben ausgewählt.";
    }

    private void ResetPlanResult()
    {
        _plan = null;
        HasPlan = false;
        SourceLocationCount = 0;
        UniqueContentCount = 0;
        DuplicateSourceCount = 0;
        PlannedCopyCount = 0;
        AlreadyInTargetCount = 0;
        ConflictCount = 0;
        IssueCount = 0;
        PlannedCopySize = "0 B";
        PreviewItems = Array.Empty<MediaConsolidationPlanItemViewModel>();
        Issues = Array.Empty<string>();
        ProgressPercentage = 0;
        ProgressPhase = string.Empty;
        ProgressDetail = string.Empty;
    }

    private void Cancel() => _cancellationTokenSource?.Cancel();

    private static int StatusOrder(MediaConsolidationPlanItemStatus status) => status switch
    {
        MediaConsolidationPlanItemStatus.NameConflict => 0,
        MediaConsolidationPlanItemStatus.Issue => 1,
        MediaConsolidationPlanItemStatus.AlreadyInTarget => 2,
        _ => 3
    };

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0
            ? $"{value:N0} {units[unit]}"
            : $"{value:N1} {units[unit]}";
    }
}

public sealed class MediaConsolidationSourceViewModel
{
    private readonly Action<MediaConsolidationSourceViewModel> _remove;

    public MediaConsolidationSourceViewModel(
        string path,
        Action<MediaConsolidationSourceViewModel> remove)
    {
        Path = path;
        _remove = remove;
        RemoveCommand = new RelayCommand(() => _remove(this));
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name
        ? name
        : Path;

    public IRelayCommand RemoveCommand { get; }
}

public sealed record MediaConsolidationStructureOptionViewModel(
    MediaConsolidationStructureMode Mode,
    string Name,
    string Example);

public sealed class MediaConsolidationPlanItemViewModel
{
    public MediaConsolidationPlanItemViewModel(MediaConsolidationPlanItem item)
    {
        SourcePath = item.SourcePath;
        TargetRelativePath = item.TargetRelativePath;
        TargetPath = item.TargetPath;
        Size = FormatBytes(item.Length);
        OccurrenceCount = item.SourceOccurrences.Count;
        Message = item.Message;
        DateText = item.EffectiveMediaDate.ToString("dd.MM.yyyy HH:mm");
        DateSource = item.UsesCaptureDate ? "Aufnahmedatum" : "Dateiänderungszeit";
        Status = item.Status switch
        {
            MediaConsolidationPlanItemStatus.Copy => "KOPIEREN",
            MediaConsolidationPlanItemStatus.AlreadyInTarget => "BEREITS IM ZIEL",
            MediaConsolidationPlanItemStatus.NameConflict => "KONFLIKT",
            _ => "PRÜFEN"
        };
    }

    public string SourcePath { get; }
    public string TargetRelativePath { get; }
    public string TargetPath { get; }
    public string Size { get; }
    public int OccurrenceCount { get; }
    public string OccurrenceText => OccurrenceCount == 1 ? "1 Quelle" : $"{OccurrenceCount:N0} Quellen";
    public string Message { get; }
    public string DateText { get; }
    public string DateSource { get; }
    public string Status { get; }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{value:N1} {units[unit]}";
    }
}
