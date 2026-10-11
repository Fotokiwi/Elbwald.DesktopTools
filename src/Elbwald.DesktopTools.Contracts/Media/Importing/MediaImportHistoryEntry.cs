namespace Elbwald.DesktopTools.Contracts.Media.Importing;

public sealed record MediaImportHistoryEntry(
    string SourcePath,
    string DestinationRootPath,
    string DestinationPath,
    string DestinationSubfolderName,
    DateTimeOffset CompletedUtc,
    int CopiedCount,
    int AlreadyImportedCount,
    int ConflictCount);
