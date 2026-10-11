using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;

namespace Elbwald.DesktopTools.Core.Media;

/// <summary>
/// Media type detection backed by the central media format registry. The
/// scanner therefore no longer owns a hard-coded list of supported formats.
/// </summary>
public sealed class ExtensionMediaTypeDetector : IMediaTypeDetector
{
    private readonly IMediaFormatRegistry _formatRegistry;

    public ExtensionMediaTypeDetector(IMediaFormatRegistry formatRegistry)
    {
        ArgumentNullException.ThrowIfNull(formatRegistry);
        _formatRegistry = formatRegistry;
    }

    public MediaFileType Detect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return _formatRegistry.DetectMediaType(filePath);
    }
}
