using System.Text.Json.Serialization;

namespace Elbwald.DesktopTools.Core.Modules;

public sealed record ModuleDependencyManifest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("minimumVersion")]
    public string? MinimumVersion { get; init; }
}
