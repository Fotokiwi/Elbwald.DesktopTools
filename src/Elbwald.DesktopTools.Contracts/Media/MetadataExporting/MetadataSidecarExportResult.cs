namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public sealed record MetadataSidecarExportResult(
    bool Success,
    string SidecarPath,
    string? BackupPath = null,
    string? ErrorMessage = null);
