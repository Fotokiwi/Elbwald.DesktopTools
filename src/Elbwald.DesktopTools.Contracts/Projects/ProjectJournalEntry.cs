namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectJournalEntry(
    string Id,
    string ProjectId,
    DateOnly EntryDate,
    string Title,
    string? Body,
    string? Location,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
