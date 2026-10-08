namespace Elbwald.DesktopTools.Contracts.Media.Dates;

public enum MediaDateSource
{
    ExifDateTimeOriginal,
    ExifDateTimeDigitized,
    ExifDateTimeModified,
    ExifLegacyCapturedAt,
    GpsUtc,
    FileName,
    FileSystemCreationUtc,
    FileSystemLastWriteUtc
}
