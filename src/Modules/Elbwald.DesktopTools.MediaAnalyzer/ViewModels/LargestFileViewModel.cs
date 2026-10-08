using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.MediaAnalyzer.ViewModels;

public sealed class LargestFileViewModel
{
    public LargestFileViewModel(
        MediaAnalyzedFile analyzedFile)
    {
        ArgumentNullException.ThrowIfNull(analyzedFile);

        FileName =
            analyzedFile.File.FileName;

        FullPath =
            analyzedFile.File.FullPath;

        Extension =
            string.IsNullOrWhiteSpace(
                analyzedFile.File.Extension)
                ? "ohne Endung"
                : analyzedFile.File.Extension;

        Size =
            FormatBytes(
                analyzedFile.File.Length);
    }

    public string FileName { get; }

    public string FullPath { get; }

    public string Extension { get; }

    public string Size { get; }

    private static string FormatBytes(
        long bytes)
    {
        string[] units =
        {
            "B",
            "KB",
            "MB",
            "GB",
            "TB"
        };

        var value =
            Math.Max(
                0,
                (double)bytes);

        var unitIndex = 0;

        while (value >= 1024
               && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N1} {units[unitIndex]}";
    }
}
