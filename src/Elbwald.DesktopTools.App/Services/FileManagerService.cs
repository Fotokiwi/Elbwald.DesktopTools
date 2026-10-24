using System.Diagnostics;

namespace Elbwald.DesktopTools.App.Services;

public sealed class FileManagerService : IFileManagerService
{
    public bool TryOpenContainingFolder(string absolutePath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            errorMessage = "Für diesen Fundort konnte kein aktueller absoluter Pfad ermittelt werden.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(absolutePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errorMessage = "Der Dateipfad ist ungültig: " + exception.Message;
            return false;
        }

        var directory = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            errorMessage = "Der zugehörige Ordner ist derzeit nicht erreichbar.";
            return false;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo("explorer.exe")
                {
                    UseShellExecute = false
                };

                if (File.Exists(fullPath))
                {
                    startInfo.ArgumentList.Add("/select," + fullPath);
                }
                else
                {
                    startInfo.ArgumentList.Add(directory);
                }

                Process.Start(startInfo);
                return true;
            }

            if (OperatingSystem.IsMacOS())
            {
                var startInfo = new ProcessStartInfo("open")
                {
                    UseShellExecute = false
                };

                if (File.Exists(fullPath))
                {
                    startInfo.ArgumentList.Add("-R");
                    startInfo.ArgumentList.Add(fullPath);
                }
                else
                {
                    startInfo.ArgumentList.Add(directory);
                }

                Process.Start(startInfo);
                return true;
            }

            if (OperatingSystem.IsLinux())
            {
                if (TryStartLinuxOpener("xdg-open", directory)
                    || TryStartLinuxOpener("gio", directory, "open"))
                {
                    return true;
                }

                errorMessage = "Es wurde kein unterstützter Dateimanager-Opener (xdg-open/gio) gefunden.";
                return false;
            }

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = "Der Dateimanager konnte nicht geöffnet werden: " + exception.GetBaseException().Message;
            return false;
        }
    }
    private static bool TryStartLinuxOpener(string executable, string directory, string? firstArgument = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false
            };

            if (!string.IsNullOrWhiteSpace(firstArgument))
            {
                startInfo.ArgumentList.Add(firstArgument);
            }

            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
