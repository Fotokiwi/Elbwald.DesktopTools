namespace Elbwald.DesktopTools.Contracts.Media.Companions;

public sealed class MediaCompanionGroupPlan
{
    public MediaCompanionGroupPlan(
        string directoryPath,
        string companionStem,
        IEnumerable<MediaCompanionMember> members,
        IEnumerable<MediaCompanionProjection> projectedSidecars,
        MediaCompanionGroupState state,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(companionStem);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(projectedSidecars);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        DirectoryPath = directoryPath;
        CompanionStem = companionStem;
        Members = members.ToArray();
        ProjectedSidecars = projectedSidecars.ToArray();
        State = state;
        Message = message;
    }

    public string DirectoryPath { get; }

    public string CompanionStem { get; }

    public IReadOnlyList<MediaCompanionMember> Members { get; }

    public IReadOnlyList<MediaCompanionProjection> ProjectedSidecars { get; }

    public MediaCompanionGroupState State { get; }

    public string Message { get; }

    public int RawImageCount =>
        Members.Count(member =>
            member.Kind == MediaCompanionKind.RawImage);

    public int JpegImageCount =>
        Members.Count(member =>
            member.Kind == MediaCompanionKind.JpegImage);

    public int SidecarCount =>
        Members.Count(member =>
            member.Kind == MediaCompanionKind.XmpSidecar);

    public int ImageCount =>
        RawImageCount + JpegImageCount;

    public bool IsRawJpegPair =>
        RawImageCount > 0
        && JpegImageCount > 0;

    public bool HasSidecar =>
        SidecarCount > 0;
}
