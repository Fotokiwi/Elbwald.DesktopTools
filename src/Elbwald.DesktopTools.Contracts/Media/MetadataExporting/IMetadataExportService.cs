namespace Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

public interface IMetadataExportService
{
    Task<MetadataExportInspection> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    MetadataExportCapability GetEmbeddedCapability(string mediaPath, MetadataExportValues values);

    Task<MetadataSidecarExportResult> WriteSidecarAsync(
        string mediaPath,
        MetadataExportValues values,
        CancellationToken cancellationToken = default);

    Task<MetadataEmbeddedCopyResult> WriteEmbeddedCopyAsync(
        string sourceJpegPath,
        string outputJpegPath,
        MetadataExportValues values,
        CancellationToken cancellationToken = default);

    Task<MetadataEmbeddedWriteResult> WriteEmbeddedAsync(
        string mediaItemId,
        string mediaPath,
        string expectedSha256,
        long expectedLength,
        DateTime expectedLastWriteTimeUtc,
        MetadataExportValues values,
        string batchId,
        CancellationToken cancellationToken = default);

    Task<bool> RestoreEmbeddedAsync(
        string mediaPath,
        string backupPath,
        string expectedCurrentSha256,
        CancellationToken cancellationToken = default);

    void FinalizeBackup(string? backupPath);
}
