using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Elbwald.DesktopTools.Image.WebP;

/// <summary>
/// Capability-only extension that contributes WebP format recognition.
/// It intentionally has no navigation entry or UI.
/// </summary>
public sealed class WebpExtensionModule : IExtensionModule
{
    public const string ExtensionId = "elbwald.image.webp";

    public string Id => ExtensionId;

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMediaFormatProvider<WebpMediaFormatProvider>(Id);
    }
}
