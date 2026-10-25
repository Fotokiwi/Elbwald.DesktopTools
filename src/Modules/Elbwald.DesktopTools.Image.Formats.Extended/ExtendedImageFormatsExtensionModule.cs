using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Contracts.Media.Thumbnailing;
using Elbwald.DesktopTools.Contracts.Modules;
using Elbwald.DesktopTools.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Image.Formats.Extended;

/// <summary>
/// Capability-only package for non-default raster/image container formats.
/// JPEG and PNG remain host-owned base formats. RAW and JPEG XL deliberately
/// live in separate future packages because their workflows differ materially.
/// </summary>
public sealed class ExtendedImageFormatsExtensionModule : IExtensionModule
{
    public const string ExtensionId = "elbwald.image.formats.extended";

    public string Id => ExtensionId;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediaFormatProvider<WebpMediaFormatProvider>(Id);
        services.AddMediaFormatProvider<TiffMediaFormatProvider>(Id);
        services.AddMediaFormatProvider<HeifMediaFormatProvider>(Id);
        services.AddMediaFormatProvider<AvifMediaFormatProvider>(Id);
        services.AddMediaFormatProvider<Jpeg2000MediaFormatProvider>(Id);

        // Vertical support is introduced format by format. WebP and TIFF now
        // provide proven metadata + export capabilities; the remaining formats
        // stay detection-only until their own integration tests exist.
        services.AddImageDecodeProvider<MagickExtendedImageDecodeProvider>(Id);
        services.AddImageMetadataProvider<WebpImageMetadataProvider>(Id);
        services.AddImageMetadataProvider<TiffImageMetadataProvider>(Id);
        services.AddImageExportProvider<WebpImageExportProvider>(Id);
        services.AddImageExportProvider<TiffImageExportProvider>(Id);
        services.AddImageThumbnailProvider<MagickExtendedImageThumbnailProvider>(Id);

        services.AddSettingsContribution(
            new SettingsContribution(
                ExtensionId: Id,
                Id: "extended-image-formats",
                Title: "Erweiterte Bildformate",
                Description: "WebP und TIFF werden über das optionale Magick.NET-Backend inklusive Vollbild-Decoding bereitgestellt. Weitere Formate werden erst nach vollständiger vertikaler Prüfung freigeschaltet.",
                Icon: "Image",
                Order: 100));
    }
}
