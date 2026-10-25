using System.Text.Json;

namespace Elbwald.DesktopTools.Contracts.Media.Editing;

/// <summary>
/// One versioned, non-destructive edit step. Parameters remain opaque JSON so new
/// operation types can be added by editor modules without database migrations.
/// </summary>
public sealed record ImageEditOperation(
    string Id,
    string TypeId,
    int Version,
    bool IsEnabled,
    string ParametersJson)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(TypeId);

        if (Version <= 0)
        {
            throw new InvalidOperationException(
                $"Edit-Operation '{TypeId}' hat keine gültige Version.");
        }

        if (string.IsNullOrWhiteSpace(ParametersJson))
        {
            throw new InvalidOperationException(
                $"Edit-Operation '{TypeId}' hat keine Parameterdefinition.");
        }

        try
        {
            using var _ = JsonDocument.Parse(ParametersJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Edit-Operation '{TypeId}' enthält ungültiges Parameter-JSON.",
                exception);
        }
    }
}
