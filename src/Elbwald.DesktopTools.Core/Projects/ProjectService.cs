using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Projects;

namespace Elbwald.DesktopTools.Core.Projects;

public sealed class ProjectService : IProjectService
{
    private readonly SqliteProjectStore _store;
    private readonly IMediaIndexService _mediaIndexService;

    public ProjectService(ProjectOptions options, IMediaIndexService mediaIndexService)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        _store = new SqliteProjectStore(options);
        _mediaIndexService = mediaIndexService;
    }

    public Task<IReadOnlyList<ProjectRecord>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        _store.GetProjectsAsync(cancellationToken);

    public Task<ProjectRecord?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
        _store.GetProjectAsync(projectId, cancellationToken);

    public Task<ProjectRecord> CreateProjectAsync(ProjectCreateRequest request, CancellationToken cancellationToken = default) =>
        _store.InsertProjectAsync(request, cancellationToken);

    public Task<ProjectRecord> UpdateProjectAsync(string projectId, ProjectUpdateRequest request, CancellationToken cancellationToken = default) =>
        _store.UpdateProjectAsync(projectId, request, cancellationToken);

    public Task<IReadOnlyList<ProjectMediaReference>> GetMediaReferencesAsync(string projectId, CancellationToken cancellationToken = default) =>
        _store.GetMediaReferencesAsync(projectId, cancellationToken);

    public async Task<bool> AddMediaAsync(string projectId, string mediaItemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        if (await _store.GetProjectAsync(projectId, cancellationToken) is null)
            throw new InvalidOperationException("Das Projekt existiert nicht mehr.");
        if (await _mediaIndexService.GetItemAsync(mediaItemId, cancellationToken) is null)
            throw new InvalidOperationException("Das Medium ist im lokalen Medienindex nicht mehr vorhanden.");
        return await _store.AddMediaAsync(projectId, mediaItemId, cancellationToken);
    }

    public Task<bool> RemoveMediaAsync(
        string projectId,
        string mediaItemId,
        CancellationToken cancellationToken = default) =>
        _store.RemoveMediaAsync(projectId, mediaItemId, cancellationToken);

    public async Task SetCoverAsync(
        string projectId,
        string? mediaItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);

        if (!string.IsNullOrWhiteSpace(mediaItemId))
        {
            var media = await _mediaIndexService.GetItemAsync(
                mediaItemId,
                cancellationToken);

            if (media is null)
            {
                throw new InvalidOperationException(
                    "Das Medium ist im lokalen Medienindex nicht mehr vorhanden.");
            }

            if (media.MediaType != MediaFileType.Image)
            {
                throw new InvalidOperationException(
                    "Als Projektcover kann derzeit nur ein Bild verwendet werden.");
            }
        }

        await _store.SetCoverAsync(
            projectId,
            mediaItemId,
            cancellationToken);
    }

    public Task SetHighlightAsync(
        string projectId,
        string mediaItemId,
        bool isHighlight,
        CancellationToken cancellationToken = default) =>
        _store.SetHighlightAsync(
            projectId,
            mediaItemId,
            isHighlight,
            cancellationToken);


    public async Task SetMediaProjectDateAsync(
        string projectId,
        string mediaItemId,
        DateOnly? projectDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);

        var project = await _store.GetProjectAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Das Projekt existiert nicht mehr.");

        if (projectDate.HasValue)
        {
            if (project.StartDate.HasValue && projectDate.Value < project.StartDate.Value)
            {
                throw new InvalidOperationException(
                    "Der Projekttag liegt vor dem gespeicherten Projektbeginn.");
            }

            if (project.EndDate.HasValue && projectDate.Value > project.EndDate.Value)
            {
                throw new InvalidOperationException(
                    "Der Projekttag liegt nach dem gespeicherten Projektende.");
            }
        }

        await _store.SetMediaProjectDateAsync(
            projectId,
            mediaItemId,
            projectDate,
            cancellationToken);
    }

    public Task<IReadOnlyList<ProjectJournalEntry>> GetJournalEntriesAsync(
        string projectId,
        CancellationToken cancellationToken = default) =>
        _store.GetJournalEntriesAsync(projectId, cancellationToken);

    public async Task<ProjectJournalEntry> CreateJournalEntryAsync(
        string projectId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentNullException.ThrowIfNull(request);

        if (await _store.GetProjectAsync(projectId, cancellationToken) is null)
        {
            throw new InvalidOperationException("Das Projekt existiert nicht mehr.");
        }

        return await _store.CreateJournalEntryAsync(
            projectId,
            request,
            cancellationToken);
    }

    public async Task<ProjectJournalEntry> UpdateJournalEntryAsync(
        string projectId,
        string entryId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(request);

        return await _store.UpdateJournalEntryAsync(
            projectId,
            entryId,
            request,
            cancellationToken);
    }

    public Task DeleteJournalEntryAsync(
        string projectId,
        string entryId,
        CancellationToken cancellationToken = default) =>
        _store.DeleteJournalEntryAsync(
            projectId,
            entryId,
            cancellationToken);

    public Task<IReadOnlyList<ProjectJournalMediaReference>> GetJournalMediaReferencesAsync(
        string entryId,
        CancellationToken cancellationToken = default) =>
        _store.GetJournalMediaReferencesAsync(entryId, cancellationToken);

    public async Task SetJournalMediaAsync(
        string projectId,
        string entryId,
        IReadOnlyCollection<string> mediaItemIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(mediaItemIds);

        var allowedIds = (await _store.GetMediaReferencesAsync(projectId, cancellationToken))
            .Select(reference => reference.MediaItemId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var requestedIds = mediaItemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var invalidId = requestedIds.FirstOrDefault(id => !allowedIds.Contains(id));
        if (invalidId is not null)
        {
            throw new InvalidOperationException(
                "Tagebucheinträge können nur Medien verwenden, die dem Projekt zugeordnet sind.");
        }

        await _store.SetJournalMediaAsync(
            projectId,
            entryId,
            requestedIds,
            cancellationToken);
    }
}
