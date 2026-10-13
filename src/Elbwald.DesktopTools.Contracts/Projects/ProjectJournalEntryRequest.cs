namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectJournalEntryRequest(
    DateOnly EntryDate,
    string Title,
    string? Body = null,
    string? Location = null);
