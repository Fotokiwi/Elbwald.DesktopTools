namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public enum LibraryHealthScanStage
{
    ResolvingStorageLocations = 0,
    ScanningFiles = 1,
    ReadingMetadata = 2,
    InspectingIndexLocations = 3,
    InspectingIndexMetadata = 4,
    Finalizing = 5,
    Completed = 6
}
