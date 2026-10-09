using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Contracts.MediaIndex;

public sealed record MediaIndexItem(
    string Id,
    string Sha256,
    long Length,
    MediaFileType MediaType,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);
