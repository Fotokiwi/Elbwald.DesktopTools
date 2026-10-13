namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectJournalMediaReference(
    string EntryId,
    string MediaItemId,
    DateTimeOffset AddedAtUtc);
