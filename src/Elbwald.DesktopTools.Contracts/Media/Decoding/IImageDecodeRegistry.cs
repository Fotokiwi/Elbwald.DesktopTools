using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Decoding;

/// <summary>
/// Central view over built-in and currently active extension image decoders.
/// </summary>
public interface IImageDecodeRegistry
{
    event EventHandler? ProvidersChanged;

    IReadOnlyList<IImageDecodeProvider> Providers { get; }

    IReadOnlyList<IImageDecodeProvider> FindProviders(MediaFile file);
}
