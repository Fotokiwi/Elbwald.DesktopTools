namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public sealed record MetadataSidecarWriteResult(
    bool Success,
    string SidecarPath,
    string? BackupPath = null,
    string? ErrorMessage = null);
