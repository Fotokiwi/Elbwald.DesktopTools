namespace Elbwald.DesktopTools.Contracts.Media;

public sealed record MediaScanError(
    string Path,
    string Operation,
    MediaScanErrorKind Kind,
    string Message);
