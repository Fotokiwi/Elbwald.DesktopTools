namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record ImageMetadata(
    int? PixelWidth,
    int? PixelHeight,
    DateTime? CapturedAt,
    string? CameraMake,
    string? CameraModel,
    string? LensModel,
    int? Orientation,
    bool HasExif,
    bool HasGps,
    double? Latitude,
    double? Longitude)
{
    public DateTime? DateTimeOriginal { get; init; }

    public DateTime? DateTimeDigitized { get; init; }

    public DateTime? DateTimeModified { get; init; }

    public DateTime? GpsDateTimeUtc { get; init; }

    public bool HasDimensions =>
        PixelWidth is > 0
        && PixelHeight is > 0;

    public bool HasCaptureDate =>
        CapturedAt.HasValue;

    public bool HasCameraInformation =>
        !string.IsNullOrWhiteSpace(CameraMake)
        || !string.IsNullOrWhiteSpace(CameraModel);

    public ImageDisplayOrientation DisplayOrientation
    {
        get
        {
            if (!HasDimensions)
            {
                return ImageDisplayOrientation.Unknown;
            }

            var width = PixelWidth!.Value;
            var height = PixelHeight!.Value;

            if (Orientation is 5 or 6 or 7 or 8)
            {
                (width, height) = (height, width);
            }

            if (width == height)
            {
                return ImageDisplayOrientation.Square;
            }

            return width > height
                ? ImageDisplayOrientation.Landscape
                : ImageDisplayOrientation.Portrait;
        }
    }
}
