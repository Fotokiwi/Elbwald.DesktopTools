using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Companions;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class MediaCompanionPlanner
    : IMediaCompanionPlanner
{
    private static readonly HashSet<string> RawExtensions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".dng",
            ".cr2",
            ".cr3",
            ".nef",
            ".arw",
            ".raf",
            ".orf",
            ".rw2",
            ".pef"
        };

    private static readonly HashSet<string> JpegExtensions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg"
        };

    private static readonly TimeSpan ReviewDateDifference =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan StrongDateDifference =
        TimeSpan.FromHours(1);

    public IReadOnlyList<MediaCompanionGroupPlan> CreatePlans(
        string destinationRoot,
        IEnumerable<MediaCompanionContextEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationRoot);
        ArgumentNullException.ThrowIfNull(entries);

        var normalizedDestinationRoot =
            Path.GetFullPath(
                destinationRoot);

        var pathComparer =
            OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        var entryList =
            entries.ToArray();

        var entryByPath =
            entryList.ToDictionary(
                entry =>
                    Path.GetFullPath(
                        entry.File.File.FullPath),
                pathComparer);

        var groupedMembers =
            new Dictionary<string, List<MediaCompanionMember>>(
                StringComparer.Ordinal);

        foreach (var entry in entryList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCreateMember(
                    entry.File,
                    out var member))
            {
                continue;
            }

            var key =
                BuildGroupKey(
                    member.File.File.DirectoryPath,
                    member.CompanionStem);

            if (!groupedMembers.TryGetValue(
                    key,
                    out var members))
            {
                members =
                    new List<MediaCompanionMember>();

                groupedMembers.Add(
                    key,
                    members);
            }

            members.Add(
                member);
        }

        var results =
            new List<MediaCompanionGroupPlan>();

        foreach (var members in groupedMembers.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (members.Count < 2)
            {
                continue;
            }

            var imageMembers =
                members
                    .Where(member =>
                        member.Kind
                        is MediaCompanionKind.RawImage
                        or MediaCompanionKind.JpegImage)
                    .ToArray();

            if (imageMembers.Length == 0)
            {
                continue;
            }

            var hasRaw =
                imageMembers.Any(member =>
                    member.Kind
                    == MediaCompanionKind.RawImage);

            var hasJpeg =
                imageMembers.Any(member =>
                    member.Kind
                    == MediaCompanionKind.JpegImage);

            var hasSidecar =
                members.Any(member =>
                    member.Kind
                    == MediaCompanionKind.XmpSidecar);

            if (!(hasRaw && hasJpeg)
                && !hasSidecar)
            {
                continue;
            }

            var imageEntries =
                imageMembers
                    .Select(member =>
                        entryByPath[
                            Path.GetFullPath(
                                member.File.File.FullPath)])
                    .ToArray();

            var plannedImageEntries =
                imageEntries
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(
                            entry.PlannedRelativeDirectory))
                    .ToArray();

            var relativeDirectories =
                plannedImageEntries
                    .Select(entry =>
                        entry.PlannedRelativeDirectory!)
                    .Distinct(pathComparer)
                    .ToArray();

            var state =
                MediaCompanionGroupState.Consistent;

            var messageParts =
                new List<string>
                {
                    BuildRecognitionSummary(
                        members)
                };

            if (plannedImageEntries.Length == 0)
            {
                state =
                    MaxState(
                        state,
                        MediaCompanionGroupState.Review);

                messageParts.Add(
                    "Keine Bilddatei dieser Gruppe ist derzeit im Sortierplan. "
                    + "Companions werden deshalb nicht projiziert.");
            }
            else if (plannedImageEntries.Length
                     < imageEntries.Length)
            {
                state =
                    MaxState(
                        state,
                        MediaCompanionGroupState.Review);

                messageParts.Add(
                    $"{imageEntries.Length - plannedImageEntries.Length:N0} Bild-Companion(s) "
                    + "sind wegen Datums-/Vertrauensregeln nicht im Plan. "
                    + "Die Gruppe darf später nicht teilweise ausgeführt werden.");
            }

            if (relativeDirectories.Length > 1)
            {
                state =
                    MediaCompanionGroupState.Conflict;

                messageParts.Add(
                    "Die Bild-Companions würden in unterschiedliche Zielordner sortiert. "
                    + "Eine spätere Ausführung muss diese Gruppe blockieren, bis der Konflikt geklärt ist.");
            }

            var dateDifference =
                GetMaximumResolvedDateDifference(
                    imageEntries);

            if (dateDifference.HasValue
                && dateDifference.Value
                    > ReviewDateDifference)
            {
                state =
                    MaxState(
                        state,
                        dateDifference.Value
                            > StrongDateDifference
                            ? MediaCompanionGroupState.Conflict
                            : MediaCompanionGroupState.Review);

                messageParts.Add(
                    $"Die gewählten Aufnahmezeiten der Bild-Companions weichen um bis zu "
                    + $"{FormatDifference(dateDifference.Value)} voneinander ab.");
            }

            var projections =
                new List<MediaCompanionProjection>();

            if (relativeDirectories.Length == 1)
            {
                var relativeDirectory =
                    relativeDirectories[0];

                foreach (var sidecar in members.Where(member =>
                             member.Kind
                             == MediaCompanionKind.XmpSidecar))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var projectedDestination =
                        Path.GetFullPath(
                            Path.Combine(
                                normalizedDestinationRoot,
                                relativeDirectory,
                                sidecar.File.File.FileName));

                    var destinationExists =
                        File.Exists(
                            projectedDestination)
                        || Directory.Exists(
                            projectedDestination);

                    projections.Add(
                        new MediaCompanionProjection(
                            sidecar.File.File.FullPath,
                            projectedDestination,
                            sidecar.Kind,
                            destinationExists));

                    if (destinationExists)
                    {
                        state =
                            MediaCompanionGroupState.Conflict;

                        messageParts.Add(
                            $"Für Sidecar '{sidecar.File.File.FileName}' existiert am projizierten Ziel bereits "
                            + "eine Datei oder ein Verzeichnis. Überschreiben bleibt verboten.");
                    }
                }
            }

            messageParts.Add(
                "0034 erkennt und projiziert die Gruppe nur; Sidecars werden noch nicht "
                + "in den Dateioperationsplan aufgenommen.");

            results.Add(
                new MediaCompanionGroupPlan(
                    members[0].File.File.DirectoryPath,
                    members[0].CompanionStem,
                    members,
                    projections,
                    state,
                    string.Join(
                        " ",
                        messageParts)));
        }

        return results
            .OrderByDescending(group =>
                group.State)
            .ThenBy(group =>
                group.DirectoryPath,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group =>
                group.CompanionStem,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool TryCreateMember(
        MediaAnalyzedFile file,
        out MediaCompanionMember member)
    {
        var mediaFile =
            file.File;

        var extension =
            mediaFile.Extension;

        if (RawExtensions.Contains(
                extension))
        {
            member =
                new MediaCompanionMember(
                    file,
                    MediaCompanionKind.RawImage,
                    Path.GetFileNameWithoutExtension(
                        mediaFile.FileName),
                    AssociatedImageFileName: null);

            return true;
        }

        if (JpegExtensions.Contains(
                extension))
        {
            member =
                new MediaCompanionMember(
                    file,
                    MediaCompanionKind.JpegImage,
                    Path.GetFileNameWithoutExtension(
                        mediaFile.FileName),
                    AssociatedImageFileName: null);

            return true;
        }

        if (!string.Equals(
                extension,
                ".xmp",
                StringComparison.OrdinalIgnoreCase))
        {
            member = null!;
            return false;
        }

        var xmpStem =
            Path.GetFileNameWithoutExtension(
                mediaFile.FileName);

        var innerExtension =
            Path.GetExtension(
                xmpStem);

        string companionStem;
        string? associatedImageFileName;

        if (RawExtensions.Contains(
                innerExtension)
            || JpegExtensions.Contains(
                innerExtension))
        {
            companionStem =
                Path.GetFileNameWithoutExtension(
                    xmpStem);

            associatedImageFileName =
                xmpStem;
        }
        else
        {
            companionStem =
                xmpStem;

            associatedImageFileName =
                null;
        }

        member =
            new MediaCompanionMember(
                file,
                MediaCompanionKind.XmpSidecar,
                companionStem,
                associatedImageFileName);

        return true;
    }

    private static string BuildGroupKey(
        string directoryPath,
        string stem)
    {
        var normalizedDirectory =
            Path.GetFullPath(
                directoryPath);

        if (OperatingSystem.IsWindows())
        {
            normalizedDirectory =
                normalizedDirectory.ToUpperInvariant();
        }

        return normalizedDirectory
               + "\u001F"
               + stem.ToUpperInvariant();
    }

    private static TimeSpan? GetMaximumResolvedDateDifference(
        IReadOnlyList<MediaCompanionContextEntry> imageEntries)
    {
        var values =
            imageEntries
                .Select(entry =>
                    entry.DateResolution?.Value)
                .Where(value =>
                    value.HasValue)
                .Select(value =>
                    value!.Value)
                .OrderBy(value =>
                    value)
                .ToArray();

        if (values.Length < 2)
        {
            return null;
        }

        return values[^1]
               - values[0];
    }

    private static MediaCompanionGroupState MaxState(
        MediaCompanionGroupState left,
        MediaCompanionGroupState right)
    {
        return (MediaCompanionGroupState)Math.Max(
            (int)left,
            (int)right);
    }

    private static string BuildRecognitionSummary(
        IReadOnlyList<MediaCompanionMember> members)
    {
        var rawCount =
            members.Count(member =>
                member.Kind
                == MediaCompanionKind.RawImage);

        var jpegCount =
            members.Count(member =>
                member.Kind
                == MediaCompanionKind.JpegImage);

        var sidecarCount =
            members.Count(member =>
                member.Kind
                == MediaCompanionKind.XmpSidecar);

        return $"Companion-Gruppe erkannt: "
               + $"{rawCount:N0} RAW, "
               + $"{jpegCount:N0} JPEG, "
               + $"{sidecarCount:N0} XMP.";
    }

    private static string FormatDifference(
        TimeSpan difference)
    {
        var absolute =
            difference.Duration();

        if (absolute.TotalDays >= 1)
        {
            return $"{(int)absolute.TotalDays}d "
                   + $"{absolute.Hours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
        }

        return $"{(int)absolute.TotalHours:00}:{absolute.Minutes:00}:{absolute.Seconds:00}";
    }
}
