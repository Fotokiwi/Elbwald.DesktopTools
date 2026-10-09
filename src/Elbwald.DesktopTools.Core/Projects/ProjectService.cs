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

    public Task<bool> RemoveMediaAsync(string projectId, string mediaItemId, CancellationToken cancellationToken = default) =>
        _store.RemoveMediaAsync(projectId, mediaItemId, cancellationToken);
}
