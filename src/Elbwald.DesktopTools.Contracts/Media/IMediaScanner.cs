namespace Elbwald.DesktopTools.Contracts.Media;

public interface IMediaScanner
{
    Task<MediaScanResult> ScanAsync(
        string path,
        MediaScanOptions? options = null,
        IProgress<MediaScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
