using System.Text.RegularExpressions;

namespace Elbwald.DesktopTools.App.ViewModels;

internal static partial class DuplicateFinderConfidenceAnalyzer
{
    public static DuplicateFinderConfidenceResult Analyze(
        IReadOnlyList<DuplicateFinderLocationItemViewModel> locations)
    {
        ArgumentNullException.ThrowIfNull(locations);

        var candidates = locations
            .Where(location => !location.IsProtectedEndpoint)
            .ToArray();

        if (candidates.Length < 2)
        {
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.Unclear,
                null,
                "Keine automatische Bereinigung: Es gibt weniger als zwei ungeschützte Fundorte.");
        }

        var descriptors = candidates
            .Select(CreateDescriptor)
            .ToArray();

        if (descriptors.Select(item => item.NormalizedFileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != 1)
        {
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.Unclear,
                null,
                "Die bitidentischen Dateien tragen fachlich unterschiedliche Namen. Eine automatische Referenzwahl wäre zu spekulativ.");
        }

        var sameEndpoint = descriptors
            .Select(item => item.Location.EndpointId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1;

        var sameDirectory = sameEndpoint
            && descriptors.Select(item => item.Directory)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

        var uniquePlainFile = descriptors
            .Where(item => !item.FileNameLooksLikeCopy)
            .ToArray();

        if (sameDirectory
            && uniquePlainFile.Length == 1
            && descriptors.Any(item => item.FileNameLooksLikeCopy))
        {
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.VerySafe,
                uniquePlainFile[0].Location.LocationId,
                "Gleicher Ordner und gleicher normalisierter Dateiname; genau eine Datei besitzt keinen typischen Kopie-Zusatz.");
        }

        var normalizedDirectoryCount = descriptors
            .Select(item => item.NormalizedDirectory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var uniquePlainDirectory = descriptors
            .Where(item => !item.DirectoryLooksLikeCopy)
            .ToArray();

        if (sameEndpoint
            && normalizedDirectoryCount == 1
            && uniquePlainDirectory.Length == 1
            && descriptors.Any(item => item.DirectoryLooksLikeCopy))
        {
            var keeper = uniquePlainDirectory[0];
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.VerySafe,
                keeper.Location.LocationId,
                "Parallele Verzeichnisse unterscheiden sich nur durch typische Kopie-Zusätze; der ursprüngliche Verzeichnispfad ist eindeutig.");
        }

        if (uniquePlainFile.Length == 1
            && descriptors.Any(item => item.FileNameLooksLikeCopy))
        {
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.Likely,
                uniquePlainFile[0].Location.LocationId,
                "Der Dateiname weist auf Original plus Kopie hin, die Dateien liegen aber nicht eindeutig im selben bzw. parallelen Verzeichnis.");
        }

        var exactFileNameCount = descriptors
            .Select(item => item.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (exactFileNameCount == 1)
        {
            return new DuplicateFinderConfidenceResult(
                DuplicateFinderConfidenceLevel.Likely,
                null,
                "Identischer Dateiname an mehreren Fundorten, aber ohne eindeutiges Merkmal dafür, welcher Pfad die Referenz sein soll.");
        }

        return new DuplicateFinderConfidenceResult(
            DuplicateFinderConfidenceLevel.Likely,
            uniquePlainFile.FirstOrDefault()?.Location.LocationId,
            "Die Namen lassen sich auf denselben Basisnamen normalisieren, die Pfadstruktur liefert aber keine eindeutige automatische Referenz.");
    }

    private static Descriptor CreateDescriptor(DuplicateFinderLocationItemViewModel location)
    {
        var relativePath = NormalizeSeparators(location.RelativePath);
        var fileName = Path.GetFileName(relativePath);
        var directory = Path.GetDirectoryName(relativePath) ?? string.Empty;
        directory = NormalizeSeparators(directory);

        var fileStem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var normalizedStem = NormalizeCopySuffix(fileStem, out var fileLooksLikeCopy);
        var normalizedDirectory = NormalizeDirectory(directory, out var directoryLooksLikeCopy);

        return new Descriptor(
            location,
            fileName,
            directory,
            normalizedStem + extension,
            normalizedDirectory,
            fileLooksLikeCopy,
            directoryLooksLikeCopy);
    }

    private static string NormalizeDirectory(string directory, out bool looksLikeCopy)
    {
        looksLikeCopy = false;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var normalizedParts = new List<string>();
        foreach (var part in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = NormalizeCopySuffix(part, out var partLooksLikeCopy);
            looksLikeCopy |= partLooksLikeCopy;
            normalizedParts.Add(normalized);
        }

        return string.Join('/', normalizedParts).ToLowerInvariant();
    }

    private static string NormalizeCopySuffix(string value, out bool looksLikeCopy)
    {
        var normalized = value.Trim();
        looksLikeCopy = false;

        while (true)
        {
            var next = CopySuffixRegex().Replace(normalized, string.Empty).Trim();
            if (string.Equals(next, normalized, StringComparison.Ordinal))
            {
                break;
            }

            normalized = next;
            looksLikeCopy = true;
        }

        return normalized.ToLowerInvariant();
    }

    private static string NormalizeSeparators(string value) =>
        value.Replace('\\', '/').Trim('/');

    [GeneratedRegex(@"(?:\s+|[-_]\s*)(?:kopie|copy|duplicate|duplikat)(?:\s*[-_]?\s*\d{1,3})?$|\s*\(\d{1,2}\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CopySuffixRegex();

    private sealed record Descriptor(
        DuplicateFinderLocationItemViewModel Location,
        string FileName,
        string Directory,
        string NormalizedFileName,
        string NormalizedDirectory,
        bool FileNameLooksLikeCopy,
        bool DirectoryLooksLikeCopy);
}

internal sealed record DuplicateFinderConfidenceResult(
    DuplicateFinderConfidenceLevel Level,
    long? SuggestedKeeperLocationId,
    string Reason);
