namespace Elbwald.DesktopTools.Contracts.Projects;

public sealed record ProjectMediaReference(
    string ProjectId,
    string MediaItemId,
    DateTimeOffset AddedAtUtc);
