using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.DuplicateFinding;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class DuplicateFinderGroupItemViewModel : ObservableObject
{
    private long _planVersion;

    [ObservableProperty]
    private string preflightSummary = "Plan noch nicht geprüft.";

    [ObservableProperty]
    private string preflightDetail = "Vor einer späteren Bereinigung werden Referenz und Entfernungskandidaten erneut direkt auf dem Datenträger geprüft.";

    [ObservableProperty]
    private bool hasPreflightResult;

    [ObservableProperty]
    private bool preflightIsReady;

    public DuplicateFinderGroupItemViewModel(
        ExactDuplicateGroup group,
        IReadOnlyDictionary<string, string> endpointNames,
        IReadOnlyDictionary<string, string> endpointRoots,
        Action<DuplicateFinderLocationItemViewModel> openContainingFolder)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(endpointNames);
        ArgumentNullException.ThrowIfNull(endpointRoots);
        ArgumentNullException.ThrowIfNull(openContainingFolder);

        MediaItemId = group.MediaItemId;
        Sha256 = group.Sha256;
        Length = group.Length;
        HashShort = group.Sha256.Length > 16
            ? group.Sha256[..16] + "…"
            : group.Sha256;
        MediaType = MediaIndexBrowseItemViewModel.GetMediaTypeLabel(group.MediaType);
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(group.Length);
        CopyCount = group.CopyCount;
        RedundantCopyCount = group.RedundantCopyCount;
        ReclaimableBytes = group.ReclaimableBytes;
        ReclaimableText = MediaIndexBrowseItemViewModel.FormatBytes(group.ReclaimableBytes);

        Locations = group.Locations
            .Select(location => new DuplicateFinderLocationItemViewModel(
                location,
                endpointNames,
                endpointRoots,
                location => SetKeeper(location),
                ToggleRemoval,
                openContainingFolder))
            .ToArray();

        var confidence = DuplicateFinderConfidenceAnalyzer.Analyze(Locations);
        ConfidenceLevel = confidence.Level;
        SuggestedKeeperLocationId = confidence.SuggestedKeeperLocationId;
        ConfidenceReason = confidence.Reason;

        Title = Locations
            .Select(location => Path.GetFileName(location.RelativePath))
            .FirstOrDefault(fileName => !string.IsNullOrWhiteSpace(fileName))
            ?? group.MediaItemId;

        LocationSummary = CopyCount == 2
            ? "2 aktuelle Fundorte"
            : $"{CopyCount:N0} aktuelle Fundorte";

        InitializePlan();
    }

    public event EventHandler? PlanChanged;

    public string MediaItemId { get; }

    public string Sha256 { get; }

    public long Length { get; }

    public string HashShort { get; }

    public string Title { get; }

    public string MediaType { get; }

    public string SizeText { get; }

    public int CopyCount { get; }

    public int RedundantCopyCount { get; }

    public long ReclaimableBytes { get; }

    public string ReclaimableText { get; }

    public string LocationSummary { get; }

    public DuplicateFinderConfidenceLevel ConfidenceLevel { get; }

    public long? SuggestedKeeperLocationId { get; }

    public string ConfidenceReason { get; }

    public string ConfidenceLabel => ConfidenceLevel switch
    {
        DuplicateFinderConfidenceLevel.VerySafe => "SEHR SICHER",
        DuplicateFinderConfidenceLevel.Likely => "WAHRSCHEINLICH",
        _ => "UNKLAR"
    };

    public bool IsVerySafe => ConfidenceLevel == DuplicateFinderConfidenceLevel.VerySafe;

    public string SuggestedKeeperText
    {
        get
        {
            if (SuggestedKeeperLocationId is null)
            {
                return "Keine automatische Referenz vorgeschlagen.";
            }

            var location = Locations.FirstOrDefault(item => item.LocationId == SuggestedKeeperLocationId.Value);
            return location is null
                ? "Keine automatische Referenz vorgeschlagen."
                : $"Vorgeschlagene Referenz: {location.EndpointName} · {location.RelativePath}";
        }
    }

    public int ConfidenceSortRank => (int)ConfidenceLevel;

    public IReadOnlyList<DuplicateFinderLocationItemViewModel> Locations { get; }

    public long PlanVersion => _planVersion;

    public DuplicateFinderLocationItemViewModel? Keeper =>
        Locations.FirstOrDefault(location => location.IsKeeper);

    public int PlannedRemovalCount =>
        Locations.Count(location => location.IsRemovalCandidate);

    public long PlannedReclaimableBytes =>
        Length <= 0 || PlannedRemovalCount == 0
            ? 0
            : Length * PlannedRemovalCount;

    public string PlannedReclaimableText =>
        MediaIndexBrowseItemViewModel.FormatBytes(PlannedReclaimableBytes);

    public string CleanupPlanSummary =>
        Keeper is null
            ? "Nur geschützte Backup-/Archiv-Fundorte · keine generische Bereinigung"
            : PlannedRemovalCount == 0
                ? "Referenz festgelegt · keine Datei zur Entfernung vorgesehen"
                : PlannedRemovalCount == 1
                    ? $"1 Entfernungskandidat · {PlannedReclaimableText} geplant"
                    : $"{PlannedRemovalCount:N0} Entfernungskandidaten · {PlannedReclaimableText} geplant";

    public bool CanRunPreflight => Keeper is not null && PlannedRemovalCount > 0;

    public bool TryKeepOldestModified()
    {
        var candidate = Locations
            .Where(location => !location.IsProtectedEndpoint)
            .OrderBy(location => location.LastWriteTimeUtc)
            .ThenBy(location => location.EndpointName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(location => location.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        return candidate is not null && SetKeeper(candidate);
    }

    public bool TryKeepNewestModified()
    {
        var candidate = Locations
            .Where(location => !location.IsProtectedEndpoint)
            .OrderByDescending(location => location.LastWriteTimeUtc)
            .ThenBy(location => location.EndpointName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(location => location.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        return candidate is not null && SetKeeper(candidate);
    }

    public bool ApplyVerySafeSuggestion()
    {
        if (!IsVerySafe || SuggestedKeeperLocationId is null)
        {
            return false;
        }

        var suggestedKeeper = Locations.FirstOrDefault(
            location => location.LocationId == SuggestedKeeperLocationId.Value
                && !location.IsProtectedEndpoint);

        if (suggestedKeeper is null)
        {
            return false;
        }

        var changed = false;
        foreach (var location in Locations)
        {
            var shouldKeep = ReferenceEquals(location, suggestedKeeper);
            var shouldRemove = !shouldKeep && !location.IsProtectedEndpoint;
            changed |= location.IsKeeper != shouldKeep
                || location.IsRemovalCandidate != shouldRemove;
            location.SetPlanState(shouldKeep, shouldRemove);
        }

        if (changed)
        {
            NotifyPlanChanged();
        }

        return changed;
    }

    public bool TryKeepEndpoint(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return false;
        }

        var candidate = Locations
            .Where(location => !location.IsProtectedEndpoint)
            .Where(location => string.Equals(
                location.EndpointId,
                endpointId,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(location => location.RelativePath, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();

        return candidate is not null && SetKeeper(candidate);
    }

    public bool ResetPlanToDefault()
    {
        var keeper = Locations.FirstOrDefault(location => !location.IsProtectedEndpoint);
        var changed = false;

        foreach (var location in Locations)
        {
            var shouldKeep = ReferenceEquals(location, keeper);
            var shouldRemove = !shouldKeep && !location.IsProtectedEndpoint;
            changed |= location.IsKeeper != shouldKeep
                || location.IsRemovalCandidate != shouldRemove;
            location.SetPlanState(shouldKeep, shouldRemove);
        }

        if (changed)
        {
            NotifyPlanChanged();
        }

        return changed;
    }

    public DuplicateCleanupPlan CreateCleanupPlan()
    {
        var keeper = Keeper
            ?? throw new InvalidOperationException("Für diese Dublettengruppe wurde keine Referenzdatei ausgewählt.");

        var removalIds = Locations
            .Where(location => location.IsRemovalCandidate)
            .Select(location => location.LocationId)
            .ToArray();

        if (removalIds.Length == 0)
        {
            throw new InvalidOperationException("Für diese Dublettengruppe wurde kein Bereinigungskandidat ausgewählt.");
        }

        return new DuplicateCleanupPlan(
            MediaItemId,
            Sha256,
            Length,
            keeper.LocationId,
            removalIds);
    }

    public void SetPreflightRunning()
    {
        HasPreflightResult = false;
        PreflightIsReady = false;
        PreflightSummary = "Preflight läuft …";
        PreflightDetail = "Dateien werden gegen Pfad, Größe, SHA-256 und Zugriffsstatus geprüft.";

        foreach (var location in Locations)
        {
            if (location.IsKeeper || location.IsRemovalCandidate)
            {
                location.ResetPreflight();
            }
        }
    }

    public void ResetPreflightAfterError(string message)
    {
        HasPreflightResult = false;
        PreflightIsReady = false;
        PreflightSummary = "Preflight fehlgeschlagen";
        PreflightDetail = string.IsNullOrWhiteSpace(message)
            ? "Die Sicherheitsprüfung konnte nicht abgeschlossen werden."
            : message;

        foreach (var location in Locations)
        {
            location.ResetPreflight();
        }
    }

    public void InvalidatePreflightAfterExecution(string detail)
    {
        HasPreflightResult = false;
        PreflightIsReady = false;
        PreflightSummary = "Preflight erneut erforderlich";
        PreflightDetail = string.IsNullOrWhiteSpace(detail)
            ? "Nach einer Ausführung oder einem Ausführungsversuch muss der aktuelle Dateisystemzustand erneut geprüft werden."
            : detail;

        foreach (var location in Locations)
        {
            location.ResetPreflight();
        }
    }

    public void ApplyPreflight(DuplicateCleanupPreflightReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var byLocationId = report.Items.ToDictionary(item => item.LocationId);

        foreach (var location in Locations)
        {
            if (byLocationId.TryGetValue(location.LocationId, out var result))
            {
                location.ApplyPreflight(result);
            }
            else
            {
                location.ResetPreflight();
            }
        }

        HasPreflightResult = true;
        PreflightIsReady = report.IsReady;

        if (report.IsReady)
        {
            PreflightSummary = $"Preflight bestanden · {report.ReadyRemovalCount:N0} Kandidat(en) · {PlannedReclaimableText}";
            PreflightDetail = "Referenz und alle geplanten Entfernungskandidaten stimmen aktuell mit der indexierten SHA-256-Identität überein.";
            return;
        }

        var failedItems = report.Items.Count(item => !item.IsReady);
        PreflightSummary = $"Preflight blockiert · {failedItems:N0} Fundort(e) mit Problem";
        PreflightDetail = report.GlobalIssues.FirstOrDefault()
            ?? report.Items.FirstOrDefault(item => !item.IsReady)?.Message
            ?? "Der Bereinigungsplan konnte nicht vollständig bestätigt werden.";
    }

    public bool MatchesSearch(string? searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var value = searchText.Trim();

        return Title.Contains(value, StringComparison.CurrentCultureIgnoreCase)
            || MediaType.Contains(value, StringComparison.CurrentCultureIgnoreCase)
            || Sha256.Contains(value, StringComparison.OrdinalIgnoreCase)
            || MediaItemId.Contains(value, StringComparison.OrdinalIgnoreCase)
            || Locations.Any(location =>
                location.EndpointName.Contains(value, StringComparison.CurrentCultureIgnoreCase)
                || location.EndpointId.Contains(value, StringComparison.OrdinalIgnoreCase)
                || location.RelativePath.Contains(value, StringComparison.CurrentCultureIgnoreCase));
    }

    private void InitializePlan()
    {
        var keeper = Locations.FirstOrDefault(location => !location.IsProtectedEndpoint);

        foreach (var location in Locations)
        {
            var isKeeper = ReferenceEquals(location, keeper);
            location.SetPlanState(
                keeper: isKeeper,
                removalCandidate: !isKeeper && !location.IsProtectedEndpoint);
            location.ResetPreflight();
        }

        NotifyPlanChanged(incrementVersion: false);
    }

    private bool SetKeeper(DuplicateFinderLocationItemViewModel selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.IsKeeper
            || selected.IsProtectedEndpoint
            || !Locations.Contains(selected))
        {
            return false;
        }

        foreach (var location in Locations)
        {
            if (ReferenceEquals(location, selected))
            {
                location.SetPlanState(keeper: true, removalCandidate: false);
            }
            else if (location.IsKeeper)
            {
                location.SetPlanState(
                    keeper: false,
                    removalCandidate: !location.IsProtectedEndpoint);
            }
        }

        NotifyPlanChanged();
        return true;
    }

    private void ToggleRemoval(DuplicateFinderLocationItemViewModel selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        if (selected.IsKeeper
            || selected.IsProtectedEndpoint
            || !Locations.Contains(selected))
        {
            return;
        }

        selected.SetPlanState(
            keeper: false,
            removalCandidate: !selected.IsRemovalCandidate);

        NotifyPlanChanged();
    }

    private void NotifyPlanChanged(bool incrementVersion = true)
    {
        if (incrementVersion)
        {
            _planVersion++;
        }

        HasPreflightResult = false;
        PreflightIsReady = false;
        PreflightSummary = "Plan noch nicht geprüft.";
        PreflightDetail = "Vor einer späteren Bereinigung werden Referenz und Entfernungskandidaten erneut direkt auf dem Datenträger geprüft.";

        foreach (var location in Locations)
        {
            location.ResetPreflight();
        }

        OnPropertyChanged(nameof(Keeper));
        OnPropertyChanged(nameof(PlannedRemovalCount));
        OnPropertyChanged(nameof(PlannedReclaimableBytes));
        OnPropertyChanged(nameof(PlannedReclaimableText));
        OnPropertyChanged(nameof(CleanupPlanSummary));
        OnPropertyChanged(nameof(CanRunPreflight));

        PlanChanged?.Invoke(this, EventArgs.Empty);
    }
}
