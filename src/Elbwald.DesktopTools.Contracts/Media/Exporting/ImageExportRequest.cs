using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.Media.Exporting;

public sealed record ImageExportRequest(
    string SourcePath,
    MediaFileType MediaType,
    int? PixelWidth,
    int? PixelHeight,
    int? MaxEdge,
    int? Quality);
