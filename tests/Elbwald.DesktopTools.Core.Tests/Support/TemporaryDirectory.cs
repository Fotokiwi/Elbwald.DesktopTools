namespace Elbwald.DesktopTools.Core.Tests.Support;

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        RootPath = Path.Combine(
            Path.GetTempPath(),
            "Elbwald.DesktopTools.Tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string relativePath)
    {
        return Path.Combine(RootPath, relativePath);
    }

    public string CreateFile(
        string relativePath,
        string content = "test")
    {
        var path = GetPath(relativePath);
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        Directory.Delete(
            RootPath,
            recursive: true);
    }
}
