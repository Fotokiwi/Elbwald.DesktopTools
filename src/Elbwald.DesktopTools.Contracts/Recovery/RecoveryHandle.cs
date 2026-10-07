namespace Elbwald.DesktopTools.Contracts.Recovery;

public sealed record RecoveryHandle(
    string Id,
    string SourcePath,
    long Length,
    string Sha256,
    DateTime LastWriteTimeUtc,
    DateTimeOffset CreatedAtUtc,
    RecoveryStorageKind StorageKind,
    string? StorageLocation = null);
