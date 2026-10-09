namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageEndpoint(
    string Id,
    string Name,
    StorageEndpointKind Kind,
    StorageLocation Location,
    bool Enabled = true)
{
    public const string DefaultImportId = "standard.import";
    public const string DefaultLibraryId = "standard.library";
}
