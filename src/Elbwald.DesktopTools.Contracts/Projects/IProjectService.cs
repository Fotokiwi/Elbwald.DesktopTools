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

    Task SetCoverAsync(
        string projectId,
        string? mediaItemId,
        CancellationToken cancellationToken = default);

    Task SetHighlightAsync(
        string projectId,
        string mediaItemId,
        bool isHighlight,
        CancellationToken cancellationToken = default);

    Task SetMediaProjectDateAsync(
        string projectId,
        string mediaItemId,
        DateOnly? projectDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectJournalEntry>> GetJournalEntriesAsync(
        string projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectJournalEntry> CreateJournalEntryAsync(
        string projectId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectJournalEntry> UpdateJournalEntryAsync(
        string projectId,
        string entryId,
        ProjectJournalEntryRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteJournalEntryAsync(
        string projectId,
        string entryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectJournalMediaReference>> GetJournalMediaReferencesAsync(
        string entryId,
        CancellationToken cancellationToken = default);

    Task SetJournalMediaAsync(
        string projectId,
        string entryId,
        IReadOnlyCollection<string> mediaItemIds,
        CancellationToken cancellationToken = default);
}
