namespace Elbwald.DesktopTools.Contracts.Media.Exporting;

/// <summary>
/// Encodes an image into a host-owned destination stream. Providers never receive
/// the final destination path, so commit/overwrite safety remains under host control.
/// </summary>
public interface IImageExportProvider
{
    string ProviderId { get; }

    string FormatId { get; }

    string DisplayName { get; }

    string FileExtension { get; }

    string MimeType { get; }

    int Priority { get; }

    bool SupportsResize { get; }

    bool SupportsQuality { get; }

    ImageExportSupport CheckSupport(ImageExportRequest request);

    Task ExportAsync(
        ImageExportRequest request,
        Stream destination,
        CancellationToken cancellationToken = default);
}
