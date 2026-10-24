using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.MediaIndex;

namespace Elbwald.DesktopTools.Contracts.DuplicateFinding;

public sealed record ExactDuplicateGroup(
    string MediaItemId,
    string Sha256,
    long Length,
    MediaFileType MediaType,
    IReadOnlyList<MediaIndexLocation> Locations)
{
    public int CopyCount => Locations.Count;

    public int RedundantCopyCount => Math.Max(0, CopyCount - 1);

    public long ReclaimableBytes =>
        Length <= 0 || RedundantCopyCount == 0
            ? 0
            : Length * RedundantCopyCount;
}
