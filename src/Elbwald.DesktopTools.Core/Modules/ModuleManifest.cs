using System.Text.Json.Serialization;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed record ModuleManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("apiVersion")]
    public required int ApiVersion { get; init; }

    [JsonPropertyName("minimumHostVersion")]
    public required string MinimumHostVersion { get; init; }

    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}
