using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Formats;
using Elbwald.DesktopTools.Contracts.Modules;

namespace Elbwald.DesktopTools.Core.Media.Formats;

/// <summary>
/// Immutable runtime registry that combines host-owned compatibility formats
/// with validated extension format providers. Multiple providers may recognize
/// the same extension. Detection only returns a media type when all matching
/// providers agree on that type.
/// </summary>
public sealed class MediaFormatRegistry : IMediaFormatRegistry
{
    private readonly IMediaFormatProvider[] _providers;
    private readonly IReadOnlyDictionary<string, IMediaFormatProvider[]> _providersByExtension;
    private readonly IReadOnlyDictionary<string, IMediaFormatProvider[]> _providersByFormatId;

    public MediaFormatRegistry(
        IExtensionRegistry extensionRegistry,
        IEnumerable<IMediaFormatProvider> builtInProviders)
    {
        ArgumentNullException.ThrowIfNull(extensionRegistry);
        ArgumentNullException.ThrowIfNull(builtInProviders);

        var extensionProviders = extensionRegistry
            .GetProviders<IMediaFormatProvider>(MediaCapabilityIds.FormatProvider);

        _providers = builtInProviders
            .Concat(extensionProviders)
            .Select(ValidateProvider)
            .OrderBy(provider => provider.FormatId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        _providersByExtension = BuildExtensionIndex(_providers);
        _providersByFormatId = _providers
            .GroupBy(provider => provider.FormatId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IMediaFormatProvider> Providers => _providers;

    public IReadOnlyList<IMediaFormatProvider> FindByFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = NormalizeExtension(Path.GetExtension(filePath));

        if (extension.Length == 0)
        {
            return [];
        }

        return _providersByExtension.TryGetValue(extension, out var providers)
            ? providers
            : [];
    }

    public IReadOnlyList<IMediaFormatProvider> FindByFormatId(string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);

        return _providersByFormatId.TryGetValue(formatId.Trim(), out var providers)
            ? providers
            : [];
    }

    public MediaFileType DetectMediaType(string filePath)
    {
        var matches = FindByFilePath(filePath);

        if (matches.Count == 0)
        {
            return MediaFileType.Unknown;
        }

        var mediaTypes = matches
            .Select(provider => provider.MediaType)
            .Distinct()
            .Take(2)
            .ToArray();

        return mediaTypes.Length == 1
            ? mediaTypes[0]
            : MediaFileType.Unknown;
    }

    private static IReadOnlyDictionary<string, IMediaFormatProvider[]> BuildExtensionIndex(
        IEnumerable<IMediaFormatProvider> providers)
    {
        var index = new Dictionary<string, List<IMediaFormatProvider>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            foreach (var extension in provider.FileExtensions)
            {
                var normalizedExtension = NormalizeExtension(extension);

                if (!index.TryGetValue(normalizedExtension, out var matches))
                {
                    matches = [];
                    index.Add(normalizedExtension, matches);
                }

                matches.Add(provider);
            }
        }

        return index.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IMediaFormatProvider ValidateProvider(IMediaFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.FormatId))
        {
            throw new InvalidOperationException(
                $"Media-Format-Provider '{provider.GetType().FullName}' hat keine Format-ID.");
        }

        if (provider.MediaType == MediaFileType.Unknown)
        {
            throw new InvalidOperationException(
                $"Media-Format-Provider '{provider.FormatId}' darf nicht MediaFileType.Unknown liefern.");
        }

        if (provider.FileExtensions is null || provider.FileExtensions.Count == 0)
        {
            throw new InvalidOperationException(
                $"Media-Format-Provider '{provider.FormatId}' deklariert keine Dateiendungen.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fileExtension in provider.FileExtensions)
        {
            var normalized = NormalizeExtension(fileExtension);

            if (normalized.Length <= 1)
            {
                throw new InvalidOperationException(
                    $"Media-Format-Provider '{provider.FormatId}' enthält eine ungültige Dateiendung.");
            }

            if (!seen.Add(normalized))
            {
                throw new InvalidOperationException(
                    $"Media-Format-Provider '{provider.FormatId}' deklariert '{fileExtension}' mehrfach.");
            }
        }

        return provider;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var normalized = extension.Trim();

        if (!normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = "." + normalized;
        }

        return normalized.ToLowerInvariant();
    }
}
