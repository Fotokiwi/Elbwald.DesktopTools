using Elbwald.DesktopTools.Contracts.Media.Dates;

namespace Elbwald.DesktopTools.PhotoSort.ViewModels;

public sealed class PhotoSortDateCandidateOptionViewModel
{
    public PhotoSortDateCandidateOptionViewModel(
        MediaDateCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Value = candidate.Value;
        Source = candidate.Source;
        SourceLabel = FormatSource(candidate.Source);
        DisplayText = $"{SourceLabel}: {candidate.Value:dd.MM.yyyy, HH:mm:ss}";
    }

    public DateTime Value { get; }

    public MediaDateSource Source { get; }

    public string SourceLabel { get; }

    public string DisplayText { get; }

    private static string FormatSource(MediaDateSource source)
    {
        return source switch
        {
            MediaDateSource.ExifDateTimeOriginal => "Kamera-Aufnahmedatum",
            MediaDateSource.ExifLegacyCapturedAt => "Kamera-Aufnahmedatum",
            MediaDateSource.ExifDateTimeDigitized => "Digitalisierungsdatum",
            MediaDateSource.ExifDateTimeModified => "EXIF-Änderungsdatum",
            MediaDateSource.FileName => "Dateiname",
            MediaDateSource.GpsUtc => "GPS-Zeit",
            MediaDateSource.FileSystemCreationUtc => "Datei erstellt",
            MediaDateSource.FileSystemLastWriteUtc => "Datei geändert",
            MediaDateSource.UserOverride => "Eigene Auswahl",
            _ => "Gefundenes Datum"
        };
    }
}
