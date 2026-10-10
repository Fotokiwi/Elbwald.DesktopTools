using CommunityToolkit.Mvvm.ComponentModel;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

namespace Elbwald.DesktopTools.App.ViewModels;

public partial class MetadataFileItemViewModel : ObservableObject
{
    [ObservableProperty] private bool isSelected = true;
    [ObservableProperty] private string status = "Bereit";
    [ObservableProperty] private string exportStatus = "Nur Elbwald DB";

    public required string MediaItemId { get; init; }
    public required string Sha256 { get; set; }
    public required string FullPath { get; init; }
    public required string FileName { get; init; }
    public required long Length { get; set; }
    public required DateTime LastWriteTimeUtc { get; set; }

    public DateTime? OriginalCaptureDate { get; init; }
    public string? Camera { get; init; }
    public double? OriginalLatitude { get; init; }
    public double? OriginalLongitude { get; init; }

    public bool CaptureDateOverridden { get; set; }
    public DateTime? DatabaseCaptureDate { get; set; }
    public bool TitleOverridden { get; set; }
    public string? DatabaseTitle { get; set; }
    public bool DescriptionOverridden { get; set; }
    public string? DatabaseDescription { get; set; }
    public MetadataGpsMode GpsMode { get; set; }
    public double? DatabaseLatitude { get; set; }
    public double? DatabaseLongitude { get; set; }

    public string SizeText => FormatBytes(Length);
    public string CaptureDateText =>
        (CaptureDateOverridden ? DatabaseCaptureDate : OriginalCaptureDate)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "–";
    public string CameraText => string.IsNullOrWhiteSpace(Camera) ? "–" : Camera;
    public string GpsText
    {
        get
        {
            if (GpsMode == MetadataGpsMode.Remove)
            {
                return "Entfernt (Elbwald)";
            }

            var lat = GpsMode == MetadataGpsMode.Set ? DatabaseLatitude : OriginalLatitude;
            var lon = GpsMode == MetadataGpsMode.Set ? DatabaseLongitude : OriginalLongitude;
            return lat.HasValue && lon.HasValue
                ? $"{lat.Value:0.#####}, {lon.Value:0.#####}"
                : "–";
        }
    }

    public string DatabaseState
    {
        get
        {
            var count = 0;
            if (CaptureDateOverridden) count++;
            if (TitleOverridden) count++;
            if (DescriptionOverridden) count++;
            if (GpsMode != MetadataGpsMode.Inherit) count++;
            return count == 0 ? "Original" : $"Elbwald · {count} Feld(er)";
        }
    }

    public void ApplyDatabaseSnapshot(MediaMetadataSnapshot snapshot)
    {
        CaptureDateOverridden = snapshot.CaptureDateOverridden;
        DatabaseCaptureDate = snapshot.CaptureDate;
        TitleOverridden = snapshot.TitleOverridden;
        DatabaseTitle = snapshot.Title;
        DescriptionOverridden = snapshot.DescriptionOverridden;
        DatabaseDescription = snapshot.Description;
        GpsMode = snapshot.GpsMode;
        DatabaseLatitude = snapshot.Latitude;
        DatabaseLongitude = snapshot.Longitude;
        OnPropertyChanged(nameof(CaptureDateText));
        OnPropertyChanged(nameof(GpsText));
        OnPropertyChanged(nameof(DatabaseState));
    }


    public void ApplyPhysicalSnapshot(string sha256, long length, DateTime lastWriteTimeUtc)
    {
        Sha256 = sha256;
        Length = length;
        LastWriteTimeUtc = lastWriteTimeUtc;
        OnPropertyChanged(nameof(SizeText));
    }
    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
