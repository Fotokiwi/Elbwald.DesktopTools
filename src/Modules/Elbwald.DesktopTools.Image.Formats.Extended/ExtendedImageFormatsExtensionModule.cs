using Elbwald.DesktopTools.Contracts.Media.Exporting;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Media.Metadata;
using Elbwald.DesktopTools.Contracts.Modules;
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

        // WebP already has proven metadata + export implementations. The other
        // extended formats are introduced format-first and gain their own
        // metadata/export/thumbnail capabilities in follow-up patches.
        services.AddImageMetadataProvider<WebpImageMetadataProvider>(Id);
        services.AddImageExportProvider<WebpImageExportProvider>(Id);
    }
}
