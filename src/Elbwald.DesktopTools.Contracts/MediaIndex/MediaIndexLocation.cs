using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexLocation(
    long Id,
    string MediaItemId,
    string EndpointId,
    StorageEndpointKind EndpointKind,
    string VolumeId,
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    bool IsPresent,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);
