using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Formats;

/// <summary>
/// Central read-only registry over built-in and extension-provided media
/// formats. Consumers ask for capabilities instead of hard-coding format lists.
/// </summary>
public interface IMediaFormatRegistry
{
    IReadOnlyList<IMediaFormatProvider> Providers { get; }

    IReadOnlyList<IMediaFormatProvider> FindByFilePath(string filePath);

    IReadOnlyList<IMediaFormatProvider> FindByFormatId(string formatId);

    MediaFileType DetectMediaType(string filePath);
}
