namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// Raised when an enabled stored edit operation cannot be rendered because the
/// module that provides its processor is not currently available.
/// </summary>
public sealed class ImageEditOperationUnavailableException : InvalidOperationException
{
    public ImageEditOperationUnavailableException(ImageEditOperation operation)
        : base(BuildMessage(operation))
    {
        OperationId = operation.Id;
        OperationTypeId = operation.TypeId;
        OperationVersion = operation.Version;
    }

    public string OperationId { get; }

    public string OperationTypeId { get; }

    public int OperationVersion { get; }

    private static string BuildMessage(ImageEditOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return $"Für die aktivierte Bildbearbeitungsoperation '{operation.TypeId}' "
            + $"(Version {operation.Version}, ID '{operation.Id}') ist kein aktiver Prozessor verfügbar.";
    }
}
