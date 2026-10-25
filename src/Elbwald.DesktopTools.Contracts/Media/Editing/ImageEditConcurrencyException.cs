namespace Elbwald.DesktopTools.Contracts.Media.Editing;

public sealed class ImageEditConcurrencyException : InvalidOperationException
{
    public ImageEditConcurrencyException(string message)
        : base(message)
    {
    }
}
