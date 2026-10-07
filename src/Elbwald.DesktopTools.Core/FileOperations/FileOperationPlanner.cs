using Elbwald.DesktopTools.Contracts.FileOperations;

namespace Elbwald.DesktopTools.Core.FileOperations;

public sealed class FileOperationPlanner : IFileOperationPlanner
{
    public FileOperationPlan CreatePlan(
        IEnumerable<FileOperationRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var pathComparer = GetPathComparer();
        var operations = new List<FileOperationPlanItem>();
        var conflicts = new List<FileOperationConflict>();

        var firstSourceIndex = new Dictionary<string, int>(pathComparer);
        var firstDestinationIndex = new Dictionary<string, int>(pathComparer);

        var requestIndex = 0;

        foreach (var request in requests)
        {
            if (request is null)
            {
                throw new ArgumentException(
                    "Die Liste der Dateioperationen darf keine null-Einträge enthalten.",
                    nameof(requests));
            }

            var sourceValid = TryNormalizePath(
                request.SourcePath,
                out var sourcePath);

            var destinationValid = TryNormalizePath(
                request.DestinationPath,
                out var destinationPath);

            var operation = new FileOperationPlanItem(
                requestIndex,
                request.Kind,
                sourcePath ?? request.SourcePath ?? string.Empty,
                destinationPath ?? request.DestinationPath ?? string.Empty);

            operations.Add(operation);

            if (!sourceValid)
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.InvalidSourcePath,
                    requestIndex));
            }

            if (!destinationValid)
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.InvalidDestinationPath,
                    requestIndex));
            }

            if (sourceValid && !File.Exists(operation.SourcePath))
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.SourceFileMissing,
                    requestIndex));
            }

            if (sourceValid
                && destinationValid
                && pathComparer.Equals(
                    operation.SourcePath,
                    operation.DestinationPath))
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.SameSourceAndDestination,
                    requestIndex));
            }

            if (destinationValid
                && (File.Exists(operation.DestinationPath)
                    || Directory.Exists(operation.DestinationPath)))
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.DestinationAlreadyExists,
                    requestIndex));
            }

            if (sourceValid)
            {
                if (firstSourceIndex.TryGetValue(
                        operation.SourcePath,
                        out var existingSourceIndex))
                {
                    conflicts.Add(new FileOperationConflict(
                        FileOperationConflictKind.SourceUsedMultipleTimes,
                        requestIndex,
                        existingSourceIndex));
                }
                else
                {
                    firstSourceIndex.Add(
                        operation.SourcePath,
                        requestIndex);
                }
            }

            if (destinationValid)
            {
                if (firstDestinationIndex.TryGetValue(
                        operation.DestinationPath,
                        out var existingDestinationIndex))
                {
                    conflicts.Add(new FileOperationConflict(
                        FileOperationConflictKind.DuplicateDestination,
                        requestIndex,
                        existingDestinationIndex));
                }
                else
                {
                    firstDestinationIndex.Add(
                        operation.DestinationPath,
                        requestIndex);
                }
            }

            requestIndex++;
        }

        foreach (var operation in operations)
        {
            if (!TryNormalizePath(operation.DestinationPath, out var destinationPath)
                || destinationPath is null)
            {
                continue;
            }

            if (firstSourceIndex.TryGetValue(
                    destinationPath,
                    out var sourceOperationIndex)
                && sourceOperationIndex != operation.Index)
            {
                conflicts.Add(new FileOperationConflict(
                    FileOperationConflictKind.DestinationIsAnotherSource,
                    operation.Index,
                    sourceOperationIndex));
            }
        }

        return new FileOperationPlan(
            operations,
            conflicts);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static bool TryNormalizePath(
        string? path,
        out string? normalizedPath)
    {
        normalizedPath = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return false;
        }
    }
}
