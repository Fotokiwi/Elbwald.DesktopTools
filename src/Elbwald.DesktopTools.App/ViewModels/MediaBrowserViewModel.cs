using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.Knowledge;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.MediaIndex.Collections;
using Elbwald.DesktopTools.Contracts.Projects;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.App.Services;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MediaBrowserViewModel : ObservableObject, IDisposable
{
    private const int PageSize = 120;

    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IMediaThumbnailService _thumbnailService;
    private readonly MediaThumbnailOptions _thumbnailOptions;
    private readonly IImageMetadataReader _metadataReader;
    private readonly IProjectService _projectService;
    private readonly MediaExportSelectionContext _exportSelectionContext;
    private readonly INavigationService _navigationService;

    private readonly List<MediaBrowserItemViewModel> _items = [];
    private readonly List<LibraryFolderTreeNodeViewModel> _folderRoots = [];
    private readonly List<LibraryTimelineTreeNodeViewModel> _timelineRoots = [];
    private IReadOnlyDictionary<string, string> _endpointNames = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _endpointRoots = new Dictionary<string, string>();
    private CancellationTokenSource? _loadCancellation;
    private int _offset;
    private bool _lastPageWasFull;
    private int _selectionGeneration;
    private string? _navigationEndpointId;
    private string? _navigationRelativeFolder;
    private string? _navigationProjectId;
    private MediaFileType? _navigationMediaType;
    private DateTime? _navigationCaptureDateFrom;
    private DateTime? _navigationCaptureDateTo;
    private IReadOnlyList<string> _navigationKnowledgeItemIds = Array.Empty<string>();
    private string? _navigationSmartCollectionId;
    private int? _navigationTimelineYear;
    private int? _navigationTimelineMonth;
    private int? _navigationTimelineDay;
    private string? _editingSmartCollectionId;
    private string? _smartCollectionEditorEndpointId;
    private string? _smartCollectionEditorRelativeFolder;
    private IReadOnlyList<string> _selectedMediaIds = Array.Empty<string>();

    [ObservableProperty]
    private bool isNavigationOpen;

    [ObservableProperty]
    private string navigationTitle = "Alle Medien";

    [ObservableProperty]
    private IReadOnlyList<LibraryFolderTreeNodeViewModel> folderTreeItems = Array.Empty<LibraryFolderTreeNodeViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryBreadcrumbItemViewModel> navigationBreadcrumbs = Array.Empty<LibraryBreadcrumbItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryNavigationItemViewModel> projectNavigationItems = Array.Empty<LibraryNavigationItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibraryNavigationItemViewModel> typeNavigationItems = Array.Empty<LibraryNavigationItemViewModel>();

    [ObservableProperty]
    private string selectedNavigationPanel = "Ordner";

    [ObservableProperty]
    private IReadOnlyList<LibraryTimelineTreeNodeViewModel> timelineItems = Array.Empty<LibraryTimelineTreeNodeViewModel>();

    [ObservableProperty]
    private IReadOnlyList<LibrarySmartCollectionItemViewModel> smartCollections = Array.Empty<LibrarySmartCollectionItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<SmartCollectionKnowledgeOptionViewModel> smartCollectionKnowledgeOptions = Array.Empty<SmartCollectionKnowledgeOptionViewModel>();

    [ObservableProperty]
    private string smartCollectionName = string.Empty;

    [ObservableProperty]
    private string smartCollectionSearchText = string.Empty;

    [ObservableProperty]
    private string smartCollectionType = "Alle Medien";

    [ObservableProperty]
    private string smartCollectionRating = "Keine Mindestbewertung";

    [ObservableProperty]
    private string smartCollectionFromText = string.Empty;

    [ObservableProperty]
    private string smartCollectionToText = string.Empty;

    [ObservableProperty]
    private string smartCollectionStatus = "Neue dynamische Sammlung anlegen oder bestehende bearbeiten.";

    [ObservableProperty]
    private string smartCollectionScopeText = "Gesamte Bibliothek";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "Bibliothek wird geladen …";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedTypeFilter = "Alle Medien";

    [ObservableProperty]
    private string selectedRatingFilter = "Alle Bewertungen";

    [ObservableProperty]
    private string selectedSort = "Zuletzt gesehen";

    [ObservableProperty]
    private bool isGridView = true;

    [ObservableProperty]
    private IReadOnlyList<MediaBrowserItemViewModel> items = Array.Empty<MediaBrowserItemViewModel>();

    [ObservableProperty]
    private int selectionCount;

    [ObservableProperty]
    private MediaBrowserItemViewModel? selectedMedia;

    [ObservableProperty]
    private Bitmap? selectedPreview;

    [ObservableProperty]
    private string selectedTitle = string.Empty;

    [ObservableProperty]
    private string selectedSubTitle = string.Empty;

    [ObservableProperty]
    private string selectedMediaId = string.Empty;

    [ObservableProperty]
    private string selectedFileName = string.Empty;

    [ObservableProperty]
    private string selectedType = string.Empty;

    [ObservableProperty]
    private string selectedSize = string.Empty;

    [ObservableProperty]
    private string selectedCaptureDate = string.Empty;

    [ObservableProperty]
    private string selectedCamera = string.Empty;

    [ObservableProperty]
    private string selectedDimensions = string.Empty;

    [ObservableProperty]
    private string selectedGps = string.Empty;

    [ObservableProperty]
    private string selectedDescription = string.Empty;

    [ObservableProperty]
    private string selectedProjects = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MediaIndexLocationItemViewModel> selectedLocations = Array.Empty<MediaIndexLocationItemViewModel>();

    [ObservableProperty]
    private string detailStatus = "Medium auswählen, um Details anzuzeigen.";

    [ObservableProperty]
    private string quickKnowledgeKind = "Tag";

    [ObservableProperty]
    private string quickKnowledgeText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<QuickKnowledgeItemViewModel> quickKnowledgeItems = Array.Empty<QuickKnowledgeItemViewModel>();

    [ObservableProperty]
    private string quickKnowledgeStatus = "Auswahl treffen, um Personen, Orte, Tags oder Ereignisse zuzuweisen.";

    [ObservableProperty]
    private string selectedKnowledgeSummary = "–";

    [ObservableProperty]
    private string selectedRatingText = "Unbewertet";

    [ObservableProperty]
    private IReadOnlyList<AssignedKnowledgeItemViewModel> selectedKnowledgeItems = Array.Empty<AssignedKnowledgeItemViewModel>();

    [ObservableProperty]
    private IReadOnlyList<QuickProjectOptionViewModel> quickProjects = Array.Empty<QuickProjectOptionViewModel>();

    [ObservableProperty]
    private QuickProjectOptionViewModel? selectedQuickProject;

    [ObservableProperty]
    private string quickProjectStatus = "Auswahl kann direkt einem Projekt zugeordnet werden.";

    public MediaBrowserViewModel(
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IMediaThumbnailService thumbnailService,
        MediaThumbnailOptions thumbnailOptions,
        IImageMetadataReader metadataReader,
        IProjectService projectService,
        MediaExportSelectionContext exportSelectionContext,
        INavigationService navigationService)
    {
        _mediaIndexService = mediaIndexService ?? throw new ArgumentNullException(nameof(mediaIndexService));
        _storageSettingsStore = storageSettingsStore ?? throw new ArgumentNullException(nameof(storageSettingsStore));
        _storageLocationResolver = storageLocationResolver ?? throw new ArgumentNullException(nameof(storageLocationResolver));
        _thumbnailService = thumbnailService ?? throw new ArgumentNullException(nameof(thumbnailService));
        _thumbnailOptions = thumbnailOptions ?? throw new ArgumentNullException(nameof(thumbnailOptions));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _projectService = projectService ?? throw new ArgumentNullException(nameof(projectService));
        _exportSelectionContext = exportSelectionContext ?? throw new ArgumentNullException(nameof(exportSelectionContext));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
        SearchCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => !IsBusy && CanLoadMore);
        ShowGridCommand = new RelayCommand(() => IsGridView = true);
        ShowListCommand = new RelayCommand(() => IsGridView = false);
        ToggleNavigationCommand = new AsyncRelayCommand(ToggleNavigationAsync);
        ShowAllMediaCommand = new AsyncRelayCommand(ShowAllMediaAsync);
        SelectNavigationCommand = new AsyncRelayCommand<LibraryNavigationItemViewModel>(SelectNavigationAsync);
        SelectNavigationPanelCommand = new AsyncRelayCommand<string>(SelectNavigationPanelAsync);
        NewSmartCollectionCommand = new RelayCommand(NewSmartCollection);
        UseCurrentViewForSmartCollectionCommand = new RelayCommand(UseCurrentViewForSmartCollection);
        SaveSmartCollectionCommand = new AsyncRelayCommand(SaveSmartCollectionAsync, CanSaveSmartCollection);
        AddQuickKnowledgeCommand = new AsyncRelayCommand(AddQuickKnowledgeAsync, CanApplyQuickKnowledge);
        SetRatingCommand = new AsyncRelayCommand<string>(SetRatingAsync, _ => HasSelection && !IsBusy);
        AssignProjectCommand = new AsyncRelayCommand(AssignProjectAsync, CanAssignProject);
        ExportSelectionCommand = new RelayCommand(OpenExport, () => HasSelection && !IsBusy);

        TypeNavigationItems =
        [
            new LibraryNavigationItemViewModel(LibraryNavigationKind.MediaType, "Bilder", "image", select: SelectNavigationAsync),
            new LibraryNavigationItemViewModel(LibraryNavigationKind.MediaType, "Videos", "video", select: SelectNavigationAsync),
            new LibraryNavigationItemViewModel(LibraryNavigationKind.MediaType, "Audio", "audio", select: SelectNavigationAsync)
        ];

        _ = InitializeAsync();
    }

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand SearchCommand { get; }
    public IAsyncRelayCommand LoadMoreCommand { get; }
    public IRelayCommand ShowGridCommand { get; }
    public IRelayCommand ShowListCommand { get; }
    public IAsyncRelayCommand ToggleNavigationCommand { get; }
    public IAsyncRelayCommand ShowAllMediaCommand { get; }
    public IAsyncRelayCommand<LibraryNavigationItemViewModel> SelectNavigationCommand { get; }
    public IAsyncRelayCommand<string> SelectNavigationPanelCommand { get; }
    public IRelayCommand NewSmartCollectionCommand { get; }
    public IRelayCommand UseCurrentViewForSmartCollectionCommand { get; }
    public IAsyncRelayCommand SaveSmartCollectionCommand { get; }
    public IAsyncRelayCommand AddQuickKnowledgeCommand { get; }
    public IAsyncRelayCommand<string> SetRatingCommand { get; }
    public IAsyncRelayCommand AssignProjectCommand { get; }
    public IRelayCommand ExportSelectionCommand { get; }

    public IReadOnlyList<string> TypeFilters { get; } = ["Alle Medien", "Bilder", "Videos", "Audio"];
    public IReadOnlyList<string> RatingFilters { get; } = ["Alle Bewertungen", "ab 1 ★", "ab 2 ★", "nur 3 ★"];
    public IReadOnlyList<string> SortOptions { get; } = ["Zuletzt gesehen", "Zuletzt hinzugefügt", "Aufnahmedatum neu–alt", "Aufnahmedatum alt–neu", "Dateiname A–Z", "Dateiname Z–A", "Größe absteigend", "Größe aufsteigend"];
    public IReadOnlyList<string> QuickKnowledgeKinds { get; } = ["Tag", "Person", "Ort", "Ereignis"];
    public IReadOnlyList<string> NavigationPanels { get; } = ["Ordner", "Timeline", "Sammlungen", "Projekte"];
    public IReadOnlyList<string> SmartCollectionTypeOptions { get; } = ["Alle Medien", "Bilder", "Videos", "Audio"];
    public IReadOnlyList<string> SmartCollectionRatingOptions { get; } = ["Keine Mindestbewertung", "ab 1 ★", "ab 2 ★", "nur 3 ★"];

    public int LoadedCount => Items.Count;
    public bool HasItems => Items.Count > 0;
    public bool IsListView => !IsGridView;
    public bool HasNoItems => !IsBusy && Items.Count == 0;
    public bool CanLoadMore => _lastPageWasFull;
    public bool HasSelection => SelectionCount > 0;
    public bool HasNoSelection => SelectionCount == 0;
    public bool HasSingleSelection => SelectionCount == 1 && SelectedMedia is not null;
    public bool HasMultipleSelection => SelectionCount > 1;
    public string SelectionText => SelectionCount switch
    {
        0 => "Keine Auswahl",
        1 => "1 Medium ausgewählt",
        _ => $"{SelectionCount:N0} Medien ausgewählt"
    };
    public bool IsFolderNavigationPanel => SelectedNavigationPanel == "Ordner";
    public bool IsTimelineNavigationPanel => SelectedNavigationPanel == "Timeline";
    public bool IsSmartCollectionsNavigationPanel => SelectedNavigationPanel == "Sammlungen";
    public bool IsProjectsNavigationPanel => SelectedNavigationPanel == "Projekte";


    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListView));
    }

    partial void OnSelectedNavigationPanelChanged(string value)
    {
        OnPropertyChanged(nameof(IsFolderNavigationPanel));
        OnPropertyChanged(nameof(IsTimelineNavigationPanel));
        OnPropertyChanged(nameof(IsSmartCollectionsNavigationPanel));
        OnPropertyChanged(nameof(IsProjectsNavigationPanel));
    }

    partial void OnSmartCollectionNameChanged(string value) =>
        SaveSmartCollectionCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SearchCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged();
        AddQuickKnowledgeCommand.NotifyCanExecuteChanged();
        SaveSmartCollectionCommand.NotifyCanExecuteChanged();
        SetRatingCommand.NotifyCanExecuteChanged();
        AssignProjectCommand.NotifyCanExecuteChanged();
        ExportSelectionCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasNoItems));
    }

    partial void OnItemsChanged(IReadOnlyList<MediaBrowserItemViewModel> value)
    {
        OnPropertyChanged(nameof(LoadedCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasNoItems));
        OnPropertyChanged(nameof(CanLoadMore));
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasNoSelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionText));
        AddQuickKnowledgeCommand.NotifyCanExecuteChanged();
        SetRatingCommand.NotifyCanExecuteChanged();
        AssignProjectCommand.NotifyCanExecuteChanged();
        ExportSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMediaChanged(MediaBrowserItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSingleSelection));
    }

    partial void OnQuickKnowledgeKindChanged(string value)
    {
        QuickKnowledgeText = string.Empty;
        _ = LoadQuickKnowledgeItemsAsync();
    }

    partial void OnQuickKnowledgeTextChanged(string value) =>
        AddQuickKnowledgeCommand.NotifyCanExecuteChanged();

    partial void OnSelectedQuickProjectChanged(QuickProjectOptionViewModel? value) =>
        AssignProjectCommand.NotifyCanExecuteChanged();

    public void SetSelection(IReadOnlyList<MediaBrowserItemViewModel> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);

        _selectedMediaIds = selected.Select(item => item.Id).Distinct(StringComparer.Ordinal).ToArray();
        _exportSelectionContext.SetSelection(_selectedMediaIds);
        SelectionCount = selected.Count;
        SelectedMedia = selected.Count == 1 ? selected[0] : null;

        var generation = ++_selectionGeneration;
        if (selected.Count == 1)
        {
            _ = LoadDetailsAsync(selected[0], generation);
            _ = LoadSelectedKnowledgeAsync(selected[0].Id, generation);
        }
        else
        {
            ClearDetails();
            SelectedKnowledgeSummary = selected.Count > 1 ? "Mehrfachauswahl" : "–";
            SelectedKnowledgeItems = Array.Empty<AssignedKnowledgeItemViewModel>();
            SelectedRatingText = selected.Count > 1 ? "Bewertung auf Auswahl anwenden" : "Unbewertet";
            DetailStatus = selected.Count > 1
                ? $"{selected.Count:N0} Medien ausgewählt. Schnellbearbeitung wirkt auf die gesamte Auswahl."
                : "Medium auswählen, um Details anzuzeigen.";
        }

        QuickKnowledgeStatus = selected.Count == 0
            ? "Auswahl treffen, um Personen, Orte, Tags oder Ereignisse zuzuweisen."
            : $"Schnellbearbeitung wirkt auf {selected.Count:N0} Medium/Medien.";
        QuickProjectStatus = selected.Count == 0
            ? "Auswahl treffen, um Medien einem Projekt zuzuweisen."
            : $"Projektzuordnung wirkt auf {selected.Count:N0} Medium/Medien.";
        _ = LoadQuickKnowledgeItemsAsync();
    }

    private void OpenExport()
    {
        if (!HasSelection) return;
        _exportSelectionContext.SetSelection(_selectedMediaIds);
        _navigationService.NavigateTo("export");
    }

    private async Task InitializeAsync()
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        await RefreshNavigationAsync();
        await RefreshAsync();
    }

    private bool CanApplyQuickKnowledge() =>
        HasSelection && !IsBusy && !string.IsNullOrWhiteSpace(QuickKnowledgeText);

    private MediaKnowledgeKind GetQuickKnowledgeKind() => QuickKnowledgeKind switch
    {
        "Person" => MediaKnowledgeKind.Person,
        "Ort" => MediaKnowledgeKind.Place,
        "Ereignis" => MediaKnowledgeKind.Event,
        _ => MediaKnowledgeKind.Tag
    };

    private async Task LoadQuickKnowledgeItemsAsync()
    {
        try
        {
            var items = await _mediaIndexService.GetKnowledgeItemsAsync(GetQuickKnowledgeKind(), limit: 12);
            QuickKnowledgeItems = items
                .Select(item => new QuickKnowledgeItemViewModel(item, ApplyQuickKnowledgeItemAsync))
                .ToArray();
        }
        catch
        {
            QuickKnowledgeItems = Array.Empty<QuickKnowledgeItemViewModel>();
        }
    }

    private async Task AddQuickKnowledgeAsync()
    {
        if (!CanApplyQuickKnowledge()) return;
        var name = QuickKnowledgeText.Trim();
        try
        {
            var item = await _mediaIndexService.EnsureKnowledgeItemAsync(
                new MediaKnowledgeDraft(GetQuickKnowledgeKind(), name));
            var added = await _mediaIndexService.AssignKnowledgeItemBatchAsync(_selectedMediaIds, item.Id);
            QuickKnowledgeText = string.Empty;
            QuickKnowledgeStatus = added == 0
                ? $"„{item.Name}“ war der Auswahl bereits zugeordnet."
                : $"„{item.Name}“ wurde {added:N0} Zuordnung(en) hinzugefügt.";
            await LoadQuickKnowledgeItemsAsync();
            await RefreshSelectedKnowledgeAsync();
        }
        catch (Exception exception)
        {
            QuickKnowledgeStatus = "Zuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private async Task ApplyQuickKnowledgeItemAsync(QuickKnowledgeItemViewModel item)
    {
        if (!HasSelection) return;
        try
        {
            var added = await _mediaIndexService.AssignKnowledgeItemBatchAsync(_selectedMediaIds, item.Id);
            QuickKnowledgeStatus = added == 0
                ? $"„{item.Name}“ war der Auswahl bereits zugeordnet."
                : $"„{item.Name}“ wurde {added:N0} Zuordnung(en) hinzugefügt.";
            await LoadQuickKnowledgeItemsAsync();
            await RefreshSelectedKnowledgeAsync();
        }
        catch (Exception exception)
        {
            QuickKnowledgeStatus = "Zuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private async Task SetRatingAsync(string? value)
    {
        if (!HasSelection || !int.TryParse(value, out var rating)) return;
        rating = Math.Clamp(rating, 0, 3);
        try
        {
            await _mediaIndexService.SetRatingBatchAsync(_selectedMediaIds, rating);
            foreach (var item in Items.Where(item => _selectedMediaIds.Contains(item.Id, StringComparer.Ordinal)))
                item.SetRating(rating);
            SelectedRatingText = rating == 0 ? "Unbewertet" : new string('★', rating);
            QuickKnowledgeStatus = rating == 0
                ? $"Bewertung für {SelectionCount:N0} Medium/Medien entfernt."
                : $"{SelectionCount:N0} Medium/Medien mit {rating} Stern(en) bewertet.";
        }
        catch (Exception exception)
        {
            QuickKnowledgeStatus = "Bewertung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private bool CanAssignProject() =>
        HasSelection && !IsBusy && SelectedQuickProject is not null;

    private async Task AssignProjectAsync()
    {
        if (!CanAssignProject() || SelectedQuickProject is null) return;

        var project = SelectedQuickProject;
        var added = 0;
        try
        {
            foreach (var mediaItemId in _selectedMediaIds)
            {
                if (await _projectService.AddMediaAsync(project.Id, mediaItemId))
                {
                    added++;
                }
            }

            QuickProjectStatus = added == 0
                ? $"Die Auswahl war „{project.Name}“ bereits zugeordnet."
                : $"{added:N0} Medium/Medien wurden „{project.Name}“ zugeordnet.";

            if (SelectedMedia is not null)
            {
                var names = await GetProjectNamesAsync(SelectedMedia.Id);
                SelectedProjects = names.Count == 0 ? "–" : string.Join(" · ", names);
            }
        }
        catch (Exception exception)
        {
            QuickProjectStatus = "Projektzuordnung fehlgeschlagen: " + exception.GetBaseException().Message;
        }
    }

    private async Task RemoveSelectedKnowledgeAsync(AssignedKnowledgeItemViewModel item)
    {
        if (SelectedMedia is null) return;

        try
        {
            var removed = await _mediaIndexService.RemoveKnowledgeItemBatchAsync([SelectedMedia.Id], item.Id);
            QuickKnowledgeStatus = removed == 0
                ? $"„{item.Name}“ war nicht mehr zugeordnet."
                : $"„{item.Name}“ wurde entfernt.";
            await RefreshSelectedKnowledgeAsync();
            await LoadQuickKnowledgeItemsAsync();
        }
        catch (Exception exception)
        {
            QuickKnowledgeStatus = "Zuordnung konnte nicht entfernt werden: " + exception.GetBaseException().Message;
        }
    }

    private async Task LoadSelectedKnowledgeAsync(string mediaItemId, int generation)
    {
        try
        {
            var snapshot = await _mediaIndexService.GetKnowledgeAsync(mediaItemId);
            if (generation != _selectionGeneration || SelectedMedia?.Id != mediaItemId) return;
            SelectedRatingText = snapshot.Rating == 0 ? "Unbewertet" : new string('★', snapshot.Rating);
            SelectedKnowledgeItems = snapshot.Items
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new AssignedKnowledgeItemViewModel(item, RemoveSelectedKnowledgeAsync))
                .ToArray();
            SelectedKnowledgeSummary = snapshot.Items.Count == 0
                ? "Noch keine Personen, Orte, Tags oder Ereignisse"
                : string.Join(" · ", snapshot.Items.Select(item => item.Name));
        }
        catch
        {
            if (generation == _selectionGeneration)
            {
                SelectedRatingText = "–";
                SelectedKnowledgeItems = Array.Empty<AssignedKnowledgeItemViewModel>();
                SelectedKnowledgeSummary = "Metadaten konnten nicht geladen werden";
            }
        }
    }

    private Task RefreshSelectedKnowledgeAsync()
    {
        if (SelectedMedia is null) return Task.CompletedTask;
        return LoadSelectedKnowledgeAsync(SelectedMedia.Id, _selectionGeneration);
    }

    private async Task RefreshNavigationAsync()
    {
        try
        {
            await LoadEndpointStateAsync(CancellationToken.None);
            var projects = await _projectService.GetProjectsAsync();
            var timelineYears = await _mediaIndexService.GetTimelineYearsAsync();

            _folderRoots.Clear();
            foreach (var endpoint in _endpointNames.OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase))
            {
                _folderRoots.Add(CreateFolderNode(
                    endpoint.Key,
                    endpoint.Value,
                    string.Empty,
                    0,
                    null,
                    hasChildren: true));
            }

            _timelineRoots.Clear();
            foreach (var year in timelineYears)
            {
                var start = new DateTime(year.Year, 1, 1);
                var end = start.AddYears(1).AddTicks(-1);
                _timelineRoots.Add(CreateTimelineNode(
                    year.Year,
                    null,
                    null,
                    year.Year.ToString(CultureInfo.InvariantCulture),
                    0,
                    year.MediaCount,
                    hasChildren: true,
                    start,
                    end));
            }

            var orderedProjects = projects
                .OrderBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            ProjectNavigationItems = orderedProjects
                .Select(project => new LibraryNavigationItemViewModel(
                    LibraryNavigationKind.Project, project.Name, project.Id, select: SelectNavigationAsync))
                .ToArray();
            QuickProjects = orderedProjects
                .Select(project => new QuickProjectOptionViewModel(project))
                .ToArray();
            if (SelectedQuickProject is not null)
            {
                SelectedQuickProject = QuickProjects.FirstOrDefault(project => project.Id == SelectedQuickProject.Id);
            }

            await RefreshSmartCollectionsAsync();
            await LoadSmartCollectionKnowledgeOptionsAsync();
            await RestoreFolderTreePositionAsync();
            await RestoreTimelinePositionAsync();
            RebuildVisibleFolderTree();
            RebuildVisibleTimeline();
            UpdateBreadcrumbs();
        }
        catch
        {
            _folderRoots.Clear();
            _timelineRoots.Clear();
            FolderTreeItems = Array.Empty<LibraryFolderTreeNodeViewModel>();
            TimelineItems = Array.Empty<LibraryTimelineTreeNodeViewModel>();
            ProjectNavigationItems = Array.Empty<LibraryNavigationItemViewModel>();
            QuickProjects = Array.Empty<QuickProjectOptionViewModel>();
            SelectedQuickProject = null;
            SmartCollections = Array.Empty<LibrarySmartCollectionItemViewModel>();
            UpdateBreadcrumbs();
        }
    }


    private async Task SelectNavigationPanelAsync(string? panel)
    {
        if (string.IsNullOrWhiteSpace(panel)) return;
        SelectedNavigationPanel = NavigationPanels.Contains(panel, StringComparer.Ordinal) ? panel : "Ordner";
        if (SelectedNavigationPanel == "Timeline" && _timelineRoots.Count == 0)
        {
            await RefreshNavigationAsync();
        }
        else if (SelectedNavigationPanel == "Sammlungen")
        {
            await RefreshSmartCollectionsAsync();
            if (SmartCollectionKnowledgeOptions.Count == 0)
                await LoadSmartCollectionKnowledgeOptionsAsync();
        }
    }

    private LibraryTimelineTreeNodeViewModel CreateTimelineNode(
        int year,
        int? month,
        int? day,
        string title,
        int depth,
        int count,
        bool hasChildren,
        DateTime rangeStart,
        DateTime rangeEnd) =>
        new(
            year,
            month,
            day,
            title,
            depth,
            count,
            hasChildren,
            rangeStart,
            rangeEnd,
            ToggleTimelineNodeAsync,
            SelectTimelineNodeAsync);

    private async Task ToggleTimelineNodeAsync(LibraryTimelineTreeNodeViewModel node)
    {
        if (!node.HasChildren || node.IsLoading) return;
        if (!node.IsExpanded) await EnsureTimelineChildrenLoadedAsync(node);
        node.IsExpanded = !node.IsExpanded;
        RebuildVisibleTimeline();
    }

    private async Task EnsureTimelineChildrenLoadedAsync(LibraryTimelineTreeNodeViewModel node)
    {
        if (node.ChildrenLoaded || !node.HasChildren) return;
        node.IsLoading = true;
        try
        {
            if (!node.Month.HasValue)
            {
                var months = await _mediaIndexService.GetTimelineMonthsAsync(node.Year);
                var culture = CultureInfo.GetCultureInfo("de-DE");
                node.Children = months.Select(month =>
                {
                    var start = new DateTime(month.Year, month.Month!.Value, 1);
                    var end = start.AddMonths(1).AddTicks(-1);
                    return CreateTimelineNode(
                        month.Year,
                        month.Month,
                        null,
                        culture.DateTimeFormat.GetMonthName(month.Month.Value),
                        node.Depth + 1,
                        month.MediaCount,
                        hasChildren: true,
                        start,
                        end);
                }).ToArray();
            }
            else if (!node.Day.HasValue)
            {
                var days = await _mediaIndexService.GetTimelineDaysAsync(node.Year, node.Month.Value);
                node.Children = days.Select(day =>
                {
                    var start = new DateTime(day.Year, day.Month!.Value, day.Day!.Value);
                    return CreateTimelineNode(
                        day.Year,
                        day.Month,
                        day.Day,
                        day.Day.Value.ToString("00", CultureInfo.InvariantCulture) + ".",
                        node.Depth + 1,
                        day.MediaCount,
                        hasChildren: false,
                        start,
                        start.AddDays(1).AddTicks(-1));
                }).ToArray();
            }
            node.ChildrenLoaded = true;
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private async Task SelectTimelineNodeAsync(LibraryTimelineTreeNodeViewModel node)
    {
        ClearNavigationState();
        ClearTreeSelection();
        ClearTimelineSelection();
        UpdateNavigationItemSelection(null);
        node.IsSelected = true;
        _navigationCaptureDateFrom = node.RangeStart;
        _navigationCaptureDateTo = node.RangeEnd;
        _navigationTimelineYear = node.Year;
        _navigationTimelineMonth = node.Month;
        _navigationTimelineDay = node.Day;
        SelectedSort = "Aufnahmedatum neu–alt";
        NavigationTitle = BuildTimelineNavigationTitle(node.Year, node.Month, node.Day);
        RebuildVisibleTimeline();
        UpdateBreadcrumbs();
        IsNavigationOpen = false;
        await RefreshAsync();
    }

    private static string BuildTimelineNavigationTitle(int year, int? month, int? day)
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        if (!month.HasValue) return $"Timeline · {year}";
        var monthName = culture.DateTimeFormat.GetMonthName(month.Value);
        return !day.HasValue
            ? $"Timeline · {monthName} {year}"
            : $"Timeline · {day.Value:00}. {monthName} {year}";
    }

    private async Task RestoreTimelinePositionAsync()
    {
        ClearTimelineSelection();
        if (!_navigationTimelineYear.HasValue) return;
        var year = _timelineRoots.FirstOrDefault(node => node.Year == _navigationTimelineYear.Value);
        if (year is null) return;
        if (!_navigationTimelineMonth.HasValue)
        {
            year.IsSelected = true;
            return;
        }

        year.IsExpanded = true;
        await EnsureTimelineChildrenLoadedAsync(year);
        var month = year.Children.FirstOrDefault(node => node.Month == _navigationTimelineMonth.Value);
        if (month is null) return;
        if (!_navigationTimelineDay.HasValue)
        {
            month.IsSelected = true;
            return;
        }

        month.IsExpanded = true;
        await EnsureTimelineChildrenLoadedAsync(month);
        var day = month.Children.FirstOrDefault(node => node.Day == _navigationTimelineDay.Value);
        if (day is not null) day.IsSelected = true;
    }

    private void RebuildVisibleTimeline()
    {
        var visible = new List<LibraryTimelineTreeNodeViewModel>();
        foreach (var root in _timelineRoots) AddVisibleTimelineNode(root, visible);
        TimelineItems = visible;
    }

    private static void AddVisibleTimelineNode(
        LibraryTimelineTreeNodeViewModel node,
        List<LibraryTimelineTreeNodeViewModel> target)
    {
        target.Add(node);
        if (!node.IsExpanded) return;
        foreach (var child in node.Children) AddVisibleTimelineNode(child, target);
    }

    private void ClearTimelineSelection()
    {
        foreach (var root in _timelineRoots) ClearTimelineSelection(root);
    }

    private static void ClearTimelineSelection(LibraryTimelineTreeNodeViewModel node)
    {
        node.IsSelected = false;
        foreach (var child in node.Children) ClearTimelineSelection(child);
    }

    private async Task RefreshSmartCollectionsAsync()
    {
        var collections = await _mediaIndexService.GetSmartCollectionsAsync();
        SmartCollections = collections
            .Select(collection => new LibrarySmartCollectionItemViewModel(
                collection,
                OpenSmartCollectionAsync,
                EditSmartCollectionAsync,
                DeleteSmartCollectionAsync))
            .ToArray();
        foreach (var item in SmartCollections)
            item.IsSelected = string.Equals(item.Id, _navigationSmartCollectionId, StringComparison.Ordinal);
    }

    private async Task LoadSmartCollectionKnowledgeOptionsAsync()
    {
        var kinds = new[]
        {
            MediaKnowledgeKind.Person,
            MediaKnowledgeKind.Place,
            MediaKnowledgeKind.Tag,
            MediaKnowledgeKind.Event
        };
        var result = new List<SmartCollectionKnowledgeOptionViewModel>();
        foreach (var kind in kinds)
        {
            var items = await _mediaIndexService.GetKnowledgeItemsAsync(kind, limit: 60);
            result.AddRange(items.Select(item => new SmartCollectionKnowledgeOptionViewModel(item)));
        }
        SmartCollectionKnowledgeOptions = result.ToArray();
    }

    private async Task OpenSmartCollectionAsync(LibrarySmartCollectionItemViewModel item)
    {
        var collection = item.Collection;
        ClearNavigationState();
        ClearTreeSelection();
        ClearTimelineSelection();
        UpdateNavigationItemSelection(null);
        _navigationSmartCollectionId = collection.Id;
        _navigationEndpointId = collection.EndpointId;
        _navigationRelativeFolder = collection.RelativeFolder;
        _navigationCaptureDateFrom = collection.CaptureDateFrom;
        _navigationCaptureDateTo = collection.CaptureDateTo;
        _navigationKnowledgeItemIds = collection.KnowledgeItemIds;
        SearchText = collection.SearchText ?? string.Empty;
        SelectedTypeFilter = collection.MediaType switch
        {
            MediaFileType.Image => "Bilder",
            MediaFileType.Video => "Videos",
            MediaFileType.Audio => "Audio",
            _ => "Alle Medien"
        };
        SelectedRatingFilter = collection.MinimumRating switch
        {
            1 => "ab 1 ★",
            2 => "ab 2 ★",
            3 => "nur 3 ★",
            _ => "Alle Bewertungen"
        };
        NavigationTitle = "Sammlung · " + collection.Name;
        foreach (var candidate in SmartCollections)
            candidate.IsSelected = ReferenceEquals(candidate, item);
        UpdateBreadcrumbs();
        IsNavigationOpen = false;
        await RefreshAsync();
    }

    private Task EditSmartCollectionAsync(LibrarySmartCollectionItemViewModel item)
    {
        var collection = item.Collection;
        _editingSmartCollectionId = collection.Id;
        _smartCollectionEditorEndpointId = collection.EndpointId;
        _smartCollectionEditorRelativeFolder = collection.RelativeFolder;
        SmartCollectionScopeText = BuildSmartCollectionScopeText(collection.EndpointId, collection.RelativeFolder);
        SmartCollectionName = collection.Name;
        SmartCollectionSearchText = collection.SearchText ?? string.Empty;
        SmartCollectionType = collection.MediaType switch
        {
            MediaFileType.Image => "Bilder",
            MediaFileType.Video => "Videos",
            MediaFileType.Audio => "Audio",
            _ => "Alle Medien"
        };
        SmartCollectionRating = collection.MinimumRating switch
        {
            1 => "ab 1 ★",
            2 => "ab 2 ★",
            3 => "nur 3 ★",
            _ => "Keine Mindestbewertung"
        };
        SmartCollectionFromText = collection.CaptureDateFrom?.ToString("dd.MM.yyyy") ?? string.Empty;
        SmartCollectionToText = collection.CaptureDateTo?.ToString("dd.MM.yyyy") ?? string.Empty;
        var selectedIds = collection.KnowledgeItemIds.ToHashSet(StringComparer.Ordinal);
        foreach (var option in SmartCollectionKnowledgeOptions)
            option.IsSelected = selectedIds.Contains(option.Id);
        SmartCollectionStatus = $"„{collection.Name}“ wird bearbeitet.";
        return Task.CompletedTask;
    }

    private async Task DeleteSmartCollectionAsync(LibrarySmartCollectionItemViewModel item)
    {
        await _mediaIndexService.DeleteSmartCollectionAsync(item.Id);
        if (string.Equals(_editingSmartCollectionId, item.Id, StringComparison.Ordinal)) NewSmartCollection();
        if (string.Equals(_navigationSmartCollectionId, item.Id, StringComparison.Ordinal))
        {
            await ShowAllMediaAsync();
        }
        await RefreshSmartCollectionsAsync();
        SmartCollectionStatus = $"Sammlung „{item.Name}“ gelöscht. Medien wurden nicht verändert.";
    }

    private void NewSmartCollection()
    {
        _editingSmartCollectionId = null;
        _smartCollectionEditorEndpointId = null;
        _smartCollectionEditorRelativeFolder = null;
        SmartCollectionScopeText = "Gesamte Bibliothek";
        SmartCollectionName = string.Empty;
        SmartCollectionSearchText = string.Empty;
        SmartCollectionType = "Alle Medien";
        SmartCollectionRating = "Keine Mindestbewertung";
        SmartCollectionFromText = string.Empty;
        SmartCollectionToText = string.Empty;
        foreach (var option in SmartCollectionKnowledgeOptions) option.IsSelected = false;
        SmartCollectionStatus = "Neue dynamische Sammlung anlegen.";
    }

    private void UseCurrentViewForSmartCollection()
    {
        _editingSmartCollectionId = null;
        _smartCollectionEditorEndpointId = _navigationEndpointId;
        _smartCollectionEditorRelativeFolder = _navigationRelativeFolder;
        SmartCollectionScopeText = BuildSmartCollectionScopeText(_navigationEndpointId, _navigationRelativeFolder);
        SmartCollectionSearchText = SearchText;
        var type = _navigationMediaType ?? GetSelectedMediaType();
        SmartCollectionType = type switch
        {
            MediaFileType.Image => "Bilder",
            MediaFileType.Video => "Videos",
            MediaFileType.Audio => "Audio",
            _ => "Alle Medien"
        };
        SmartCollectionRating = GetMinimumRating() switch
        {
            1 => "ab 1 ★",
            2 => "ab 2 ★",
            3 => "nur 3 ★",
            _ => "Keine Mindestbewertung"
        };
        SmartCollectionFromText = _navigationCaptureDateFrom?.ToString("dd.MM.yyyy") ?? string.Empty;
        SmartCollectionToText = _navigationCaptureDateTo?.ToString("dd.MM.yyyy") ?? string.Empty;
        var knowledgeIds = _navigationKnowledgeItemIds.ToHashSet(StringComparer.Ordinal);
        foreach (var option in SmartCollectionKnowledgeOptions)
            option.IsSelected = knowledgeIds.Contains(option.Id);
        SmartCollectionStatus = !string.IsNullOrWhiteSpace(_navigationProjectId)
            ? "Aktuelle Filter übernommen. Die feste Projektzuordnung selbst ist bewusst kein Smart-Collection-Kriterium."
            : "Aktuelle Bibliotheksansicht übernommen. Nur noch einen Namen vergeben und speichern.";
    }

    private string BuildSmartCollectionScopeText(string? endpointId, string? relativeFolder)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return "Gesamte Bibliothek";
        var endpointName = _endpointNames.TryGetValue(endpointId, out var name) ? name : endpointId;
        return string.IsNullOrWhiteSpace(relativeFolder)
            ? endpointName
            : endpointName + " › " + relativeFolder.Replace("/", " › ");
    }

    private bool CanSaveSmartCollection() => !IsBusy && !string.IsNullOrWhiteSpace(SmartCollectionName);

    private async Task SaveSmartCollectionAsync()
    {
        if (!CanSaveSmartCollection()) return;
        if (!TryParseOptionalDate(SmartCollectionFromText, out var from, out var fromError))
        {
            SmartCollectionStatus = fromError ?? "Ungültiges Startdatum.";
            return;
        }
        if (!TryParseOptionalDate(SmartCollectionToText, out var to, out var toError))
        {
            SmartCollectionStatus = toError ?? "Ungültiges Enddatum.";
            return;
        }
        if (from.HasValue && to.HasValue && to.Value.Date < from.Value.Date)
        {
            SmartCollectionStatus = "Das Enddatum darf nicht vor dem Startdatum liegen.";
            return;
        }

        var draft = new MediaSmartCollectionDraft(
            _editingSmartCollectionId,
            SmartCollectionName.Trim(),
            string.IsNullOrWhiteSpace(SmartCollectionSearchText) ? null : SmartCollectionSearchText.Trim(),
            SmartCollectionType switch
            {
                "Bilder" => MediaFileType.Image,
                "Videos" => MediaFileType.Video,
                "Audio" => MediaFileType.Audio,
                _ => null
            },
            _smartCollectionEditorEndpointId,
            _smartCollectionEditorRelativeFolder,
            from?.Date,
            to?.Date.AddDays(1).AddTicks(-1),
            SmartCollectionRating switch
            {
                "ab 1 ★" => 1,
                "ab 2 ★" => 2,
                "nur 3 ★" => 3,
                _ => null
            },
            SmartCollectionKnowledgeOptions.Where(option => option.IsSelected).Select(option => option.Id).ToArray());

        try
        {
            var saved = await _mediaIndexService.SaveSmartCollectionAsync(draft);
            _editingSmartCollectionId = saved.Id;
            SmartCollectionStatus = $"Sammlung „{saved.Name}“ gespeichert. Sie wird dynamisch aus dem Index berechnet.";
            await RefreshSmartCollectionsAsync();
        }
        catch (Exception exception)
        {
            SmartCollectionStatus = "Sammlung konnte nicht gespeichert werden: " + exception.GetBaseException().Message;
        }
    }

    private static bool TryParseOptionalDate(string? text, out DateTime? value, out string? error)
    {
        value = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (DateTime.TryParseExact(text.Trim(), "dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out var parsed)
            || DateTime.TryParse(text.Trim(), CultureInfo.GetCultureInfo("de-DE"), DateTimeStyles.None, out parsed))
        {
            value = parsed.Date;
            return true;
        }
        error = $"Datum „{text.Trim()}“ ist ungültig. Erwartet wird z. B. 03.08.2026.";
        return false;
    }

    private async Task ToggleNavigationAsync()
    {
        if (IsNavigationOpen)
        {
            IsNavigationOpen = false;
            return;
        }

        if (_folderRoots.Count == 0)
        {
            await RefreshNavigationAsync();
        }
        else
        {
            await RestoreFolderTreePositionAsync();
            await RestoreTimelinePositionAsync();
            RebuildVisibleFolderTree();
            RebuildVisibleTimeline();
        }

        IsNavigationOpen = true;
    }

    private async Task ShowAllMediaAsync()
    {
        ClearNavigationState();
        NavigationTitle = "Alle Medien";
        ClearTreeSelection();
        ClearTimelineSelection();
        UpdateNavigationItemSelection(null);
        UpdateBreadcrumbs();
        IsNavigationOpen = false;
        await RefreshAsync();
    }

    private async Task SelectNavigationAsync(LibraryNavigationItemViewModel? item)
    {
        ClearNavigationState();
        ClearTreeSelection();
        ClearTimelineSelection();
        UpdateNavigationItemSelection(item);

        if (item is null || item.Kind == LibraryNavigationKind.All)
        {
            NavigationTitle = "Alle Medien";
        }
        else
        {
            switch (item.Kind)
            {
                case LibraryNavigationKind.Project:
                    _navigationProjectId = item.Key;
                    NavigationTitle = "Projekt · " + item.Title;
                    break;
                case LibraryNavigationKind.MediaType:
                    _navigationMediaType = item.Key switch
                    {
                        "image" => MediaFileType.Image,
                        "video" => MediaFileType.Video,
                        "audio" => MediaFileType.Audio,
                        _ => null
                    };
                    NavigationTitle = item.Title;
                    break;
            }
        }

        UpdateBreadcrumbs();
        IsNavigationOpen = false;
        await RefreshAsync();
    }

    private LibraryFolderTreeNodeViewModel CreateFolderNode(
        string endpointId,
        string title,
        string relativeFolder,
        int depth,
        int? count,
        bool hasChildren) =>
        new(
            endpointId,
            title,
            relativeFolder,
            depth,
            count,
            hasChildren,
            ToggleFolderNodeAsync,
            SelectFolderNodeAsync);

    private async Task ToggleFolderNodeAsync(LibraryFolderTreeNodeViewModel node)
    {
        if (!node.HasChildren || node.IsLoading)
        {
            return;
        }

        if (!node.IsExpanded)
        {
            await EnsureFolderChildrenLoadedAsync(node);
        }

        node.IsExpanded = !node.IsExpanded;
        RebuildVisibleFolderTree();
    }

    private async Task EnsureFolderChildrenLoadedAsync(LibraryFolderTreeNodeViewModel node)
    {
        if (node.ChildrenLoaded || !node.HasChildren)
        {
            return;
        }

        node.IsLoading = true;
        try
        {
            var children = await _mediaIndexService.GetFolderChildrenAsync(
                node.EndpointId,
                string.IsNullOrWhiteSpace(node.RelativeFolder) ? null : node.RelativeFolder);

            node.Children = children
                .Select(child => CreateFolderNode(
                    child.EndpointId,
                    child.Name,
                    child.RelativeFolder,
                    node.Depth + 1,
                    child.MediaCount,
                    child.HasChildren))
                .ToArray();
            node.ChildrenLoaded = true;
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private async Task SelectFolderNodeAsync(LibraryFolderTreeNodeViewModel node)
    {
        ClearNavigationState();
        UpdateNavigationItemSelection(null);
        ClearTreeSelection();
        ClearTimelineSelection();

        node.IsSelected = true;
        _navigationEndpointId = node.EndpointId;
        _navigationRelativeFolder = string.IsNullOrWhiteSpace(node.RelativeFolder) ? null : node.RelativeFolder;
        NavigationTitle = BuildFolderNavigationTitle(node.EndpointId, node.RelativeFolder);
        UpdateBreadcrumbs();
        RebuildVisibleFolderTree();
        IsNavigationOpen = false;
        await RefreshAsync();
    }

    private async Task NavigateFolderBreadcrumbAsync(string endpointId, string? relativeFolder)
    {
        ClearNavigationState();
        UpdateNavigationItemSelection(null);
        ClearTimelineSelection();
        _navigationEndpointId = endpointId;
        _navigationRelativeFolder = string.IsNullOrWhiteSpace(relativeFolder) ? null : relativeFolder;
        NavigationTitle = BuildFolderNavigationTitle(endpointId, relativeFolder ?? string.Empty);
        await RestoreFolderTreePositionAsync();
        RebuildVisibleFolderTree();
        UpdateBreadcrumbs();
        await RefreshAsync();
    }

    private async Task NavigateTimelineBreadcrumbAsync(int year, int? month, int? day)
    {
        var start = day.HasValue
            ? new DateTime(year, month!.Value, day.Value)
            : month.HasValue
                ? new DateTime(year, month.Value, 1)
                : new DateTime(year, 1, 1);
        var end = day.HasValue
            ? start.AddDays(1).AddTicks(-1)
            : month.HasValue
                ? start.AddMonths(1).AddTicks(-1)
                : start.AddYears(1).AddTicks(-1);

        ClearNavigationState();
        ClearTreeSelection();
        UpdateNavigationItemSelection(null);
        _navigationCaptureDateFrom = start;
        _navigationCaptureDateTo = end;
        _navigationTimelineYear = year;
        _navigationTimelineMonth = month;
        _navigationTimelineDay = day;
        SelectedSort = "Aufnahmedatum neu–alt";
        NavigationTitle = BuildTimelineNavigationTitle(year, month, day);
        await RestoreTimelinePositionAsync();
        RebuildVisibleTimeline();
        UpdateBreadcrumbs();
        await RefreshAsync();
    }

    private async Task RestoreFolderTreePositionAsync()
    {
        ClearTreeSelection();
        if (string.IsNullOrWhiteSpace(_navigationEndpointId))
        {
            return;
        }

        var root = _folderRoots.FirstOrDefault(node =>
            string.Equals(node.EndpointId, _navigationEndpointId, StringComparison.OrdinalIgnoreCase));
        if (root is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_navigationRelativeFolder))
        {
            root.IsSelected = true;
            return;
        }

        var parts = _navigationRelativeFolder
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        current.IsExpanded = true;
        await EnsureFolderChildrenLoadedAsync(current);
        var path = string.Empty;

        foreach (var part in parts)
        {
            path = path.Length == 0 ? part : path + "/" + part;
            var child = current.Children.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativeFolder, path, StringComparison.OrdinalIgnoreCase));
            if (child is null)
            {
                break;
            }

            current = child;
            if (!string.Equals(path, _navigationRelativeFolder, StringComparison.OrdinalIgnoreCase))
            {
                current.IsExpanded = true;
                await EnsureFolderChildrenLoadedAsync(current);
            }
        }

        if (string.Equals(current.RelativeFolder, _navigationRelativeFolder, StringComparison.OrdinalIgnoreCase))
        {
            current.IsSelected = true;
        }
    }

    private void RebuildVisibleFolderTree()
    {
        var visible = new List<LibraryFolderTreeNodeViewModel>();
        foreach (var root in _folderRoots)
        {
            AddVisibleNode(root, visible);
        }
        FolderTreeItems = visible;
    }

    private static void AddVisibleNode(
        LibraryFolderTreeNodeViewModel node,
        List<LibraryFolderTreeNodeViewModel> target)
    {
        target.Add(node);
        if (!node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AddVisibleNode(child, target);
        }
    }

    private void ClearTreeSelection()
    {
        foreach (var root in _folderRoots)
        {
            ClearTreeSelection(root);
        }
    }

    private static void ClearTreeSelection(LibraryFolderTreeNodeViewModel node)
    {
        node.IsSelected = false;
        foreach (var child in node.Children)
        {
            ClearTreeSelection(child);
        }
    }

    private void UpdateNavigationItemSelection(LibraryNavigationItemViewModel? item)
    {
        foreach (var candidate in ProjectNavigationItems.Concat(TypeNavigationItems))
        {
            candidate.IsSelected = ReferenceEquals(candidate, item);
        }
    }

    private void ClearNavigationState()
    {
        _navigationEndpointId = null;
        _navigationRelativeFolder = null;
        _navigationProjectId = null;
        _navigationMediaType = null;
        _navigationCaptureDateFrom = null;
        _navigationCaptureDateTo = null;
        _navigationKnowledgeItemIds = Array.Empty<string>();
        _navigationSmartCollectionId = null;
        _navigationTimelineYear = null;
        _navigationTimelineMonth = null;
        _navigationTimelineDay = null;
        foreach (var collection in SmartCollections) collection.IsSelected = false;
    }

    private string BuildFolderNavigationTitle(string endpointId, string relativeFolder)
    {
        var endpointTitle = _endpointNames.TryGetValue(endpointId, out var name) ? name : endpointId;
        if (string.IsNullOrWhiteSpace(relativeFolder))
        {
            return endpointTitle;
        }

        return endpointTitle + " › " + relativeFolder.Replace("/", " › ");
    }

    private void UpdateBreadcrumbs()
    {
        var crumbs = new List<(string Title, Func<Task> Navigate)>();
        crumbs.Add(("Alle Medien", ShowAllMediaAsync));

        if (!string.IsNullOrWhiteSpace(_navigationSmartCollectionId))
        {
            crumbs.Add((NavigationTitle, () => SelectNavigationPanelAndOpenAsync("Sammlungen")));
        }
        else if (!string.IsNullOrWhiteSpace(_navigationEndpointId))
        {
            var endpointId = _navigationEndpointId;
            var endpointTitle = _endpointNames.TryGetValue(endpointId, out var name) ? name : endpointId;
            crumbs.Add((endpointTitle, () => NavigateFolderBreadcrumbAsync(endpointId, null)));

            if (!string.IsNullOrWhiteSpace(_navigationRelativeFolder))
            {
                var parts = _navigationRelativeFolder.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = string.Empty;
                foreach (var part in parts)
                {
                    current = current.Length == 0 ? part : current + "/" + part;
                    var target = current;
                    crumbs.Add((part, () => NavigateFolderBreadcrumbAsync(endpointId, target)));
                }
            }
        }
        else if (_navigationTimelineYear.HasValue)
        {
            var year = _navigationTimelineYear.Value;
            crumbs.Add(("Timeline", () => SelectNavigationPanelAndOpenAsync("Timeline")));
            crumbs.Add((year.ToString(CultureInfo.InvariantCulture), () => NavigateTimelineBreadcrumbAsync(year, null, null)));
            if (_navigationTimelineMonth.HasValue)
            {
                var month = _navigationTimelineMonth.Value;
                var monthName = CultureInfo.GetCultureInfo("de-DE").DateTimeFormat.GetMonthName(month);
                crumbs.Add((monthName, () => NavigateTimelineBreadcrumbAsync(year, month, null)));
                if (_navigationTimelineDay.HasValue)
                {
                    var day = _navigationTimelineDay.Value;
                    crumbs.Add(($"{day:00}.", () => NavigateTimelineBreadcrumbAsync(year, month, day)));
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(_navigationProjectId))
        {
            crumbs.Add((NavigationTitle, () => SelectNavigationPanelAndOpenAsync("Projekte")));
        }
        else if (_navigationMediaType.HasValue)
        {
            crumbs.Add((NavigationTitle, () => SelectNavigationPanelAndOpenAsync("Projekte")));
        }

        NavigationBreadcrumbs = crumbs
            .Select((crumb, index) => new LibraryBreadcrumbItemViewModel(
                crumb.Title,
                isCurrent: index == crumbs.Count - 1,
                showSeparator: index > 0,
                crumb.Navigate))
            .ToArray();
    }

    private async Task SelectNavigationPanelAndOpenAsync(string panel)
    {
        await SelectNavigationPanelAsync(panel);
        IsNavigationOpen = true;
    }

    private async Task RefreshAsync()
    {
        _loadCancellation?.Cancel();
        _offset = 0;
        _lastPageWasFull = false;
        DisposeItems();
        Items = Array.Empty<MediaBrowserItemViewModel>();
        SetSelection(Array.Empty<MediaBrowserItemViewModel>());
        await LoadPageAsync(replace: true);
    }

    private async Task LoadMoreAsync()
    {
        if (!CanLoadMore)
        {
            return;
        }

        await LoadPageAsync(replace: false);
    }

    private async Task LoadPageAsync(bool replace)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = replace ? "Bibliothek wird geladen …" : "Weitere Medien werden geladen …";

        if (replace)
        {
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = new CancellationTokenSource();
        }

        var token = _loadCancellation?.Token ?? CancellationToken.None;

        try
        {
            await LoadEndpointStateAsync(token);

            IReadOnlyList<MediaIndexSearchResult> results;
            if (!string.IsNullOrWhiteSpace(_navigationProjectId))
            {
                results = await LoadProjectResultsAsync(_navigationProjectId, token);
            }
            else
            {
                var query = new MediaIndexSearchQuery(
                    SearchText: SearchText,
                    PresentOnly: true,
                    HistoricalOnly: false,
                    Limit: PageSize,
                    Offset: _offset,
                    MediaType: _navigationMediaType ?? GetSelectedMediaType(),
                    SortOrder: GetSelectedSortOrder(),
                    EndpointId: _navigationEndpointId,
                    RelativeFolder: _navigationRelativeFolder,
                    CaptureDateFrom: _navigationCaptureDateFrom,
                    CaptureDateTo: _navigationCaptureDateTo,
                    MinimumRating: GetMinimumRating(),
                    KnowledgeItemIds: _navigationKnowledgeItemIds);

                results = await _mediaIndexService.SearchAsync(query, token);
            }
            var page = results
                .Select(result => new MediaBrowserItemViewModel(result, _endpointNames))
                .ToArray();

            _lastPageWasFull = page.Length == PageSize;
            _offset += page.Length;

            if (replace)
            {
                _items.Clear();
            }

            _items.AddRange(page);
            Items = _items.ToArray();
            OnPropertyChanged(nameof(CanLoadMore));
            LoadMoreCommand.NotifyCanExecuteChanged();

            StatusMessage = Items.Count == 0
                ? $"{NavigationTitle}: keine aktuellen Medien gefunden."
                : $"{NavigationTitle} · {Items.Count:N0} Medien geladen" + (_lastPageWasFull ? " · weitere verfügbar" : string.Empty);

            _ = LoadGridThumbnailsAsync(page, token);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Laden abgebrochen.";
        }
        catch (Exception exception)
        {
            StatusMessage = "Bibliothek konnte nicht geladen werden: " + exception.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IReadOnlyList<MediaIndexSearchResult>> LoadProjectResultsAsync(
        string projectId,
        CancellationToken cancellationToken)
    {
        var references = (await _projectService.GetMediaReferencesAsync(projectId, cancellationToken))
            .OrderByDescending(reference => reference.AddedAtUtc)
            .ToArray();
        var matches = new List<MediaIndexSearchResult>();
        var skipped = 0;
        var wantedType = _navigationMediaType ?? GetSelectedMediaType();
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = await _mediaIndexService.GetItemAsync(reference.MediaItemId, cancellationToken);
            if (item is null || (wantedType.HasValue && item.MediaType != wantedType.Value))
            {
                continue;
            }

            var locations = await _mediaIndexService.GetLocationsAsync(item.Id, cancellationToken);
            var representative = locations
                .OrderByDescending(location => location.IsPresent)
                .ThenByDescending(location => location.LastSeenUtc)
                .FirstOrDefault();
            if (representative is null || !representative.IsPresent)
            {
                continue;
            }

            var metadata = await _mediaIndexService.GetMetadataAsync(item.Id, cancellationToken);
            var facts = await _mediaIndexService.GetFactsAsync(item.Id, cancellationToken);
            var knowledge = await _mediaIndexService.GetKnowledgeAsync(item.Id, cancellationToken);
            var minimumRating = GetMinimumRating();
            if (minimumRating.HasValue && knowledge.Rating < minimumRating.Value)
            {
                continue;
            }

            if (search is not null
                && !item.Id.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                && !representative.RelativePath.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                && !(metadata.Title?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
                && !(metadata.Description?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false))
            {
                continue;
            }

            if (skipped++ < _offset)
            {
                continue;
            }

            matches.Add(new MediaIndexSearchResult(
                item,
                representative.EndpointId,
                representative.RelativePath,
                representative.IsPresent,
                locations.Count(location => location.IsPresent),
                locations.Count(location => !location.IsPresent),
                metadata.TitleOverridden ? metadata.Title : null,
                metadata.CaptureDateOverridden ? metadata.CaptureDate : facts?.CaptureDate,
                metadata.DescriptionOverridden ? metadata.Description : null,
                knowledge.Rating));

            if (matches.Count >= PageSize)
            {
                break;
            }
        }

        return matches;
    }

    private async Task LoadEndpointStateAsync(CancellationToken cancellationToken)
    {
        var settings = await _storageSettingsStore.LoadAsync(cancellationToken);
        _endpointNames = settings.Endpoints
            .GroupBy(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);

        var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in settings.Endpoints.Where(endpoint => endpoint.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = _storageLocationResolver.Resolve(endpoint.Location);
            if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                continue;
            }

            roots[endpoint.Id] = Path.GetFullPath(resolution.ResolvedPath);
        }

        _endpointRoots = roots;
    }

    private async Task LoadGridThumbnailsAsync(
        IReadOnlyList<MediaBrowserItemViewModel> page,
        CancellationToken cancellationToken)
    {
        foreach (var item in page)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!item.IsImage)
            {
                item.SetThumbnailUnavailable(item.MediaType);
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
                    item.Id,
                    file,
                    _thumbnailOptions.GridWidth,
                    cancellationToken);

                if (result.IsSuccessful && result.EncodedImage is { Length: > 0 })
                {
                    using var stream = new MemoryStream(result.EncodedImage, writable: false);
                    item.SetThumbnail(new Bitmap(stream));
                }
                else
                {
                    item.SetThumbnailUnavailable("Keine Vorschau");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                item.SetThumbnailUnavailable("Keine Vorschau");
            }
        }
    }

    private async Task LoadDetailsAsync(MediaBrowserItemViewModel item, int generation)
    {
        DetailStatus = "Details werden geladen …";
        SelectedPreview?.Dispose();
        SelectedPreview = null;
        SelectedTitle = item.DisplayTitle;
        SelectedSubTitle = item.RelativePath;
        SelectedMediaId = item.Id;
        SelectedFileName = item.FileName;
        SelectedType = item.MediaType;
        SelectedSize = item.SizeText;
        SelectedCaptureDate = "–";
        SelectedCamera = "–";
        SelectedDimensions = "–";
        SelectedGps = "–";
        SelectedDescription = string.Empty;
        SelectedProjects = "–";
        SelectedLocations = Array.Empty<MediaIndexLocationItemViewModel>();

        try
        {
            var metadataTask = _mediaIndexService.GetMetadataAsync(item.Id);
            var locationsTask = _mediaIndexService.GetLocationsAsync(item.Id);
            var projectsTask = GetProjectNamesAsync(item.Id);
            var file = TryResolveMediaFile(item.Result);
            var originalMetadataTask = file is not null && item.IsImage
                ? _metadataReader.ReadAsync(file)
                : Task.FromResult(new ImageMetadataReadResult(ImageMetadataReadState.NotImage));

            var metadata = await metadataTask;
            var locations = await locationsTask;
            var projects = await projectsTask;
            var original = await originalMetadataTask;

            if (generation != _selectionGeneration || SelectedMedia?.Id != item.Id)
            {
                return;
            }

            var originalMetadata = original.IsSuccessful ? original.Metadata : null;
            var effectiveCapture = metadata.CaptureDateOverridden
                ? metadata.CaptureDate
                : originalMetadata?.CapturedAt;
            SelectedCaptureDate = effectiveCapture?.ToString("dd.MM.yyyy HH:mm:ss") ?? "–";
            var camera = string.Join(
                " ",
                new[] { originalMetadata?.CameraMake, originalMetadata?.CameraModel }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            SelectedCamera = string.IsNullOrWhiteSpace(camera) ? "–" : camera;
            SelectedDimensions = originalMetadata?.HasDimensions == true
                ? $"{originalMetadata.PixelWidth:N0} × {originalMetadata.PixelHeight:N0}"
                : "–";

            SelectedGps = metadata.GpsMode switch
            {
                MetadataGpsMode.Set when metadata.Latitude.HasValue && metadata.Longitude.HasValue => $"{metadata.Latitude:F6}, {metadata.Longitude:F6} · Elbwald",
                MetadataGpsMode.Remove => "entfernt · Elbwald",
                _ when originalMetadata?.HasGps == true && originalMetadata.Latitude.HasValue && originalMetadata.Longitude.HasValue => $"{originalMetadata.Latitude:F6}, {originalMetadata.Longitude:F6}",
                _ => "–"
            };

            SelectedTitle = metadata.TitleOverridden && !string.IsNullOrWhiteSpace(metadata.Title)
                ? metadata.Title
                : item.DisplayTitle;
            SelectedDescription = metadata.DescriptionOverridden
                ? metadata.Description ?? string.Empty
                : string.Empty;
            SelectedProjects = projects.Count == 0 ? "–" : string.Join(" · ", projects);
            SelectedLocations = locations
                .OrderByDescending(location => location.IsPresent)
                .ThenByDescending(location => location.LastSeenUtc)
                .Select(location => new MediaIndexLocationItemViewModel(location, _endpointNames))
                .ToArray();

            if (file is not null && item.IsImage)
            {
                var preview = await _thumbnailService.GetIndexedThumbnailAsync(
                    item.Id,
                    file,
                    _thumbnailOptions.DetailWidth);

                if (generation == _selectionGeneration
                    && SelectedMedia?.Id == item.Id
                    && preview.IsSuccessful
                    && preview.EncodedImage is { Length: > 0 })
                {
                    using var stream = new MemoryStream(preview.EncodedImage, writable: false);
                    var bitmap = new Bitmap(stream);
                    SelectedPreview?.Dispose();
                    SelectedPreview = bitmap;
                }
            }

            DetailStatus = "Details geladen.";
        }
        catch (Exception exception)
        {
            if (generation == _selectionGeneration)
            {
                DetailStatus = "Details konnten nicht vollständig geladen werden: " + exception.GetBaseException().Message;
            }
        }
    }

    private async Task<IReadOnlyList<string>> GetProjectNamesAsync(string mediaItemId)
    {
        var projects = await _projectService.GetProjectsAsync();
        var names = new List<string>();
        foreach (var project in projects)
        {
            var references = await _projectService.GetMediaReferencesAsync(project.Id);
            if (references.Any(reference => string.Equals(reference.MediaItemId, mediaItemId, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(project.Name);
            }
        }

        return names;
    }

    private MediaFile? TryResolveMediaFile(MediaIndexSearchResult result)
    {
        if (!result.RepresentativeIsPresent
            || string.IsNullOrWhiteSpace(result.RepresentativeEndpointId)
            || string.IsNullOrWhiteSpace(result.RepresentativeRelativePath)
            || !_endpointRoots.TryGetValue(result.RepresentativeEndpointId, out var root))
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
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
            var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)
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

    private MediaFileType? GetSelectedMediaType() => SelectedTypeFilter switch
    {
        "Bilder" => MediaFileType.Image,
        "Videos" => MediaFileType.Video,
        "Audio" => MediaFileType.Audio,
        _ => null
    };

    private int? GetMinimumRating() => SelectedRatingFilter switch
    {
        "ab 1 ★" => 1,
        "ab 2 ★" => 2,
        "nur 3 ★" => 3,
        _ => null
    };

    private MediaIndexSortOrder GetSelectedSortOrder() => SelectedSort switch
    {
        "Zuletzt hinzugefügt" => MediaIndexSortOrder.FirstSeenDescending,
        "Aufnahmedatum neu–alt" => MediaIndexSortOrder.CaptureDateDescending,
        "Aufnahmedatum alt–neu" => MediaIndexSortOrder.CaptureDateAscending,
        "Dateiname A–Z" => MediaIndexSortOrder.FileNameAscending,
        "Dateiname Z–A" => MediaIndexSortOrder.FileNameDescending,
        "Größe absteigend" => MediaIndexSortOrder.SizeDescending,
        "Größe aufsteigend" => MediaIndexSortOrder.SizeAscending,
        _ => MediaIndexSortOrder.LastSeenDescending
    };

    private void ClearDetails()
    {
        SelectedPreview?.Dispose();
        SelectedPreview = null;
        SelectedTitle = string.Empty;
        SelectedSubTitle = string.Empty;
        SelectedMediaId = string.Empty;
        SelectedFileName = string.Empty;
        SelectedType = string.Empty;
        SelectedSize = string.Empty;
        SelectedCaptureDate = string.Empty;
        SelectedCamera = string.Empty;
        SelectedDimensions = string.Empty;
        SelectedGps = string.Empty;
        SelectedDescription = string.Empty;
        SelectedProjects = string.Empty;
        SelectedLocations = Array.Empty<MediaIndexLocationItemViewModel>();
        SelectedKnowledgeSummary = "–";
        SelectedKnowledgeItems = Array.Empty<AssignedKnowledgeItemViewModel>();
        SelectedRatingText = "Unbewertet";
    }

    private void DisposeItems()
    {
        foreach (var item in _items)
        {
            item.Dispose();
        }
        _items.Clear();
    }

    public MediaViewerViewModel CreateViewer(
        MediaBrowserItemViewModel startItem,
        IReadOnlyList<MediaBrowserItemViewModel>? selectedItems = null)
    {
        ArgumentNullException.ThrowIfNull(startItem);

        var selectedImages = selectedItems?
            .Where(item => item.IsImage)
            .ToArray()
            ?? Array.Empty<MediaBrowserItemViewModel>();

        IReadOnlyList<MediaBrowserItemViewModel> scope =
            selectedImages.Length > 1 && selectedImages.Any(item => item.Id == startItem.Id)
                ? selectedImages
                : Items.Where(item => item.IsImage).ToArray();

        if (!scope.Any(item => item.Id == startItem.Id))
        {
            scope = new[] { startItem }.Concat(scope).DistinctBy(item => item.Id).ToArray();
        }

        return new MediaViewerViewModel(
            _thumbnailService,
            _thumbnailOptions,
            _storageSettingsStore,
            _storageLocationResolver,
            _mediaIndexService,
            scope,
            startItem.Id);
    }

    public void Dispose()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        SelectedPreview?.Dispose();
        SelectedPreview = null;
        DisposeItems();
    }
}
