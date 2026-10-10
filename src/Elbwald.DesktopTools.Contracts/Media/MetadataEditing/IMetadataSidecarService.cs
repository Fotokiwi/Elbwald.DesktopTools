namespace Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

public interface IMetadataSidecarService
{
    Task<MetadataSidecarSnapshot> ReadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default);

    Task<MetadataSidecarWriteResult> WriteAsync(
        string mediaPath,
        MetadataEditValues values,
        CancellationToken cancellationToken = default);
}
