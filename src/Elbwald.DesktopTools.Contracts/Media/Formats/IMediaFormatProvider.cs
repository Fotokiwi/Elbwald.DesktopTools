using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Formats;

/// <summary>
/// Describes a logical media format and the file extensions by which it can be
/// discovered without reading file contents. Decoding, metadata, thumbnails
/// and export are intentionally separate capabilities.
/// </summary>
public interface IMediaFormatProvider
{
    /// <summary>
    /// Stable logical format id, for example image.jpeg or image.webp.
    /// </summary>
    string FormatId { get; }

    /// <summary>
    /// Broad media kind used by scanner and library workflows.
    /// </summary>
    MediaFileType MediaType { get; }

    /// <summary>
    /// Supported file extensions including the leading dot.
    /// Matching is case-insensitive.
    /// </summary>
    IReadOnlyCollection<string> FileExtensions { get; }
}
