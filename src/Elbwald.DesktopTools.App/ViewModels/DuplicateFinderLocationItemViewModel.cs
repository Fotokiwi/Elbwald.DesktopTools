using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.DuplicateFinding;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class DuplicateFinderLocationItemViewModel : ObservableObject
{
    private readonly Action<DuplicateFinderLocationItemViewModel> _markAsKeeper;
    private readonly Action<DuplicateFinderLocationItemViewModel> _toggleRemoval;
    private readonly Action<DuplicateFinderLocationItemViewModel> _openContainingFolder;

    [ObservableProperty]
    private bool isKeeper;

    [ObservableProperty]
    private bool isRemovalCandidate;

    [ObservableProperty]
    private string preflightStatus = "Noch nicht geprüft";

    [ObservableProperty]
    private string preflightMessage = "Dieser Fundort wurde noch nicht gegen den aktuellen Dateisystemzustand geprüft.";

    [ObservableProperty]
    private bool hasPreflightResult;

    [ObservableProperty]
    private bool preflightIsReady;

    public DuplicateFinderLocationItemViewModel(
        MediaIndexLocation location,
        IReadOnlyDictionary<string, string> endpointNames,
        IReadOnlyDictionary<string, string> endpointRoots,
        Action<DuplicateFinderLocationItemViewModel> markAsKeeper,
        Action<DuplicateFinderLocationItemViewModel> toggleRemoval,
        Action<DuplicateFinderLocationItemViewModel> openContainingFolder)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(endpointNames);
        ArgumentNullException.ThrowIfNull(endpointRoots);
        ArgumentNullException.ThrowIfNull(markAsKeeper);
        ArgumentNullException.ThrowIfNull(toggleRemoval);
        ArgumentNullException.ThrowIfNull(openContainingFolder);

        _markAsKeeper = markAsKeeper;
        _toggleRemoval = toggleRemoval;
        _openContainingFolder = openContainingFolder;

        LocationId = location.Id;
        EndpointName = endpointNames.TryGetValue(location.EndpointId, out var endpointName)
            ? endpointName
            : location.EndpointId;
        EndpointId = location.EndpointId;
        EndpointKind = location.EndpointKind;
        RelativePath = location.RelativePath;
        VolumeId = location.VolumeId;
        LastWriteTimeUtc = location.LastWriteTimeUtc;
        LastSeenUtc = location.LastSeenUtc;
        LastWriteText = location.LastWriteTimeUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        LastSeen = location.LastSeenUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        SizeText = MediaIndexBrowseItemViewModel.FormatBytes(location.Length);
        AbsolutePath = TryBuildAbsolutePath(endpointRoots, location.EndpointId, location.RelativePath);

        MarkAsKeeperCommand = new RelayCommand(
            () => _markAsKeeper(this),
            () => CanMarkAsKeeper);

        ToggleRemovalCommand = new RelayCommand(
            () => _toggleRemoval(this),
            () => CanToggleRemoval);

        OpenContainingFolderCommand = new RelayCommand(
            () => _openContainingFolder(this),
            () => CanOpenContainingFolder);
    }

    public long LocationId { get; }

    public string EndpointName { get; }

    public string EndpointId { get; }

    public StorageEndpointKind EndpointKind { get; }

    public string RelativePath { get; }

    public string VolumeId { get; }

    public DateTimeOffset LastWriteTimeUtc { get; }

    public DateTimeOffset LastSeenUtc { get; }

    public string LastWriteText { get; }

    public string LastSeen { get; }

    public string SizeText { get; }

    public string? AbsolutePath { get; }

    public IRelayCommand MarkAsKeeperCommand { get; }

    public IRelayCommand ToggleRemovalCommand { get; }

    public IRelayCommand OpenContainingFolderCommand { get; }

    public bool IsProtectedEndpoint =>
        EndpointKind is StorageEndpointKind.Backup or StorageEndpointKind.Archive;

    public bool CanOpenContainingFolder => !string.IsNullOrWhiteSpace(AbsolutePath);

    public string PlanRole =>
        IsKeeper
            ? "BEHALTEN"
            : IsProtectedEndpoint
                ? "GESCHÜTZT"
                : IsRemovalCandidate
                    ? "ENTFERNEN"
                    : "NICHT ANFASSEN";

    public string RemovalButtonText =>
        IsProtectedEndpoint
            ? "Geschützt"
            : IsRemovalCandidate
                ? "Nicht entfernen"
                : "Entfernen";

    public bool CanMarkAsKeeper => !IsKeeper && !IsProtectedEndpoint;

    public bool CanToggleRemoval => !IsKeeper && !IsProtectedEndpoint;

    partial void OnIsKeeperChanged(bool value)
    {
        NotifyPlanPresentationChanged();
    }

    partial void OnIsRemovalCandidateChanged(bool value)
    {
        NotifyPlanPresentationChanged();
    }

    internal void SetPlanState(bool keeper, bool removalCandidate)
    {
        IsKeeper = keeper;
        IsRemovalCandidate = !keeper && !IsProtectedEndpoint && removalCandidate;
    }

    internal void ResetPreflight()
    {
        HasPreflightResult = false;
        PreflightIsReady = false;
        PreflightStatus = "Noch nicht geprüft";
        PreflightMessage = IsKeeper || IsRemovalCandidate
            ? "Der aktuelle Bereinigungsplan wurde noch nicht gegen das Dateisystem geprüft."
            : IsProtectedEndpoint
                ? "Backup- und Archiv-Endpunkte sind für generische Bereinigungsaktionen geschützt."
                : "Dieser Fundort ist nicht Teil des aktuellen Bereinigungsplans.";
    }

    internal void ApplyPreflight(DuplicateCleanupPreflightItem result)
    {
        ArgumentNullException.ThrowIfNull(result);

        HasPreflightResult = true;
        PreflightIsReady = result.IsReady;
        PreflightStatus = result.IsReady
            ? "Preflight OK"
            : GetStatusLabel(result.Status);
        PreflightMessage = result.Message;
    }

    private void NotifyPlanPresentationChanged()
    {
        OnPropertyChanged(nameof(PlanRole));
        OnPropertyChanged(nameof(RemovalButtonText));
        OnPropertyChanged(nameof(CanMarkAsKeeper));
        OnPropertyChanged(nameof(CanToggleRemoval));
        MarkAsKeeperCommand.NotifyCanExecuteChanged();
        ToggleRemovalCommand.NotifyCanExecuteChanged();
    }

    private static string? TryBuildAbsolutePath(
        IReadOnlyDictionary<string, string> endpointRoots,
        string endpointId,
        string relativePath)
    {
        if (!endpointRoots.TryGetValue(endpointId, out var root)
            || string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(root);
            var combined = string.IsNullOrWhiteSpace(relativePath)
                ? normalizedRoot
                : Path.GetFullPath(
                    Path.Combine(
                        normalizedRoot,
                        relativePath.Replace('/', Path.DirectorySeparatorChar)));

            var relativeToRoot = Path.GetRelativePath(normalizedRoot, combined);
            if (relativeToRoot == ".."
                || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relativeToRoot))
            {
                return null;
            }

            return combined;
        }
        catch
        {
            return null;
        }
    }

    private static string GetStatusLabel(DuplicateCleanupPreflightStatus status) =>
        status switch
        {
            DuplicateCleanupPreflightStatus.EndpointMissing => "Endpunkt fehlt",
            DuplicateCleanupPreflightStatus.EndpointUnavailable => "Speicher offline",
            DuplicateCleanupPreflightStatus.EndpointDisabled => "Endpunkt deaktiviert",
            DuplicateCleanupPreflightStatus.ProtectedEndpoint => "Endpunkt geschützt",
            DuplicateCleanupPreflightStatus.VolumeChanged => "Laufwerk geändert",
            DuplicateCleanupPreflightStatus.InvalidRelativePath => "Pfad ungültig",
            DuplicateCleanupPreflightStatus.FileMissing => "Datei fehlt",
            DuplicateCleanupPreflightStatus.SymbolicLinkUnsupported => "Link nicht unterstützt",
            DuplicateCleanupPreflightStatus.LengthMismatch => "Größe geändert",
            DuplicateCleanupPreflightStatus.HashMismatch => "Inhalt geändert",
            DuplicateCleanupPreflightStatus.ReadOnly => "Schreibgeschützt",
            DuplicateCleanupPreflightStatus.AccessDenied => "Zugriff verweigert",
            DuplicateCleanupPreflightStatus.IoError => "Dateizugriff fehlgeschlagen",
            DuplicateCleanupPreflightStatus.IndexChanged => "Index geändert",
            _ => "Prüfung fehlgeschlagen"
        };
}
