using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

namespace Elbwald.DesktopTools.App.ViewModels;

public sealed class MediaExportPlanItemViewModel
{
    public MediaExportPlanItemViewModel(
        string mediaItemId,
        string sourcePath,
        string destinationPath,
        string relativeDestinationPath,
        string expectedSha256,
        long expectedLength,
        DateTime expectedLastWriteTimeUtc,
        MediaFileType mediaType,
        int? pixelWidth,
        int? pixelHeight,
        MetadataExportValues metadata,
        bool embedXmp,
        bool isReady,
        string status,
        string? issue = null)
    {
        MediaItemId = mediaItemId;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        RelativeDestinationPath = relativeDestinationPath;
        ExpectedSha256 = expectedSha256;
        ExpectedLength = expectedLength;
        ExpectedLastWriteTimeUtc = expectedLastWriteTimeUtc;
        MediaType = mediaType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Metadata = metadata;
        EmbedXmp = embedXmp;
        IsReady = isReady;
        Status = status;
        Issue = issue;
    }

    public string MediaItemId { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public string RelativeDestinationPath { get; }
    public string ExpectedSha256 { get; }
    public long ExpectedLength { get; }
    public DateTime ExpectedLastWriteTimeUtc { get; }
    public MediaFileType MediaType { get; }
    public int? PixelWidth { get; }
    public int? PixelHeight { get; }
    public MetadataExportValues Metadata { get; }
    public bool EmbedXmp { get; }
    public bool IsReady { get; }
    public bool IsBlocked => !IsReady;
    public string Status { get; }
    public string? Issue { get; }

    public string SourceFileName => Path.GetFileName(SourcePath);
    public string SourceDirectory => Path.GetDirectoryName(SourcePath) ?? string.Empty;
    public string DestinationFileName => Path.GetFileName(DestinationPath);
    public string SizeText => FormatBytes(ExpectedLength);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes:N0} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:N1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):N1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):N2} GB";
    }
}
