namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectUpdateRequest(
    string Name,
    string Kind,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? Location = null,
    string? Notes = null);
