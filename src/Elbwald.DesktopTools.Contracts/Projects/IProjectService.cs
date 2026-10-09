namespace Elbwald.DesktopTools.Contracts.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectRecord>> GetProjectsAsync(
        CancellationToken cancellationToken = default);

    Task<ProjectRecord?> GetProjectAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectRecord> CreateProjectAsync(
        ProjectCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectRecord> UpdateProjectAsync(
        string projectId,
        ProjectUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectMediaReference>> GetMediaReferencesAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<bool> AddMediaAsync(
        string projectId,
        string mediaItemId,
        CancellationToken cancellationToken = default);

    Task<bool> RemoveMediaAsync(
        string projectId,
        string mediaItemId,
        CancellationToken cancellationToken = default);
}
