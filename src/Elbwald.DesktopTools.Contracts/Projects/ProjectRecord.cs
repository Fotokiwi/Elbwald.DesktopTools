namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectRecord(
    string Id,
    string Name,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? Location = null,
    string? Notes = null);
