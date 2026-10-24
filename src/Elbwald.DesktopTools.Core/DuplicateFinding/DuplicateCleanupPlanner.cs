using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.DuplicateFinding;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;

namespace Elbwald.DesktopTools.Core.DuplicateFinding;

public sealed class DuplicateCleanupPlanner : IDuplicateCleanupPlanner
{
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;

    public DuplicateCleanupPlanner(
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);

        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;
    }

    public async Task<DuplicateCleanupPreflightReport> RunPreflightAsync(
        DuplicateCleanupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var globalIssues = new List<string>();
        var items = new List<DuplicateCleanupPreflightItem>();

        if (string.IsNullOrWhiteSpace(plan.MediaItemId)
            || string.IsNullOrWhiteSpace(plan.ExpectedSha256)
            || plan.ExpectedLength < 0)
        {
            globalIssues.Add("Der Bereinigungsplan ist unvollständig oder ungültig.");
            return Complete(plan, startedAtUtc, items, globalIssues);
        }

        if (plan.KeeperLocationId <= 0)
        {
            globalIssues.Add("Es wurde kein gültiger Referenz-Fundort ausgewählt.");
        }

        var removalIds = plan.RemovalLocationIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (removalIds.Length == 0)
        {
            globalIssues.Add("Es wurde kein Bereinigungskandidat ausgewählt.");
        }

        if (removalIds.Contains(plan.KeeperLocationId))
        {
            globalIssues.Add("Der Referenz-Fundort darf nicht gleichzeitig entfernt werden.");
        }

        var indexedItem = await _mediaIndexService.GetItemAsync(
            plan.MediaItemId,
            cancellationToken);

        var indexIdentityChanged = indexedItem is null
            || indexedItem.Length != plan.ExpectedLength
            || !string.Equals(indexedItem.Sha256, plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase);

        if (indexIdentityChanged)
        {
            globalIssues.Add("Die Medienidentität im Index hat sich seit Erstellung des Plans geändert. Bitte Dubletten neu prüfen.");
        }

        var indexedLocations = await _mediaIndexService.GetLocationsAsync(
            plan.MediaItemId,
            cancellationToken);
        var settings = await _storageSettingsStore.LoadAsync(cancellationToken);

        var byId = indexedLocations.ToDictionary(location => location.Id);
        var selectedIds = new[] { plan.KeeperLocationId }
            .Concat(removalIds)
            .Distinct()
            .ToArray();

        foreach (var locationId in selectedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = locationId == plan.KeeperLocationId
                ? DuplicateCleanupAction.Keep
                : DuplicateCleanupAction.Remove;

            if (!byId.TryGetValue(locationId, out var location)
                || !location.IsPresent)
            {
                items.Add(new DuplicateCleanupPreflightItem(
                    locationId,
                    action,
                    string.Empty,
                    string.Empty,
                    null,
                    DuplicateCleanupPreflightStatus.IndexChanged,
                    "Der Fundort ist im aktuellen Medienindex nicht mehr als vorhanden markiert."));
                continue;
            }

            if (indexIdentityChanged)
            {
                items.Add(Failed(
                    location,
                    action,
                    null,
                    DuplicateCleanupPreflightStatus.IndexChanged,
                    "Die Dublettengruppe wurde seit Erstellung des Plans im Medienindex geändert."));
                continue;
            }

            items.Add(await InspectLocationAsync(
                plan,
                location,
                action,
                settings,
                cancellationToken));
        }

        return Complete(plan, startedAtUtc, items, globalIssues);
    }

    private async Task<DuplicateCleanupPreflightItem> InspectLocationAsync(
        DuplicateCleanupPlan plan,
        MediaIndexLocation location,
        DuplicateCleanupAction action,
        StorageSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location.EndpointId))
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.EndpointMissing,
                "Der Fundort besitzt keine gültige Speicher-Endpunkt-ID.");
        }

        var endpoint = settings.Find(location.EndpointId);

        if (endpoint is null)
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.EndpointMissing,
                "Der zugehörige Speicher-Endpunkt existiert nicht mehr in den Einstellungen.");
        }

        if (!endpoint.Enabled)
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.EndpointDisabled,
                "Der Speicher-Endpunkt ist deaktiviert und wird für Bereinigungsaktionen nicht verwendet.");
        }

        if (endpoint.Kind is StorageEndpointKind.Backup or StorageEndpointKind.Archive)
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.ProtectedEndpoint,
                "Backup- und Archiv-Endpunkte sind vollständig aus generischen Dublettenbereinigungsplänen ausgeschlossen.");
        }

        if (!string.Equals(
                endpoint.Location.VolumeId,
                location.VolumeId,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.VolumeChanged,
                "Die Laufwerkskennung des Fundorts stimmt nicht mehr mit dem konfigurierten Speicher-Endpunkt überein.");
        }

        var rootResolution = _storageLocationResolver.Resolve(endpoint.Location);
        if (!rootResolution.IsAvailable || string.IsNullOrWhiteSpace(rootResolution.ResolvedPath))
        {
            return Failed(
                location,
                action,
                rootResolution.ResolvedPath,
                DuplicateCleanupPreflightStatus.EndpointUnavailable,
                rootResolution.Message);
        }

        if (!TryResolveFilePath(
                rootResolution.ResolvedPath,
                location.RelativePath,
                out var resolvedPath))
        {
            return Failed(
                location,
                action,
                null,
                DuplicateCleanupPreflightStatus.InvalidRelativePath,
                "Der gespeicherte relative Pfad verlässt den konfigurierten Speicher-Endpunkt oder ist ungültig.");
        }

        if (!File.Exists(resolvedPath))
        {
            return Failed(
                location,
                action,
                resolvedPath,
                DuplicateCleanupPreflightStatus.FileMissing,
                "Die Datei ist am erwarteten Fundort nicht mehr vorhanden.");
        }

        try
        {
            var fileInfo = new FileInfo(resolvedPath);

            if (!string.IsNullOrWhiteSpace(fileInfo.LinkTarget))
            {
                return Failed(
                    location,
                    action,
                    resolvedPath,
                    DuplicateCleanupPreflightStatus.SymbolicLinkUnsupported,
                    "Symbolische Links werden für sichere Bereinigungspläne nicht verwendet.");
            }

            if (fileInfo.Length != plan.ExpectedLength)
            {
                return Failed(
                    location,
                    action,
                    resolvedPath,
                    DuplicateCleanupPreflightStatus.LengthMismatch,
                    $"Die Dateigröße hat sich geändert ({fileInfo.Length:N0} statt {plan.ExpectedLength:N0} Byte)." );
            }

            var hashResult = await ComputeStableSha256Async(resolvedPath, cancellationToken);
            if (!hashResult.IsStable)
            {
                return Failed(
                    location,
                    action,
                    resolvedPath,
                    DuplicateCleanupPreflightStatus.IoError,
                    "Die Datei hat sich während der Sicherheitsprüfung verändert. Bitte erneut prüfen.");
            }

            if (!string.Equals(hashResult.Sha256, plan.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    location,
                    action,
                    resolvedPath,
                    DuplicateCleanupPreflightStatus.HashMismatch,
                    "Der aktuelle SHA-256 stimmt nicht mehr mit der Dublettengruppe überein.");
            }

            if (action == DuplicateCleanupAction.Remove)
            {
                if (fileInfo.IsReadOnly)
                {
                    return Failed(
                        location,
                        action,
                        resolvedPath,
                        DuplicateCleanupPreflightStatus.ReadOnly,
                        "Die Datei ist schreibgeschützt und wird nicht als sicherer Bereinigungskandidat freigegeben.");
                }

                await using var writeProbe = new FileStream(
                    resolvedPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    bufferSize: 1,
                    options: FileOptions.None);
            }

            return new DuplicateCleanupPreflightItem(
                location.Id,
                action,
                location.EndpointId,
                location.RelativePath,
                resolvedPath,
                DuplicateCleanupPreflightStatus.Ready,
                action == DuplicateCleanupAction.Keep
                    ? "Referenzdatei vorhanden; Größe und SHA-256 stimmen mit dem Index überein."
                    : "Bereinigungskandidat vorhanden; Größe und SHA-256 stimmen und die Datei lässt sich mit Schreibzugriff öffnen.");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(
                location,
                action,
                resolvedPath,
                DuplicateCleanupPreflightStatus.AccessDenied,
                "Zugriff verweigert: " + exception.Message);
        }
        catch (IOException exception)
        {
            return Failed(
                location,
                action,
                resolvedPath,
                DuplicateCleanupPreflightStatus.IoError,
                "Dateizugriff fehlgeschlagen: " + exception.Message);
        }
    }

    private static DuplicateCleanupPreflightReport Complete(
        DuplicateCleanupPlan plan,
        DateTimeOffset startedAtUtc,
        IReadOnlyList<DuplicateCleanupPreflightItem> items,
        IReadOnlyList<string> globalIssues) =>
        new(
            startedAtUtc,
            DateTimeOffset.UtcNow,
            plan,
            items,
            globalIssues);

    private static DuplicateCleanupPreflightItem Failed(
        MediaIndexLocation location,
        DuplicateCleanupAction action,
        string? resolvedPath,
        DuplicateCleanupPreflightStatus status,
        string message) =>
        new(
            location.Id,
            action,
            location.EndpointId,
            location.RelativePath,
            resolvedPath,
            status,
            message);

    private static bool TryResolveFilePath(
        string rootPath,
        string relativePath,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(rootPath)
            || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var parts = relativePath.Split(
            ['/', '\\'],
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(part => part is "." or ".."))
        {
            return false;
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var rootWithSeparator = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        var candidate = parts.Length == 0
            ? normalizedRoot
            : Path.GetFullPath(Path.Combine(normalizedRoot, Path.Combine(parts)));

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    private static async Task<StableHashResult> ComputeStableSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        var before = new FileInfo(path);
        var beforeLength = before.Length;
        var beforeWriteTime = before.LastWriteTimeUtc;

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        var after = new FileInfo(path);
        var isStable = beforeLength == after.Length
            && beforeWriteTime == after.LastWriteTimeUtc;

        return new StableHashResult(
            Convert.ToHexString(hash),
            isStable);
    }

    private sealed record StableHashResult(
        string Sha256,
        bool IsStable);
}
