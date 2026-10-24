using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.LibraryHealth;

public sealed record LibraryHealthIndexEndpointState(
    string EndpointId,
    bool Enabled,
    StorageLocationStatus StorageStatus,
    string? ResolvedPath);
