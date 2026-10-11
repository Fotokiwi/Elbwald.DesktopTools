using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class ProjectsViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly IMediaIndexService _mediaIndexService;
    private int _projectMediaLoadVersion;

    [ObservableProperty] private IReadOnlyList<ProjectListItemViewModel> projects = Array.Empty<ProjectListItemViewModel>();
    [ObservableProperty] private ProjectListItemViewModel? selectedProject;
    [ObservableProperty] private IReadOnlyList<ProjectMediaItemViewModel> projectMedia = Array.Empty<ProjectMediaItemViewModel>();
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

    public ProjectsViewModel(IProjectService projectService, IMediaIndexService mediaIndexService)
    {
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _mediaIndexService = mediaIndexService ?? throw new ArgumentNullException(nameof(mediaIndexService));

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync, CanCreateProject);
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, CanSaveProject);
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && SelectedProject is not null);
        AddSelectedMediaCommand = new AsyncRelayCommand(AddSelectedMediaAsync, () => !IsBusy && SelectedProject is not null && SelectedSearchResult is not null);
        RemoveMediaCommand = new AsyncRelayCommand(RemoveSelectedMediaAsync, () => !IsBusy && SelectedProject is not null && SelectedProjectMedia is not null);
        FindMediaCommand = new AsyncRelayCommand(FindMediaAsync, () => !IsBusy && SelectedProject is not null);
        AddSuggestedMediaCommand = new AsyncRelayCommand(AddSuggestedMediaAsync, () => !IsBusy && SelectedProject is not null && SuggestedMedia.Any(item => item.IsSelected));
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
    public IAsyncRelayCommand FindMediaCommand { get; }
    public IAsyncRelayCommand AddSuggestedMediaCommand { get; }
    public IRelayCommand BackToProjectsCommand { get; }

    public bool HasProjects => Projects.Count > 0;
    public bool HasSelectedProject => SelectedProject is not null;
    public bool HasNoSelectedProject => SelectedProject is null;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasSuggestedMedia => SuggestedMedia.Count > 0;
    public string SelectedProjectMediaCountText => SelectedProject?.MediaCountText ?? "0 Medien";

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnNewProjectNameChanged(string value) => CreateProjectCommand.NotifyCanExecuteChanged();
    partial void OnEditNameChanged(string value) => SaveProjectCommand.NotifyCanExecuteChanged();
    partial void OnProjectsChanged(IReadOnlyList<ProjectListItemViewModel> value) => OnPropertyChanged(nameof(HasProjects));
    partial void OnSearchResultsChanged(IReadOnlyList<ProjectSearchResultViewModel> value) => OnPropertyChanged(nameof(HasSearchResults));
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
    partial void OnSelectedProjectMediaChanged(ProjectMediaItemViewModel? value) => RemoveMediaCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProjectChanged(ProjectListItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(HasNoSelectedProject));
        OnPropertyChanged(nameof(SelectedProjectMediaCountText));
        SearchResults = Array.Empty<ProjectSearchResultViewModel>();
        SelectedSearchResult = null;
        var loadVersion = ++_projectMediaLoadVersion;

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
            _ = LoadProjectMediaForSelectionAsync(value.Id, loadVersion);
        }
        NotifyCommands();
    }

    private bool CanCreateProject() => !IsBusy && !string.IsNullOrWhiteSpace(NewProjectName);
    private bool CanSaveProject() => !IsBusy && SelectedProject is not null && !string.IsNullOrWhiteSpace(EditName);

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
            Projects = items;
            SelectedProject = selectedId is null
                ? null
                : Projects.FirstOrDefault(p => p.Id == selectedId);

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
            var assignedIds = (await _projectService.GetMediaReferencesAsync(SelectedProject.Id))
                .Select(reference => reference.MediaItemId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                    SortOrder: MediaIndexSortOrder.FileNameAscending,
                    CaptureDateFrom: from,
                    CaptureDateTo: to));
                all.AddRange(page);
                if (page.Count < pageSize) break;
                offset += page.Count;
            }

            SuggestedMedia = all
                .Where(result => !assignedIds.Contains(result.Item.Id))
                .Select(result => new ProjectSearchResultViewModel(result, isSelected: true))
                .ToArray();

            FindMediaStatus = SuggestedMedia.Count == 0
                ? "Keine weiteren passenden Medien im gewählten Zeitraum gefunden. Prüfe den Zeitraum oder aktualisiere deine Bibliothek."
                : $"{SuggestedMedia.Count:N0} passende Medien gefunden. Wähle aus, was zum Projekt gehört.";
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
            await _projectService.RemoveMediaAsync(projectId, item.MediaItemId);
            await RefreshSelectedProjectCountAsync(projectId);
            await LoadProjectMediaForSelectionAsync(projectId, _projectMediaLoadVersion);
            StatusMessage = "Medium aus dem Projekt entfernt. Die Datei selbst wurde nicht verändert.";
        }
        catch (Exception exception) { StatusMessage = "Zuordnung konnte nicht entfernt werden: " + exception.GetBaseException().Message; }
        finally { IsBusy = false; }
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

            items.Add(new ProjectMediaItemViewModel(reference, resolved));
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
        Projects = items;
        SelectedProject = Projects.FirstOrDefault(project => project.Id == projectId);

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
        ProjectMedia = items;
        SelectedProjectMedia = items.FirstOrDefault();
        HasProjectMedia = items.Count > 0;
        HasNoProjectMedia = items.Count == 0;
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        CreateProjectCommand.NotifyCanExecuteChanged();
        SaveProjectCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        AddSelectedMediaCommand.NotifyCanExecuteChanged();
        RemoveMediaCommand.NotifyCanExecuteChanged();
        FindMediaCommand.NotifyCanExecuteChanged();
        AddSuggestedMediaCommand.NotifyCanExecuteChanged();
    }
}
