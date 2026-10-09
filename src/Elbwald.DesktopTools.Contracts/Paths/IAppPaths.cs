namespace Elbwald.DesktopTools.Contracts.Paths;

public interface IAppPaths
{
    bool IsPortable { get; }

    string ApplicationBaseDirectory { get; }

    string ApplicationDataDirectory { get; }

    string ConfigDirectory { get; }

    string DatabaseDirectory { get; }

    string CacheDirectory { get; }

    string DiagnosticsDirectory { get; }

    string RecoveryDirectory { get; }

    string PortableModeMarkerPath { get; }
}
