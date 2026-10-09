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
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ProjectSearchResultViewModel> searchResults = Array.Empty<ProjectSearchResultViewModel>();
    [ObservableProperty] private ProjectSearchResultViewModel? selectedSearchResult;
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

        _ = RefreshAsync();
    }

    public IReadOnlyList<string> ProjectKinds { get; } = new[] { "Sammlung", "Urlaub", "Hochzeit", "Geburtstag", "Fotobuch", "Kalender", "Sonstiges" };

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand CreateProjectCommand { get; }
    public IAsyncRelayCommand SaveProjectCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand AddSelectedMediaCommand { get; }
    public IAsyncRelayCommand RemoveMediaCommand { get; }

    public bool HasProjects => Projects.Count > 0;
    public bool HasSelectedProject => SelectedProject is not null;
    public bool HasNoSelectedProject => SelectedProject is null;
    public bool HasSearchResults => SearchResults.Count > 0;

    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnNewProjectNameChanged(string value) => CreateProjectCommand.NotifyCanExecuteChanged();
    partial void OnEditNameChanged(string value) => SaveProjectCommand.NotifyCanExecuteChanged();
    partial void OnProjectsChanged(IReadOnlyList<ProjectListItemViewModel> value) => OnPropertyChanged(nameof(HasProjects));
    partial void OnSearchResultsChanged(IReadOnlyList<ProjectSearchResultViewModel> value) => OnPropertyChanged(nameof(HasSearchResults));
    partial void OnSelectedSearchResultChanged(ProjectSearchResultViewModel? value) => AddSelectedMediaCommand.NotifyCanExecuteChanged();
    partial void OnSelectedProjectMediaChanged(ProjectMediaItemViewModel? value) => RemoveMediaCommand.NotifyCanExecuteChanged();

    partial void OnSelectedProjectChanged(ProjectListItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(HasNoSelectedProject));
        SearchResults = Array.Empty<ProjectSearchResultViewModel>();
        SelectedSearchResult = null;
        var loadVersion = ++_projectMediaLoadVersion;

        if (value is null)
        {
            EditName = string.Empty;
            EditKind = "Sammlung";
            EditLocation = string.Empty;
            EditNotes = string.Empty;
            SetProjectMedia(Array.Empty<ProjectMediaItemViewModel>());
        }
        else
        {
            EditName = value.Project.Name;
            EditKind = value.Project.Kind;
            EditLocation = value.Project.Location ?? string.Empty;
            EditNotes = value.Project.Notes ?? string.Empty;
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
            SelectedProject = selectedId is null ? Projects.FirstOrDefault() : Projects.FirstOrDefault(p => p.Id == selectedId) ?? Projects.FirstOrDefault();

            if (SelectedProject is null)
            {
                SetProjectMedia(Array.Empty<ProjectMediaItemViewModel>());
            }

            StatusMessage = Projects.Count == 0 ? "Noch keine Projekte angelegt." : $"{Projects.Count:N0} Projekt(e) · Zuordnungen referenzieren stabile Media-IDs.";
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
            var updated = await _projectService.UpdateProjectAsync(
                SelectedProject.Id,
                new ProjectUpdateRequest(EditName, EditKind, Location: EditLocation, Notes: EditNotes));
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
            StatusMessage = added ? "Medium zum Projekt hinzugefügt." : "Medium war bereits dem Projekt zugeordnet.";
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
            StatusMessage = "Projektzuordnung entfernt. Die Mediendatei wurde nicht verändert.";
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
    }
}
