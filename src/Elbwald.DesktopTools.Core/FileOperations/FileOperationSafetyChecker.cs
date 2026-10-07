using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class FileOperationSafetyChecker : IFileOperationSafetyChecker
{
    private readonly FileOperationSafetyOptions _options;

    public FileOperationSafetyChecker()
        : this(FileOperationSafetyOptions.Default)
    {
    }

    public FileOperationSafetyChecker(
        FileOperationSafetyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
    }

    public FileOperationPreflightResult Check(
        FileOperationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var issues = new List<FileOperationSafetyIssue>();

        if (!plan.CanExecute)
        {
            issues.Add(new FileOperationSafetyIssue(
                FileOperationSafetyIssueKind.PlanContainsConflicts,
                "Der Dateioperationsplan enthält Konflikte und darf nicht ausgeführt werden."));
        }

        var copyRequirements = new Dictionary<string, VolumeRequirement>(
            GetPathComparer());

        foreach (var operation in plan.Operations)
        {
            CheckOperationCore(
                operation,
                issues,
                copyRequirements);
        }

        CheckFreeSpaceRequirements(
            copyRequirements.Values,
            issues);

        return new FileOperationPreflightResult(issues);
    }

    public FileOperationPreflightResult CheckOperation(
        FileOperationPlanItem operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var issues = new List<FileOperationSafetyIssue>();
        var requirements = new Dictionary<string, VolumeRequirement>(
            GetPathComparer());

        CheckOperationCore(
            operation,
            issues,
            requirements);

        CheckFreeSpaceRequirements(
            requirements.Values,
            issues);

        return new FileOperationPreflightResult(issues);
    }

    private void CheckOperationCore(
        FileOperationPlanItem operation,
        ICollection<FileOperationSafetyIssue> issues,
        IDictionary<string, VolumeRequirement> copyRequirements)
    {
        var sourceVolume = TryResolveVolume(
            operation.SourcePath,
            out var sourceFailure);

        if (sourceVolume is null)
        {
            issues.Add(new FileOperationSafetyIssue(
                FileOperationSafetyIssueKind.StorageVolumeUnknown,
                sourceFailure
                    ?? "Das Quelllaufwerk konnte nicht bestimmt werden.",
                operation.Index));
            return;
        }

        var destinationVolume = TryResolveVolume(
            operation.DestinationPath,
            out var destinationFailure);

        if (destinationVolume is null)
        {
            issues.Add(new FileOperationSafetyIssue(
                FileOperationSafetyIssueKind.StorageVolumeUnknown,
                destinationFailure
                    ?? "Das Ziellaufwerk konnte nicht bestimmt werden.",
                operation.Index));
            return;
        }

        if (operation.Kind == FileOperationKind.Move)
        {
            if (!GetPathComparer().Equals(
                    sourceVolume.Name,
                    destinationVolume.Name))
            {
                issues.Add(new FileOperationSafetyIssue(
                    FileOperationSafetyIssueKind.CrossVolumeMoveBlocked,
                    "Verschieben zwischen unterschiedlichen Datenträgern ist "
                    + "aus Sicherheitsgründen noch gesperrt. "
                    + "Die Quelldatei bleibt unverändert.",
                    operation.Index,
                    destinationVolume.Name));
            }

            return;
        }

        if (operation.Kind != FileOperationKind.Copy)
        {
            return;
        }

        long sourceLength;

        try
        {
            sourceLength = new FileInfo(operation.SourcePath).Length;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            issues.Add(new FileOperationSafetyIssue(
                FileOperationSafetyIssueKind.StorageCapacityUnavailable,
                $"Die Größe der Quelldatei konnte nicht zuverlässig ermittelt werden: "
                + $"{exception.Message}",
                operation.Index,
                destinationVolume.Name));
            return;
        }

        if (!copyRequirements.TryGetValue(
                destinationVolume.Name,
                out var requirement))
        {
            requirement = new VolumeRequirement(destinationVolume);
            copyRequirements.Add(
                destinationVolume.Name,
                requirement);
        }

        try
        {
            requirement.RequiredBytes = checked(
                requirement.RequiredBytes + sourceLength);
        }
        catch (OverflowException)
        {
            requirement.RequiredBytes = long.MaxValue;
        }
    }

    private void CheckFreeSpaceRequirements(
        IEnumerable<VolumeRequirement> requirements,
        ICollection<FileOperationSafetyIssue> issues)
    {
        foreach (var requirement in requirements)
        {
            long totalSize;
            long available;

            try
            {
                totalSize = requirement.Volume.TotalSize;
                available = requirement.Volume.AvailableFreeSpace;
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException)
            {
                issues.Add(new FileOperationSafetyIssue(
                    FileOperationSafetyIssueKind.StorageCapacityUnavailable,
                    $"Der freie Speicher auf '{requirement.Volume.Name}' "
                    + $"konnte nicht zuverlässig bestimmt werden: {exception.Message}",
                    VolumeName: requirement.Volume.Name));
                continue;
            }

            var reserve = _options.CalculateReserveBytes(totalSize);

            long requiredWithReserve;

            try
            {
                requiredWithReserve = checked(
                    requirement.RequiredBytes + reserve);
            }
            catch (OverflowException)
            {
                requiredWithReserve = long.MaxValue;
            }

            if (available < requiredWithReserve)
            {
                issues.Add(new FileOperationSafetyIssue(
                    FileOperationSafetyIssueKind.InsufficientFreeSpace,
                    $"Nicht genügend sicherer freier Speicher auf "
                    + $"'{requirement.Volume.Name}'. "
                    + $"Benötigt einschließlich Sicherheitsreserve: "
                    + $"{FormatBytes(requiredWithReserve)}, verfügbar: "
                    + $"{FormatBytes(available)}.",
                    VolumeName: requirement.Volume.Name,
                    RequiredBytes: requiredWithReserve,
                    AvailableBytes: available));
            }
        }
    }

    private static DriveInfo? TryResolveVolume(
        string path,
        out string? failure)
    {
        failure = null;

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            failure = exception.Message;
            return null;
        }

        DriveInfo[] drives;

        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException)
        {
            failure = exception.Message;
            return null;
        }

        DriveInfo? bestMatch = null;
        var bestLength = -1;

        foreach (var drive in drives)
        {
            string root;

            try
            {
                root = Path.GetFullPath(drive.RootDirectory.FullName);
            }
            catch (Exception)
            {
                continue;
            }

            if (!IsPathWithinRoot(fullPath, root))
            {
                continue;
            }

            if (root.Length > bestLength)
            {
                bestMatch = drive;
                bestLength = root.Length;
            }
        }

        if (bestMatch is null)
        {
            failure = $"Für den Pfad '{fullPath}' wurde kein Datenträger gefunden.";
        }

        return bestMatch;
    }

    private static bool IsPathWithinRoot(
        string fullPath,
        string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        if (string.IsNullOrEmpty(normalizedRoot))
        {
            normalizedRoot = Path.DirectorySeparatorChar.ToString();
        }

        if (string.Equals(
                fullPath,
                normalizedRoot,
                comparison))
        {
            return true;
        }

        var rootWithSeparator =
            normalizedRoot == Path.DirectorySeparatorChar.ToString()
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            rootWithSeparator,
            comparison);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "unbekannt";
        }

        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private sealed class VolumeRequirement
    {
        public VolumeRequirement(DriveInfo volume)
        {
            Volume = volume;
        }

        public DriveInfo Volume { get; }

        public long RequiredBytes { get; set; }
    }
}
