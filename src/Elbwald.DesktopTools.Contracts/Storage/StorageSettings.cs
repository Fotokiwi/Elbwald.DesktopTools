namespace Elbwald.DesktopTools.Contracts.Storage;

public sealed record StorageSettings(
    IReadOnlyList<StorageEndpoint> Endpoints)
{
    public StorageSettings()
        : this(Array.Empty<StorageEndpoint>())
    {
    }

    public static StorageSettings Default { get; } = new();

    public StorageEndpoint? Find(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        return Endpoints.FirstOrDefault(
            endpoint => string.Equals(
                endpoint.Id,
                endpointId,
                StringComparison.OrdinalIgnoreCase));
    }

    public StorageSettings WithEndpoint(StorageEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (string.IsNullOrWhiteSpace(endpoint.Id))
        {
            throw new ArgumentException(
                "Ein Speicher-Endpunkt benötigt eine stabile interne ID.",
                nameof(endpoint));
        }

        var updated = Endpoints
            .Where(existing => !string.Equals(
                existing.Id,
                endpoint.Id,
                StringComparison.OrdinalIgnoreCase))
            .Append(endpoint)
            .ToArray();

        return new StorageSettings(updated);
    }

    public StorageSettings WithoutEndpoint(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);

        return new StorageSettings(
            Endpoints
                .Where(endpoint => !string.Equals(
                    endpoint.Id,
                    endpointId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray());
    }
}
