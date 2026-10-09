using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Core.Paths;

public sealed class AppPaths : IAppPaths
{
    private const string PortableMarkerFileName = "portable.flag";

    public AppPaths()
    {
        ApplicationBaseDirectory =
            Path.GetFullPath(AppContext.BaseDirectory);

        PortableModeMarkerPath =
            Path.Combine(
                ApplicationBaseDirectory,
                PortableMarkerFileName);

        IsPortable =
            File.Exists(PortableModeMarkerPath);

        ApplicationDataDirectory =
            IsPortable
                ? Path.Combine(
                    ApplicationBaseDirectory,
                    "data")
                : BuildInstalledDataRoot();

        ConfigDirectory =
            Path.Combine(
                ApplicationDataDirectory,
                "Config");

        DatabaseDirectory =
            Path.Combine(
                ApplicationDataDirectory,
                "Databases");

        CacheDirectory =
            Path.Combine(
                ApplicationDataDirectory,
                "Cache");

        DiagnosticsDirectory =
            Path.Combine(
                ApplicationDataDirectory,
                "Diagnostics");

        RecoveryDirectory =
            Path.Combine(
                ApplicationDataDirectory,
                "Recovery");
    }

    public bool IsPortable { get; }

    public string ApplicationBaseDirectory { get; }

    public string ApplicationDataDirectory { get; }

    public string ConfigDirectory { get; }

    public string DatabaseDirectory { get; }

    public string CacheDirectory { get; }

    public string DiagnosticsDirectory { get; }

    public string RecoveryDirectory { get; }

    public string PortableModeMarkerPath { get; }

    private static string BuildInstalledDataRoot()
    {
        var localData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(localData))
        {
            var userProfile =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);

            if (string.IsNullOrWhiteSpace(userProfile))
            {
                throw new InvalidOperationException(
                    "Das lokale Anwendungsdatenverzeichnis konnte nicht ermittelt werden.");
            }

            localData =
                Path.Combine(
                    userProfile,
                    ".local",
                    "share");
        }

        return Path.Combine(
            localData,
            "ElbwaldDigital",
            "DesktopTools");
    }
}
