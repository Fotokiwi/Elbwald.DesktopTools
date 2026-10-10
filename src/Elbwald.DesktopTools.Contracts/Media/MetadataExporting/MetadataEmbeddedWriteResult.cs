namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public sealed record MetadataEmbeddedWriteResult(
    bool Success,
    string MediaPath,
    string? BackupPath = null,
    string? NewSha256 = null,
    long? NewLength = null,
    DateTime? NewLastWriteTimeUtc = null,
    string? ErrorMessage = null,
    bool RecoveryRequired = false);
