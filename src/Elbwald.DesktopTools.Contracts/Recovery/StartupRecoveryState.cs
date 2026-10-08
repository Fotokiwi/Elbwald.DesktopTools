namespace Elbwald.DesktopTools.Contracts.Recovery;

public enum StartupRecoveryState
{
    NotScanned,
    Clean,
    AttentionRequired,
    ScanFailed,
    ProcessLockUnavailable
}
