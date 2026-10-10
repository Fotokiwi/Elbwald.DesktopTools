namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public sealed record MetadataEmbeddedCopyResult(
    bool Success,
    string OutputPath,
    string? Sha256 = null,
    long? Length = null,
    string? ErrorMessage = null);
