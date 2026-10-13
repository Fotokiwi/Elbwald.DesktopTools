using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.App.Services;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly MediaThumbnailOptions _thumbnailOptions;
    private IReadOnlyDictionary<string, string> _endpointRoots =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _projectThumbnailCancellation;
    private CancellationTokenSource? _projectCoverCancellation;
    private int _projectMediaLoadVersion;
    private int _projectCoverLoadVersion;
    private int _projectJournalLoadVersion;
    private int _journalEditorLoadVersion;
    private bool _journalDeleteArmed;

    [ObservableProperty] private IReadOnlyList<ProjectListItemViewModel> projects = Array.Empty<ProjectListItemViewModel>();
    [ObservableProperty] private ProjectListItemViewModel? selectedProject;
    [ObservableProperty] private IReadOnlyList<ProjectMediaItemViewModel> projectMedia = Array.Empty<ProjectMediaItemViewModel>();
    [ObservableProperty] private IReadOnlyList<ProjectMediaItemViewModel> highlightedMedia = Array.Empty<ProjectMediaItemViewModel>();
    [ObservableProperty] private ProjectMediaItemViewModel? selectedProjectMedia;
    [ObservableProperty] private string newProjectName = string.Empty;
    [ObservableProperty] private string newProjectKind = "Sammlung";
    [ObservableProperty] private string editName = string.Empty;
    [ObservableProperty] private string editKind = "Sammlung";
    [ObservableProperty] private string editLocation = string.Empty;
    [ObservableProperty] private string editNotes = string.Empty;
    [ObservableProperty] private string editStartDateText = string.Empty;
    [ObservableProperty] private string editEndDateText = string.Empty;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ProjectSearchResultViewModel> searchResults = Array.Empty<ProjectSearchResultViewModel>();
    [ObservableProperty] private ProjectSearchResultViewModel? selectedSearchResult;
    [ObservableProperty] private IReadOnlyList<ProjectSearchResultViewModel> suggestedMedia = Array.Empty<ProjectSearchResultViewModel>();
    [ObservableProperty] private string findMediaStatus = "Projektzeitraum speichern und anschließend passende Medien finden.";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Projekte werden geladen …";
    [ObservableProperty] private bool hasProjectMedia;
    [ObservableProperty] private bool hasNoProjectMedia = true;
    [ObservableProperty] private IReadOnlyList<ProjectJournalEntryItemViewModel> journalEntries = Array.Empty<ProjectJournalEntryItemViewModel>();
    [ObservableProperty] private ProjectJournalEntryItemViewModel? selectedJournalEntry;
    [ObservableProperty] private IReadOnlyList<ProjectJournalMediaOptionViewModel> journalMediaOptions = Array.Empty<ProjectJournalMediaOptionViewModel>();
    [ObservableProperty] private string journalDateText = string.Empty;
    [ObservableProperty] private string journalTitle = string.Empty;
    [ObservableProperty] private string journalBody = string.Empty;
    [ObservableProperty] private string journalLocation = string.Empty;
    [ObservableProperty] private string journalStatus = "Noch keine Tagebucheinträge.";

    public ProjectsViewModel(
        IProjectService projectService,
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IMediaThumbnailService thumbnailService,
        MediaThumbnailOptions thumbnailOptions)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _mediaIndexService = mediaIndexService ?? throw new ArgumentNullException(nameof(mediaIndexService));
        _storageSettingsStore = storageSettingsStore ?? throw new ArgumentNullException(nameof(storageSettingsStore));
        _storageLocationResolver = storageLocationResolver ?? throw new ArgumentNullException(nameof(storageLocationResolver));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _thumbnailOptions = thumbnailOptions ?? throw new ArgumentNullException(nameof(thumbnailOptions));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync, CanCreateProject);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, CanSaveProject);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && SelectedProject is not null);
        AddSelectedMediaCommand = new AsyncRelayCommand(AddSelectedMediaAsync, () => !IsBusy && SelectedProject is not null && SelectedSearchResult is not null);
        RemoveMediaCommand = new AsyncRelayCommand(RemoveSelectedMediaAsync, () => !IsBusy && SelectedProject is not null && SelectedProjectMedia is not null);
        SetCoverCommand = new AsyncRelayCommand(SetSelectedMediaAsCoverAsync, CanSetCover);
        ClearCoverCommand = new AsyncRelayCommand(ClearCoverAsync, CanClearCover);
        ToggleHighlightCommand = new AsyncRelayCommand(ToggleSelectedHighlightAsync, () => !IsBusy && SelectedProject is not null && SelectedProjectMedia is not null);
        FindMediaCommand = new AsyncRelayCommand(FindMediaAsync, () => !IsBusy && SelectedProject is not null);
        AddSuggestedMediaCommand = new AsyncRelayCommand(AddSuggestedMediaAsync, () => !IsBusy && SelectedProject is not null && SuggestedMedia.Any(item => item.IsSelected));
        NewJournalEntryCommand = new RelayCommand(StartNewJournalEntry, () => !IsBusy && SelectedProject is not null);
        SaveJournalEntryCommand = new AsyncRelayCommand(SaveJournalEntryAsync, CanSaveJournalEntry);
        DeleteJournalEntryCommand = new AsyncRelayCommand(DeleteJournalEntryAsync, () => !IsBusy && SelectedProject is not null && SelectedJournalEntry is not null);
        BackToProjectsCommand = new RelayCommand(() => SelectedProject = null);

        _ = RefreshAsync();
    }

    public IReadOnlyList<string> ProjectKinds { get; } = new[] { "Sammlung", "Urlaub", "Hochzeit", "Geburtstag", "Fotobuch", "Kalender", "Sonstiges" };

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreateProjectCommand { get; }
    public IAsyncRelayCommand SaveProjectCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand AddSelectedMediaCommand { get; }
    public IAsyncRelayCommand RemoveMediaCommand { get; }
    public IAsyncRelayCommand SetCoverCommand { get; }
    public IAsyncRelayCommand ClearCoverCommand { get; }
    public IAsyncRelayCommand ToggleHighlightCommand { get; }
    public IAsyncRelayCommand FindMediaCommand { get; }
    public IAsyncRelayCommand AddSuggestedMediaCommand { get; }
    public IRelayCommand NewJournalEntryCommand { get; }
    public IAsyncRelayCommand SaveJournalEntryCommand { get; }
    public IAsyncRelayCommand DeleteJournalEntryCommand { get; }
    public IRelayCommand BackToProjectsCommand { get; }

    public bool HasProjects => Projects.Count > 0;
    public bool HasSelectedProject => SelectedProject is not null;
    public bool HasNoSelectedProject => SelectedProject is null;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasSuggestedMedia => SuggestedMedia.Count > 0;
    public bool HasHighlights => HighlightedMedia.Count > 0;
    public bool HasNoHighlights => HighlightedMedia.Count == 0;
    public bool SelectedProjectHasCover => SelectedProject?.HasConfiguredCover == true;
    public string SelectedProjectMediaCountText => SelectedProject?.MediaCountText ?? "0 Medien";
    public bool HasJournalEntries => JournalEntries.Count > 0;
    public bool HasNoJournalEntries => JournalEntries.Count == 0;
    public bool IsEditingExistingJournalEntry => SelectedJournalEntry is not null;
    public string JournalEditorHeading => IsEditingExistingJournalEntry ? "Tagebucheintrag bearbeiten" : "Neuer Tagebucheintrag";
    public string JournalDeleteButtonText => _journalDeleteArmed ? "Löschen bestätigen" : "Eintrag löschen";

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnNewProjectNameChanged(string value) => CreateProjectCommand.NotifyCanExecuteChanged();
    partial void OnEditNameChanged(string value) => SaveProjectCommand.NotifyCanExecuteChanged();
    partial void OnProjectsChanged(IReadOnlyList<ProjectListItemViewModel> value) => OnPropertyChanged(nameof(HasProjects));
    partial void OnSearchResultsChanged(IReadOnlyList<ProjectSearchResultViewModel> value) => OnPropertyChanged(nameof(HasSearchResults));
    partial void OnHighlightedMediaChanged(IReadOnlyList<ProjectMediaItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasHighlights));
        OnPropertyChanged(nameof(HasNoHighlights));
    }
    partial void OnJournalEntriesChanged(IReadOnlyList<ProjectJournalEntryItemViewModel> value)
    {
        OnPropertyChanged(nameof(HasJournalEntries));
        OnPropertyChanged(nameof(HasNoJournalEntries));
    }
    partial void OnJournalTitleChanged(string value) => SaveJournalEntryCommand.NotifyCanExecuteChanged();
    partial void OnJournalDateTextChanged(string value) => SaveJournalEntryCommand.NotifyCanExecuteChanged();
    partial void OnSelectedJournalEntryChanged(ProjectJournalEntryItemViewModel? value)
    {
        ResetJournalDeleteArm();
        OnPropertyChanged(nameof(IsEditingExistingJournalEntry));
        OnPropertyChanged(nameof(JournalEditorHeading));
        SaveJournalEntryCommand.NotifyCanExecuteChanged();
        DeleteJournalEntryCommand.NotifyCanExecuteChanged();

        if (value is null || SelectedProject is null)
        {
            return;
        }

        JournalDateText = value.Entry.EntryDate.ToString("dd.MM.yyyy");
        JournalTitle = value.Entry.Title;
        JournalBody = value.Entry.Body ?? string.Empty;
        JournalLocation = value.Entry.Location ?? string.Empty;
        var editorVersion = ++_journalEditorLoadVersion;
        _ = LoadJournalMediaOptionsAsync(
            SelectedProject.Id,
            value.Id,
            editorVersion);
    }
    partial void OnSuggestedMediaChanged(IReadOnlyList<ProjectSearchResultViewModel> value)
    {
        OnPropertyChanged(nameof(HasSuggestedMedia));
        foreach (var item in value)
        {
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ProjectSearchResultViewModel.IsSelected))
                    AddSuggestedMediaCommand.NotifyCanExecuteChanged();
            };
        }
        AddSuggestedMediaCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedSearchResultChanged(ProjectSearchResultViewModel? value) => AddSelectedMediaCommand.NotifyCanExecuteChanged();
    partial void OnSelectedProjectMediaChanged(ProjectMediaItemViewModel? value)
    {
        RemoveMediaCommand.NotifyCanExecuteChanged();
        SetCoverCommand.NotifyCanExecuteChanged();
        ToggleHighlightCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedProjectChanged(ProjectListItemViewModel? value)
    {
        CancelProjectThumbnailLoad();
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(HasNoSelectedProject));
        OnPropertyChanged(nameof(SelectedProjectMediaCountText));
        OnPropertyChanged(nameof(SelectedProjectHasCover));
        SearchResults = Array.Empty<ProjectSearchResultViewModel>();
        SelectedSearchResult = null;
        var loadVersion = ++_projectMediaLoadVersion;
        var journalLoadVersion = ++_projectJournalLoadVersion;
        ++_journalEditorLoadVersion;
        JournalEntries = Array.Empty<ProjectJournalEntryItemViewModel>();
        SelectedJournalEntry = null;
        JournalMediaOptions = Array.Empty<ProjectJournalMediaOptionViewModel>();
        ResetJournalDeleteArm();

        if (value is null)
        {
            EditName = string.Empty;
            EditKind = "Sammlung";
            EditLocation = string.Empty;
            EditNotes = string.Empty;
            EditStartDateText = string.Empty;
            EditEndDateText = string.Empty;
            SuggestedMedia = Array.Empty<ProjectSearchResultViewModel>();
            FindMediaStatus = "Speichere zuerst einen Zeitraum. Danach kann Elbwald passende Medien aus deiner Bibliothek vorschlagen.";
            JournalDateText = string.Empty;
            JournalTitle = string.Empty;
            JournalBody = string.Empty;
            JournalLocation = string.Empty;
            JournalStatus = "Wähle ein Projekt aus, um das Tagebuch zu öffnen.";
            SetProjectMedia(Array.Empty<ProjectMediaItemViewModel>());
        }
        else
        {
            EditName = value.Project.Name;
            EditKind = value.Project.Kind;
            EditLocation = value.Project.Location ?? string.Empty;
            EditNotes = value.Project.Notes ?? string.Empty;
            EditStartDateText = value.Project.StartDate?.ToString("dd.MM.yyyy") ?? string.Empty;
            EditEndDateText = value.Project.EndDate?.ToString("dd.MM.yyyy") ?? string.Empty;
            SuggestedMedia = Array.Empty<ProjectSearchResultViewModel>();
            FindMediaStatus = value.Project.StartDate.HasValue && value.Project.EndDate.HasValue
                ? $"Zeitraum {value.Project.StartDate:dd.MM.yyyy} – {value.Project.EndDate:dd.MM.yyyy}. Bereit für die Suche."
                : "Start- und Enddatum eintragen und speichern, damit Elbwald passende Medien vorschlagen kann.";
            SetProjectMedia(Array.Empty<ProjectMediaItemViewModel>());
            JournalStatus = "Tagebucheinträge werden geladen …";
            _ = LoadProjectMediaForSelectionAsync(value.Id, loadVersion);
            _ = LoadJournalForSelectionAsync(value.Id, journalLoadVersion);
        }
        NotifyCommands();
    }

    private bool CanCreateProject() => !IsBusy && !string.IsNullOrWhiteSpace(NewProjectName);
    private bool CanSaveProject() => !IsBusy && SelectedProject is not null && !string.IsNullOrWhiteSpace(EditName);
    private bool CanSetCover() =>
        !IsBusy
        && SelectedProject is not null
        && SelectedProjectMedia is { IsImage: true };
    private bool CanClearCover() =>
        !IsBusy
        && SelectedProject?.HasConfiguredCover == true;
    private bool CanSaveJournalEntry() =>
        !IsBusy
        && SelectedProject is not null
        && !string.IsNullOrWhiteSpace(JournalDateText)
        && !string.IsNullOrWhiteSpace(JournalTitle);

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var selectedId = SelectedProject?.Id;
            var records = await _projectService.GetProjectsAsync();
            var items = new List<ProjectListItemViewModel>(records.Count);
            foreach (var record in records)
            {
                var refs = await _projectService.GetMediaReferencesAsync(record.Id);
                items.Add(new ProjectListItemViewModel(record, refs.Count));
            }
            SetProjects(items);
            SelectedProject = selectedId is null
                ? null
                : Projects.FirstOrDefault(p => p.Id == selectedId);
            StartProjectCoverThumbnailLoad(Projects);

            if (SelectedProject is null)
            {
                SetProjectMedia(Array.Empty<ProjectMediaItemViewModel>());
            }

            StatusMessage = Projects.Count == 0
                ? "Noch keine Projekte angelegt."
                : Projects.Count == 1
                    ? "1 Projekt vorhanden."
                    : $"{Projects.Count:N0} Projekte vorhanden.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Projekte konnten nicht geladen werden: " + exception.GetBaseException().Message;
        }
        finally { IsBusy = false; }
    }

    private async Task CreateProjectAsync()
    {
        if (!CanCreateProject()) return;
        IsBusy = true;
        try
        {
            var created = await _projectService.CreateProjectAsync(new ProjectCreateRequest(NewProjectName, NewProjectKind));
            NewProjectName = string.Empty;
            await ReloadProjectsAndSelectAsync(created.Id);
            StatusMessage = $"Projekt „{created.Name}“ angelegt.";
        }
        catch (Exception exception) { StatusMessage = "Projekt konnte nicht angelegt werden: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
    }

    private async Task SaveProjectAsync()
    {
        if (!CanSaveProject() || SelectedProject is null) return;
        IsBusy = true;
        try
        {
            if (!TryParseOptionalDate(EditStartDateText, out var startDate)
                || !TryParseOptionalDate(EditEndDateText, out var endDate))
            {
                StatusMessage = "Bitte ein gültiges Datum eingeben, z. B. 01.07.2026.";
                return;
            }
            if (startDate.HasValue && endDate.HasValue && endDate.Value < startDate.Value)
            {
                StatusMessage = "Das Projekt-Enddatum darf nicht vor dem Startdatum liegen.";
                return;
            }

            var updated = await _projectService.UpdateProjectAsync(
                SelectedProject.Id,
                new ProjectUpdateRequest(EditName, EditKind, startDate, endDate, EditLocation, EditNotes));
            await ReloadProjectsAndSelectAsync(updated.Id);
            StatusMessage = "Projekt gespeichert.";
        }
        catch (Exception exception) { StatusMessage = "Projekt konnte nicht gespeichert werden: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
    }

    private async Task SearchAsync()
    {
        if (SelectedProject is null) return;
        IsBusy = true;
        try
        {
            var results = await _mediaIndexService.SearchAsync(new MediaIndexSearchQuery(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                PresentOnly: true,
                Limit: 100));
            var assignedIds = (await _projectService.GetMediaReferencesAsync(SelectedProject.Id)).Select(r => r.MediaItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SearchResults = results.Where(r => !assignedIds.Contains(r.Item.Id)).Select(r => new ProjectSearchResultViewModel(r)).ToArray();
            SelectedSearchResult = SearchResults.FirstOrDefault();
            StatusMessage = SearchResults.Count == 0 ? "Keine noch nicht zugeordneten Treffer gefunden." : $"{SearchResults.Count:N0} Treffer zum Hinzufügen.";
        }
        catch (Exception exception) { StatusMessage = "Indexsuche fehlgeschlagen: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
    }

    private async Task FindMediaAsync()
    {
        if (SelectedProject is null) return;
        if (!TryParseOptionalDate(EditStartDateText, out var startDate)
            || !TryParseOptionalDate(EditEndDateText, out var endDate)
            || !startDate.HasValue
            || !endDate.HasValue)
        {
            FindMediaStatus = "Bitte zuerst Start- und Enddatum eintragen und das Projekt speichern.";
            return;
        }
        if (endDate.Value < startDate.Value)
        {
            FindMediaStatus = "Das Enddatum darf nicht vor dem Startdatum liegen.";
            return;
        }

        IsBusy = true;
        try
        {
            var assignedReferences = await _projectService.GetMediaReferencesAsync(SelectedProject.Id);
            var assignedIds = assignedReferences
                .Select(reference => reference.MediaItemId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var profile = await BuildSuggestionProfileAsync();
            var all = new List<MediaIndexSearchResult>();
            var offset = 0;
            const int pageSize = 500;
            const int maxSuggestions = 5000;
            var from = startDate.Value.ToDateTime(TimeOnly.MinValue);
            var to = endDate.Value.ToDateTime(TimeOnly.MaxValue);

            while (all.Count < maxSuggestions)
            {
                var page = await _mediaIndexService.SearchAsync(new MediaIndexSearchQuery(
                    PresentOnly: true,
                    Limit: pageSize,
                    Offset: offset,
                    SortOrder: MediaIndexSortOrder.CaptureDateAscending,
                    CaptureDateFrom: from,
                    CaptureDateTo: to));

                all.AddRange(page);

                if (page.Count < pageSize)
                {
                    break;
                }

                offset += page.Count;
            }

            var suggestions = all
                .Where(result => !assignedIds.Contains(result.Item.Id))
                .Select(result =>
                {
                    var (score, reasons) = ScoreSuggestion(result, profile);
                    return new ProjectSuggestion(result, score, reasons);
                })
                .OrderByDescending(suggestion => suggestion.Score)
                .ThenBy(suggestion => suggestion.Result.CaptureDate ?? DateTime.MaxValue)
                .ThenBy(suggestion => suggestion.Result.RepresentativeRelativePath, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var useSmartPreselection = profile.HasLearningSignals;
            SuggestedMedia = suggestions
                .Select(suggestion => new ProjectSearchResultViewModel(
                    suggestion.Result,
                    isSelected: !useSmartPreselection || suggestion.Score >= 70,
                    suggestionScore: suggestion.Score,
                    suggestionReasons: suggestion.Reasons))
                .ToArray();

            if (SuggestedMedia.Count == 0)
            {
                FindMediaStatus = "Keine weiteren passenden Medien im gewählten Zeitraum gefunden. Prüfe den Zeitraum oder aktualisiere deine Bibliothek.";
            }
            else if (!profile.HasLearningSignals)
            {
                FindMediaStatus =
                    $"{SuggestedMedia.Count:N0} Medien im Projektzeitraum gefunden. Noch gibt es zu wenig Projektdaten für eine feinere Bewertung; deshalb sind alle Treffer vorausgewählt.";
            }
            else
            {
                var preselected = SuggestedMedia.Count(item => item.IsSelected);
                FindMediaStatus = preselected == 0
                    ? $"{SuggestedMedia.Count:N0} Medien im Zeitraum gefunden. Elbwald hat Kamera, GPS, Pfad und zeitliche Nähe bewertet; bitte passende Treffer auswählen."
                    : $"{SuggestedMedia.Count:N0} Medien im Zeitraum gefunden. {preselected:N0} besonders passende Treffer sind vorausgewählt – bewertet nach Kamera, GPS, Pfad und zeitlicher Nähe.";
            }
        }
        catch (Exception exception)
        {
            FindMediaStatus = "Medienvorschläge konnten nicht ermittelt werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<ProjectSuggestionProfile> BuildSuggestionProfileAsync()
    {
        var cameraMakes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cameraModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gpsPoints = new List<(double Latitude, double Longitude)>();
        var captureDates = new List<DateTime>();
        var pathTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMeaningfulTokens(EditName, pathTokens);
        AddMeaningfulTokens(EditLocation, pathTokens);

        foreach (var media in ProjectMedia)
        {
            AddMeaningfulTokens(media.RelativePath, pathTokens);
        }

        var profileMedia = ProjectMedia
            .OrderByDescending(media => media.IsCover)
            .ThenByDescending(media => media.IsHighlight)
            .ThenByDescending(media => media.Reference.AddedAtUtc)
            .Take(40)
            .ToArray();

        foreach (var media in profileMedia)
        {
            var facts = await _mediaIndexService.GetFactsAsync(media.MediaItemId);
            var metadata = await _mediaIndexService.GetMetadataAsync(media.MediaItemId);

            if (!string.IsNullOrWhiteSpace(facts?.CameraMake))
            {
                cameraMakes.Add(NormalizeSuggestionValue(facts.CameraMake));
            }

            if (!string.IsNullOrWhiteSpace(facts?.CameraModel))
            {
                cameraModels.Add(NormalizeSuggestionValue(facts.CameraModel));
            }

            var effectiveCaptureDate = metadata.CaptureDateOverridden
                ? metadata.CaptureDate
                : facts?.CaptureDate;

            if (effectiveCaptureDate.HasValue)
            {
                captureDates.Add(effectiveCaptureDate.Value);
            }

            var effectiveGps = metadata.GpsMode switch
            {
                MetadataGpsMode.Set =>
                    (Latitude: metadata.Latitude, Longitude: metadata.Longitude),
                MetadataGpsMode.Remove =>
                    (Latitude: (double?)null, Longitude: (double?)null),
                _ =>
                    (Latitude: facts?.Latitude, Longitude: facts?.Longitude)
            };

            if (effectiveGps.Latitude is >= -90 and <= 90
                && effectiveGps.Longitude is >= -180 and <= 180)
            {
                gpsPoints.Add((effectiveGps.Latitude.Value, effectiveGps.Longitude.Value));
            }
        }

        return new ProjectSuggestionProfile(
            cameraMakes,
            cameraModels,
            gpsPoints,
            captureDates,
            pathTokens,
            ProjectMedia.Count(media => media.IsResolved));
    }

    private static (int Score, string Reasons) ScoreSuggestion(
        MediaIndexSearchResult result,
        ProjectSuggestionProfile profile)
    {
        var score = 55;
        var reasons = new List<string> { "Projektzeitraum" };

        var normalizedModel = NormalizeSuggestionValue(result.CameraModel);
        var normalizedMake = NormalizeSuggestionValue(result.CameraMake);

        if (normalizedModel.Length > 0 && profile.CameraModels.Contains(normalizedModel))
        {
            score += 18;
            reasons.Add("gleiche Kamera");
        }
        else if (normalizedMake.Length > 0 && profile.CameraMakes.Contains(normalizedMake))
        {
            score += 10;
            reasons.Add("gleicher Kamerahersteller");
        }

        if (result.Latitude is >= -90 and <= 90
            && result.Longitude is >= -180 and <= 180
            && profile.GpsPoints.Count > 0)
        {
            var nearestDistanceKm = profile.GpsPoints
                .Select(point => DistanceKilometers(
                    result.Latitude.Value,
                    result.Longitude.Value,
                    point.Latitude,
                    point.Longitude))
                .Min();

            if (nearestDistanceKm <= 25)
            {
                score += 25;
                reasons.Add("gleiche Gegend");
            }
            else if (nearestDistanceKm <= 100)
            {
                score += 16;
                reasons.Add("GPS in der Nähe");
            }
            else if (nearestDistanceKm <= 250)
            {
                score += 8;
                reasons.Add("regionale Nähe");
            }
        }

        var candidateTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddMeaningfulTokens(result.RepresentativeRelativePath, candidateTokens);
        AddMeaningfulTokens(result.Title, candidateTokens);

        if (candidateTokens.Overlaps(profile.PathTokens))
        {
            score += 12;
            reasons.Add("ähnlicher Pfad/Ort");
        }

        if (result.CaptureDate.HasValue && profile.CaptureDates.Count > 0)
        {
            var nearestDays = profile.CaptureDates
                .Select(date => Math.Abs((date.Date - result.CaptureDate.Value.Date).TotalDays))
                .Min();

            if (nearestDays <= 1)
            {
                score += 8;
                reasons.Add("gleicher Projekttag");
            }
            else if (nearestDays <= 3)
            {
                score += 5;
                reasons.Add("zeitlich sehr nah");
            }
            else if (nearestDays <= 7)
            {
                score += 3;
                reasons.Add("zeitlich nah");
            }
        }

        return (
            Math.Clamp(score, 0, 100),
            string.Join(" · ", reasons));
    }

    private static void AddMeaningfulTokens(
        string? value,
        ISet<string> target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var ignored = new HashSet<string>(
            new[]
            {
                "bild", "bilder", "foto", "fotos", "photo", "photos", "image", "images",
                "img", "dsc", "dcim", "media", "medien", "jpg", "jpeg", "png", "webp",
                "tif", "tiff"
            },
            StringComparer.OrdinalIgnoreCase);

        var tokens = value
            .Replace('\\', '/')
            .Split(
                new[] { '/', ' ', '_', '-', '.', '(', ')', '[', ']', ',', ';' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var normalized = NormalizeSuggestionValue(token);
            if (normalized.Length < 3
                || normalized.All(char.IsDigit)
                || ignored.Contains(normalized))
            {
                continue;
            }

            target.Add(normalized);
        }
    }

    private static string NormalizeSuggestionValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();

    private static double DistanceKilometers(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double earthRadiusKm = 6371.0088;

        static double ToRadians(double degrees) =>
            degrees * Math.PI / 180d;

        var lat1 = ToRadians(latitude1);
        var lat2 = ToRadians(latitude2);
        var deltaLat = ToRadians(latitude2 - latitude1);
        var deltaLon = ToRadians(longitude2 - longitude1);

        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
            + Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        var clamped = Math.Clamp(a, 0d, 1d);
        var c = 2 * Math.Atan2(Math.Sqrt(clamped), Math.Sqrt(1 - clamped));
        return earthRadiusKm * c;
    }

    private async Task AddSuggestedMediaAsync()
    {
        if (SelectedProject is null) return;
        var selected = SuggestedMedia.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0) return;

        IsBusy = true;
        try
        {
            var added = 0;
            foreach (var item in selected)
            {
                if (await _projectService.AddMediaAsync(SelectedProject.Id, item.MediaItemId)) added++;
            }

            var projectId = SelectedProject.Id;
            await RefreshSelectedProjectCountAsync(projectId);
            await LoadProjectMediaForSelectionAsync(projectId, _projectMediaLoadVersion);
            var selectedIds = selected.Select(item => item.MediaItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            SuggestedMedia = SuggestedMedia.Where(item => !selectedIds.Contains(item.MediaItemId)).ToArray();
            FindMediaStatus = added == 1
                ? "1 Medium wurde dem Projekt hinzugefügt. Die Datei bleibt unverändert."
                : $"{added:N0} Medien wurden dem Projekt hinzugefügt. Die Dateien bleiben unverändert.";
        }
        catch (Exception exception)
        {
            FindMediaStatus = "Projektzuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadJournalForSelectionAsync(
        string projectId,
        int loadVersion,
        string? selectEntryId = null)
    {
        try
        {
            var entries = await _projectService.GetJournalEntriesAsync(projectId);
            var items = new List<ProjectJournalEntryItemViewModel>(entries.Count);

            foreach (var entry in entries)
            {
                var media = await _projectService.GetJournalMediaReferencesAsync(entry.Id);
                items.Add(new ProjectJournalEntryItemViewModel(entry, media.Count));
            }

            if (loadVersion != _projectJournalLoadVersion
                || !string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal))
            {
                return;
            }

            JournalEntries = items;
            var selected = !string.IsNullOrWhiteSpace(selectEntryId)
                ? items.FirstOrDefault(item => string.Equals(item.Id, selectEntryId, StringComparison.Ordinal))
                : items.FirstOrDefault();

            if (selected is not null)
            {
                SelectedJournalEntry = selected;
                JournalStatus = items.Count == 1
                    ? "1 Tagebucheintrag vorhanden."
                    : $"{items.Count:N0} Tagebucheinträge vorhanden.";
            }
            else
            {
                StartNewJournalEntry();
                JournalStatus = "Noch keine Tagebucheinträge. Du kannst direkt den ersten Eintrag anlegen.";
            }
        }
        catch (Exception exception)
        {
            if (loadVersion == _projectJournalLoadVersion
                && string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal))
            {
                JournalStatus = "Tagebuch konnte nicht geladen werden: "
                    + exception.GetBaseException().Message;
            }
        }
    }

    private void StartNewJournalEntry()
    {
        if (SelectedProject is null)
        {
            return;
        }

        SelectedJournalEntry = null;
        ResetJournalDeleteArm();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var project = SelectedProject.Project;
        var defaultDate = project.StartDate.HasValue
            && project.EndDate.HasValue
            && today >= project.StartDate.Value
            && today <= project.EndDate.Value
                ? today
                : project.StartDate ?? today;

        JournalDateText = defaultDate.ToString("dd.MM.yyyy");
        JournalTitle = string.Empty;
        JournalBody = string.Empty;
        JournalLocation = project.Location ?? string.Empty;
        JournalStatus = "Neuer Eintrag – Datum, Titel und Notiz erfassen und anschließend speichern.";

        var editorVersion = ++_journalEditorLoadVersion;
        _ = LoadJournalMediaOptionsAsync(
            SelectedProject.Id,
            entryId: null,
            editorVersion);

        SaveJournalEntryCommand.NotifyCanExecuteChanged();
        DeleteJournalEntryCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveJournalEntryAsync()
    {
        if (!CanSaveJournalEntry() || SelectedProject is null)
        {
            return;
        }

        if (!TryParseOptionalDate(JournalDateText, out var entryDate)
            || !entryDate.HasValue)
        {
            JournalStatus = "Bitte ein gültiges Datum eingeben, z. B. 16.07.2026.";
            return;
        }

        var projectId = SelectedProject.Id;
        IsBusy = true;
        try
        {
            var request = new ProjectJournalEntryRequest(
                entryDate.Value,
                JournalTitle,
                JournalBody,
                JournalLocation);

            ProjectJournalEntry saved;
            if (SelectedJournalEntry is null)
            {
                saved = await _projectService.CreateJournalEntryAsync(
                    projectId,
                    request);
            }
            else
            {
                saved = await _projectService.UpdateJournalEntryAsync(
                    projectId,
                    SelectedJournalEntry.Id,
                    request);
            }

            var selectedMediaIds = JournalMediaOptions
                .Where(item => item.IsSelected)
                .Select(item => item.MediaItemId)
                .ToArray();

            await _projectService.SetJournalMediaAsync(
                projectId,
                saved.Id,
                selectedMediaIds);

            var loadVersion = ++_projectJournalLoadVersion;
            await LoadJournalForSelectionAsync(
                projectId,
                loadVersion,
                saved.Id);

            JournalStatus = SelectedJournalEntry?.MediaCount > 0
                ? "Tagebucheintrag gespeichert. Verknüpfte Medien bleiben unverändert."
                : "Tagebucheintrag gespeichert.";
        }
        catch (Exception exception)
        {
            JournalStatus = "Tagebucheintrag konnte nicht gespeichert werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteJournalEntryAsync()
    {
        if (SelectedProject is null || SelectedJournalEntry is null)
        {
            return;
        }

        if (!_journalDeleteArmed)
        {
            _journalDeleteArmed = true;
            OnPropertyChanged(nameof(JournalDeleteButtonText));
            JournalStatus = "Zum Löschen noch einmal auf „Löschen bestätigen“ klicken. Projektmedien und Originaldateien bleiben erhalten.";
            return;
        }

        var projectId = SelectedProject.Id;
        var entryId = SelectedJournalEntry.Id;
        IsBusy = true;
        try
        {
            await _projectService.DeleteJournalEntryAsync(projectId, entryId);
            ResetJournalDeleteArm();
            var loadVersion = ++_projectJournalLoadVersion;
            await LoadJournalForSelectionAsync(projectId, loadVersion);
            JournalStatus = "Tagebucheintrag gelöscht. Medien und Dateien wurden nicht verändert.";
        }
        catch (Exception exception)
        {
            JournalStatus = "Tagebucheintrag konnte nicht gelöscht werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadJournalMediaOptionsAsync(
        string projectId,
        string? entryId,
        int editorVersion)
    {
        try
        {
            var linkedIds = string.IsNullOrWhiteSpace(entryId)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : (await _projectService.GetJournalMediaReferencesAsync(entryId))
                    .Select(reference => reference.MediaItemId)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var projectReferences = await _projectService.GetMediaReferencesAsync(projectId);
            var options = new List<ProjectJournalMediaOptionViewModel>(projectReferences.Count);

            foreach (var reference in projectReferences)
            {
                var indexedItem = await _mediaIndexService.GetItemAsync(reference.MediaItemId);
                var locations = indexedItem is null
                    ? Array.Empty<MediaIndexLocation>()
                    : (await _mediaIndexService.GetLocationsAsync(reference.MediaItemId)).ToArray();

                var representative = locations
                    .Where(location => location.IsPresent)
                    .OrderByDescending(location => location.LastSeenUtc)
                    .FirstOrDefault()
                    ?? locations.OrderByDescending(location => location.LastSeenUtc).FirstOrDefault();

                var title = !string.IsNullOrWhiteSpace(representative?.RelativePath)
                    ? Path.GetFileName(representative.RelativePath)
                    : reference.MediaItemId;

                var detail = indexedItem is null
                    ? "Medium nicht mehr im Index"
                    : !string.IsNullOrWhiteSpace(representative?.RelativePath)
                        ? $"{indexedItem.MediaType} · {representative.RelativePath}"
                        : indexedItem.MediaType.ToString();

                options.Add(new ProjectJournalMediaOptionViewModel(
                    reference.MediaItemId,
                    title,
                    detail,
                    linkedIds.Contains(reference.MediaItemId)));
            }

            if (editorVersion != _journalEditorLoadVersion
                || !string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal)
                || !string.Equals(SelectedJournalEntry?.Id, entryId, StringComparison.Ordinal))
            {
                return;
            }

            JournalMediaOptions = options;
        }
        catch (Exception exception)
        {
            if (editorVersion == _journalEditorLoadVersion
                && string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal))
            {
                JournalStatus = "Projektmedien für den Tagebucheintrag konnten nicht geladen werden: "
                    + exception.GetBaseException().Message;
            }
        }
    }

    private void ResetJournalDeleteArm()
    {
        if (!_journalDeleteArmed)
        {
            return;
        }

        _journalDeleteArmed = false;
        OnPropertyChanged(nameof(JournalDeleteButtonText));
    }

    private static bool TryParseOptionalDate(string? text, out DateOnly? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var trimmed = text.Trim();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var styles = System.Globalization.DateTimeStyles.None;

        if (!DateOnly.TryParseExact(trimmed, "dd.MM.yyyy", culture, styles, out var parsed)
            && !DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", culture, styles, out parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private async Task AddSelectedMediaAsync()
    {
        if (SelectedProject is null || SelectedSearchResult is null) return;
        IsBusy = true;
        try
        {
            var projectId = SelectedProject.Id;
            var added = await _projectService.AddMediaAsync(projectId, SelectedSearchResult.MediaItemId);
            await RefreshSelectedProjectCountAsync(projectId);
            await LoadProjectMediaForSelectionAsync(projectId, _projectMediaLoadVersion);
            SearchResults = SearchResults.Where(r => r.MediaItemId != SelectedSearchResult.MediaItemId).ToArray();
            SelectedSearchResult = SearchResults.FirstOrDefault();
            StatusMessage = added ? "Medium zum Projekt hinzugefügt. Die Datei bleibt unverändert." : "Dieses Medium gehört bereits zum Projekt.";
        }
        catch (Exception exception) { StatusMessage = "Medium konnte nicht hinzugefügt werden: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
    }

    private async Task RemoveSelectedMediaAsync()
    {
        if (SelectedProject is null || SelectedProjectMedia is null) return;
        var item = SelectedProjectMedia;
        IsBusy = true;
        try
        {
            var projectId = SelectedProject.Id;
            var removedCover = item.IsCover;
            await _projectService.RemoveMediaAsync(projectId, item.MediaItemId);

            if (removedCover)
            {
                await ReloadProjectsAndSelectAsync(projectId);
            }
            else
            {
                await RefreshSelectedProjectCountAsync(projectId);
                await LoadProjectMediaForSelectionAsync(projectId, _projectMediaLoadVersion);
            }

            StatusMessage = removedCover
                ? "Medium aus dem Projekt entfernt. Das Projektcover wurde ebenfalls zurückgesetzt; die Datei selbst blieb unverändert."
                : "Medium aus dem Projekt entfernt. Die Datei selbst wurde nicht verändert.";
        }
        catch (Exception exception) { StatusMessage = "Zuordnung konnte nicht entfernt werden: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
    }

    private async Task SetSelectedMediaAsCoverAsync()
    {
        if (!CanSetCover() || SelectedProject is null || SelectedProjectMedia is null)
        {
            return;
        }

        var projectId = SelectedProject.Id;
        var mediaId = SelectedProjectMedia.MediaItemId;
        IsBusy = true;

        try
        {
            await _projectService.SetCoverAsync(projectId, mediaId);
            await ReloadProjectsAndSelectAsync(projectId);
            StatusMessage = "Projektcover gespeichert. Die Mediendatei wurde nicht verändert.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Projektcover konnte nicht gespeichert werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearCoverAsync()
    {
        if (!CanClearCover() || SelectedProject is null)
        {
            return;
        }

        var projectId = SelectedProject.Id;
        IsBusy = true;

        try
        {
            await _projectService.SetCoverAsync(projectId, null);
            await ReloadProjectsAndSelectAsync(projectId);
            StatusMessage = "Projektcover zurückgesetzt.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Projektcover konnte nicht zurückgesetzt werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleSelectedHighlightAsync()
    {
        if (SelectedProject is null || SelectedProjectMedia is null)
        {
            return;
        }

        var item = SelectedProjectMedia;
        var newValue = !item.IsHighlight;
        IsBusy = true;

        try
        {
            await _projectService.SetHighlightAsync(
                SelectedProject.Id,
                item.MediaItemId,
                newValue);

            item.IsHighlight = newValue;
            HighlightedMedia = ProjectMedia
                .Where(media => media.IsHighlight)
                .ToArray();

            StatusMessage = newValue
                ? "Medium als Highlight markiert."
                : "Highlight-Markierung entfernt.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Highlight konnte nicht geändert werden: "
                + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadProjectMediaForSelectionAsync(
        string projectId,
        int loadVersion)
    {
        try
        {
            var items = await BuildProjectMediaAsync(projectId);

            if (loadVersion != _projectMediaLoadVersion
                || !string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal))
            {
                return;
            }

            SetProjectMedia(items);
            var journalEditorVersion = ++_journalEditorLoadVersion;
            await LoadJournalMediaOptionsAsync(
                projectId,
                SelectedJournalEntry?.Id,
                journalEditorVersion);
            await LoadProjectThumbnailsAsync(
                items,
                projectId,
                loadVersion);
        }
        catch (Exception exception)
        {
            if (loadVersion == _projectMediaLoadVersion
                && string.Equals(SelectedProject?.Id, projectId, StringComparison.Ordinal))
            {
                StatusMessage = "Projektmedien konnten nicht geladen werden: "
                    + exception.GetBaseException().Message;
            }
        }
    }

    private async Task<IReadOnlyList<ProjectMediaItemViewModel>> BuildProjectMediaAsync(
        string projectId)
    {
        var refs = await _projectService.GetMediaReferencesAsync(projectId);
        var items = new List<ProjectMediaItemViewModel>(refs.Count);

        foreach (var reference in refs)
        {
            MediaIndexSearchResult? resolved = null;
            var item = await _mediaIndexService.GetItemAsync(reference.MediaItemId);

            if (item is not null)
            {
                var locations = await _mediaIndexService.GetLocationsAsync(reference.MediaItemId);
                var representative = locations
                    .Where(location => location.IsPresent)
                    .OrderByDescending(location => location.LastSeenUtc)
                    .FirstOrDefault()
                    ?? locations
                        .OrderByDescending(location => location.LastSeenUtc)
                        .FirstOrDefault();

                resolved = new MediaIndexSearchResult(
                    item,
                    representative?.EndpointId,
                    representative?.RelativePath,
                    representative?.IsPresent ?? false,
                    locations.Count(location => location.IsPresent),
                    locations.Count(location => !location.IsPresent));
            }

            var isCover = string.Equals(
                reference.MediaItemId,
                SelectedProject?.CoverMediaItemId,
                StringComparison.OrdinalIgnoreCase);

            items.Add(new ProjectMediaItemViewModel(
                reference,
                resolved,
                isCover));
        }

        return items;
    }

    private async Task ReloadProjectsAndSelectAsync(string projectId)
    {
        var records = await _projectService.GetProjectsAsync();
        var items = new List<ProjectListItemViewModel>();
        foreach (var record in records)
        {
            var refs = await _projectService.GetMediaReferencesAsync(record.Id);
            items.Add(new ProjectListItemViewModel(record, refs.Count));
        }
        SetProjects(items);
        SelectedProject = Projects.FirstOrDefault(project => project.Id == projectId);
        StartProjectCoverThumbnailLoad(Projects);
    }

    private async Task RefreshSelectedProjectCountAsync(string projectId)
    {
        var target = Projects.FirstOrDefault(item =>
            string.Equals(item.Id, projectId, StringComparison.Ordinal));

        if (target is null)
        {
            return;
        }

        var refs = await _projectService.GetMediaReferencesAsync(projectId);
        target.MediaCount = refs.Count;
        if (ReferenceEquals(target, SelectedProject))
        {
            OnPropertyChanged(nameof(SelectedProjectMediaCountText));
        }
    }

    private void SetProjectMedia(IReadOnlyList<ProjectMediaItemViewModel> items)
    {
        foreach (var existing in ProjectMedia)
        {
            existing.Dispose();
        }

        ProjectMedia = items;
        HighlightedMedia = items.Where(item => item.IsHighlight).ToArray();
        SelectedProjectMedia = items.FirstOrDefault();
        HasProjectMedia = items.Count > 0;
        HasNoProjectMedia = items.Count == 0;
    }

    private void SetProjects(IReadOnlyList<ProjectListItemViewModel> items)
    {
        CancelProjectCoverThumbnailLoad();

        foreach (var existing in Projects)
        {
            existing.Dispose();
        }

        Projects = items;
    }

    private void StartProjectCoverThumbnailLoad(
        IReadOnlyList<ProjectListItemViewModel> items)
    {
        CancelProjectCoverThumbnailLoad();
        var loadVersion = ++_projectCoverLoadVersion;
        _projectCoverCancellation = new CancellationTokenSource();
        _ = LoadProjectCoverThumbnailsAsync(
            items,
            loadVersion,
            _projectCoverCancellation.Token);
    }

    private async Task LoadProjectCoverThumbnailsAsync(
        IReadOnlyList<ProjectListItemViewModel> items,
        int loadVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await LoadEndpointRootsAsync(cancellationToken);

            foreach (var project in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (loadVersion != _projectCoverLoadVersion)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(project.CoverMediaItemId))
                {
                    project.SetCoverUnavailable("Noch kein Projektcover");
                    continue;
                }

                project.CoverLoading = true;

                try
                {
                    var indexedItem = await _mediaIndexService.GetItemAsync(
                        project.CoverMediaItemId,
                        cancellationToken);

                    if (indexedItem is null
                        || indexedItem.MediaType != MediaFileType.Image)
                    {
                        project.SetCoverUnavailable("Cover nicht verfügbar");
                        continue;
                    }

                    var locations = await _mediaIndexService.GetLocationsAsync(
                        project.CoverMediaItemId,
                        cancellationToken);

                    var representative = locations
                        .Where(location => location.IsPresent)
                        .OrderByDescending(location => location.LastSeenUtc)
                        .FirstOrDefault()
                        ?? locations
                            .OrderByDescending(location => location.LastSeenUtc)
                            .FirstOrDefault();

                    var resolved = new MediaIndexSearchResult(
                        indexedItem,
                        representative?.EndpointId,
                        representative?.RelativePath,
                        representative?.IsPresent ?? false,
                        locations.Count(location => location.IsPresent),
                        locations.Count(location => !location.IsPresent));

                    var file = TryResolveMediaFile(resolved);
                    if (file is null)
                    {
                        project.SetCoverUnavailable("Coverdatei nicht verfügbar");
                        continue;
                    }

                    var result = await _thumbnailService.GetIndexedThumbnailAsync(
                        project.CoverMediaItemId,
                        file,
                        _thumbnailOptions.DefaultWidth,
                        cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (loadVersion != _projectCoverLoadVersion)
                    {
                        return;
                    }

                    if (result.IsSuccessful
                        && result.EncodedImage is { Length: > 0 })
                    {
                        using var stream = new MemoryStream(
                            result.EncodedImage,
                            writable: false);

                        project.SetCoverThumbnail(new Bitmap(stream));
                    }
                    else
                    {
                        project.SetCoverUnavailable("Covervorschau nicht verfügbar");
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    project.SetCoverUnavailable("Covervorschau nicht verfügbar");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Project list was refreshed while cover thumbnails were loading.
        }
        catch
        {
            foreach (var project in items.Where(item => item.CoverLoading))
            {
                project.SetCoverUnavailable("Covervorschau nicht verfügbar");
            }
        }
    }

    private async Task LoadProjectThumbnailsAsync(
        IReadOnlyList<ProjectMediaItemViewModel> items,
        string projectId,
        int loadVersion)
    {
        CancelProjectThumbnailLoad();
        _projectThumbnailCancellation = new CancellationTokenSource();
        var cancellationToken = _projectThumbnailCancellation.Token;

        try
        {
            await LoadEndpointRootsAsync(cancellationToken);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (loadVersion != _projectMediaLoadVersion
                    || !string.Equals(
                        SelectedProject?.Id,
                        projectId,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (!item.IsImage)
                {
                    item.SetThumbnailUnavailable(item.MediaType);
                    continue;
                }

                if (item.Result is null)
                {
                    item.SetThumbnailUnavailable("Medium nicht verfügbar");
                    continue;
                }

                var file = TryResolveMediaFile(item.Result);
                if (file is null)
                {
                    item.SetThumbnailUnavailable("Datei nicht verfügbar");
                    continue;
                }

                item.ThumbnailLoading = true;

                try
                {
                    var result = await _thumbnailService.GetIndexedThumbnailAsync(
                        item.MediaItemId,
                        file,
                        _thumbnailOptions.GridWidth,
                        cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    if (loadVersion != _projectMediaLoadVersion
                        || !string.Equals(
                            SelectedProject?.Id,
                            projectId,
                            StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (result.IsSuccessful
                        && result.EncodedImage is { Length: > 0 })
                    {
                        using var stream = new MemoryStream(
                            result.EncodedImage,
                            writable: false);

                        item.SetThumbnail(new Bitmap(stream));
                    }
                    else
                    {
                        item.SetThumbnailUnavailable("Keine Vorschau");
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    item.SetThumbnailUnavailable("Keine Vorschau");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Selection changed while previews were being generated.
        }
        catch
        {
            foreach (var item in items.Where(item => item.ThumbnailLoading))
            {
                item.SetThumbnailUnavailable("Keine Vorschau");
            }
        }
    }

    private async Task LoadEndpointRootsAsync(
        CancellationToken cancellationToken)
    {
        var settings = await _storageSettingsStore.LoadAsync(cancellationToken);
        var roots = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = _storageLocationResolver.Resolve(endpoint.Location);
            if (!resolution.IsAvailable
                || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                continue;
            }

            roots[endpoint.Id] = Path.GetFullPath(resolution.ResolvedPath);
        }

        _endpointRoots = roots;
    }

    private MediaFile? TryResolveMediaFile(
        MediaIndexSearchResult result)
    {
        if (!result.RepresentativeIsPresent
            || string.IsNullOrWhiteSpace(result.RepresentativeEndpointId)
            || string.IsNullOrWhiteSpace(result.RepresentativeRelativePath)
            || !_endpointRoots.TryGetValue(
                result.RepresentativeEndpointId,
                out var root))
        {
            return null;
        }

        try
        {
            var relative = result.RepresentativeRelativePath;
            if (Path.IsPathRooted(relative))
            {
                return null;
            }

            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(
                Path.Combine(fullRoot, relative));

            var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    rootPrefix,
                    StringComparison.Ordinal)
                || !File.Exists(fullPath))
            {
                return null;
            }

            var info = new FileInfo(fullPath);

            return new MediaFile(
                fullPath,
                info.Name,
                info.Extension,
                info.Length,
                info.CreationTimeUtc,
                info.LastWriteTimeUtc,
                result.Item.MediaType,
                (info.Attributes & FileAttributes.ReparsePoint) != 0);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or ArgumentException)
        {
            return null;
        }
    }

    private void CancelProjectThumbnailLoad()
    {
        _projectThumbnailCancellation?.Cancel();
        _projectThumbnailCancellation?.Dispose();
        _projectThumbnailCancellation = null;
    }

    private void CancelProjectCoverThumbnailLoad()
    {
        _projectCoverCancellation?.Cancel();
        _projectCoverCancellation?.Dispose();
        _projectCoverCancellation = null;
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateProjectCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        AddSelectedMediaCommand.NotifyCanExecuteChanged();
        RemoveMediaCommand.NotifyCanExecuteChanged();
        SetCoverCommand.NotifyCanExecuteChanged();
        ClearCoverCommand.NotifyCanExecuteChanged();
        ToggleHighlightCommand.NotifyCanExecuteChanged();
        FindMediaCommand.NotifyCanExecuteChanged();
        AddSuggestedMediaCommand.NotifyCanExecuteChanged();
        NewJournalEntryCommand.NotifyCanExecuteChanged();
        SaveJournalEntryCommand.NotifyCanExecuteChanged();
        DeleteJournalEntryCommand.NotifyCanExecuteChanged();
    }
    private sealed record ProjectSuggestion(
        MediaIndexSearchResult Result,
        int Score,
        string Reasons);

    private sealed record ProjectSuggestionProfile(
        IReadOnlySet<string> CameraMakes,
        IReadOnlySet<string> CameraModels,
        IReadOnlyList<(double Latitude, double Longitude)> GpsPoints,
        IReadOnlyList<DateTime> CaptureDates,
        IReadOnlySet<string> PathTokens,
        int SourceMediaCount)
    {
        public bool HasLearningSignals =>
            SourceMediaCount > 0
            && (CameraMakes.Count > 0
                || CameraModels.Count > 0
                || GpsPoints.Count > 0
                || CaptureDates.Count > 0
                || PathTokens.Count > 0);
    }

}
