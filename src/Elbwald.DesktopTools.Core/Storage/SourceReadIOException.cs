namespace Elbwald.DesktopTools.Core.Storage;

public sealed class SourceReadIOException : IOException
{
    public SourceReadIOException(
        string sourcePath,
        string operation,
        IOException innerException)
        : base(
            $"Die Quelldatei '{sourcePath}' konnte beim {operation} nicht zuverlässig gelesen werden.",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        SourcePath =
            Path.GetFullPath(sourcePath);

        Operation =
            operation;
    }

    public string SourcePath { get; }

    public string Operation { get; }

    public static SourceReadIOException? Find(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Exception? current =
            exception;

        while (current is not null)
        {
            if (current is SourceReadIOException sourceRead)
            {
                return sourceRead;
            }

            current =
                current.InnerException;
        }

        return null;
    }
}
