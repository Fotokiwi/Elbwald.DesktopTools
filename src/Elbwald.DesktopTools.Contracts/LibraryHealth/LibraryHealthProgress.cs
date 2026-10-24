namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthProgress(
    LibraryHealthScanStage Stage,
    int EndpointIndex = 0,
    int EndpointTotal = 0,
    string? EndpointName = null,
    int FilesDiscovered = 0,
    int Processed = 0,
    int Total = 0,
    string? CurrentPath = null);
