using Elbwald.DesktopTools.Contracts.Media;
using Elbwald.DesktopTools.Contracts.Media.Decoding;
using Elbwald.DesktopTools.Contracts.Media.Editing;
using Elbwald.DesktopTools.Contracts.MediaIndex;
using Elbwald.DesktopTools.Contracts.Storage;
using Elbwald.DesktopTools.Image.Editor.Basic.Rendering;

namespace Elbwald.DesktopTools.Image.Editor.Basic.Preview;

/// <summary>
/// Resolves an indexed image to a currently readable source, decodes a bounded preview
/// and renders either the persisted or an in-memory edit document. The source file is
/// opened only by the selected decoder and is never modified by this service.
/// </summary>
public sealed class ImageEditPreviewService : IImageEditPreviewService
{
    private readonly IMediaIndexService _mediaIndexService;
    private readonly IStorageSettingsStore _storageSettingsStore;
    private readonly IStorageLocationResolver _storageLocationResolver;
    private readonly IImageDecodeRegistry _decodeRegistry;
    private readonly IImageEditDocumentStore _documentStore;
    private readonly ImageEditRenderer _renderer;

    public ImageEditPreviewService(
        IMediaIndexService mediaIndexService,
        IStorageSettingsStore storageSettingsStore,
        IStorageLocationResolver storageLocationResolver,
        IImageDecodeRegistry decodeRegistry,
        IImageEditDocumentStore documentStore,
        ImageEditRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(mediaIndexService);
        ArgumentNullException.ThrowIfNull(storageSettingsStore);
        ArgumentNullException.ThrowIfNull(storageLocationResolver);
        ArgumentNullException.ThrowIfNull(decodeRegistry);
        ArgumentNullException.ThrowIfNull(documentStore);
        ArgumentNullException.ThrowIfNull(renderer);

        _mediaIndexService = mediaIndexService;
        _storageSettingsStore = storageSettingsStore;
        _storageLocationResolver = storageLocationResolver;
        _decodeRegistry = decodeRegistry;
        _documentStore = documentStore;
        _renderer = renderer;
    }

    public async Task<ImageEditPreviewResult> RenderAsync(
        ImageEditPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        var indexedItem = await _mediaIndexService
            .GetItemAsync(request.MediaItemId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException(
                $"Media-ID '{request.MediaItemId}' ist nicht mehr im Medienindex vorhanden.");

        if (indexedItem.MediaType != MediaFileType.Image)
        {
            throw new InvalidOperationException(
                $"Media-ID '{request.MediaItemId}' ist kein Bild und kann nicht im Bildeditor geöffnet werden.");
        }

        var sourceFile = await ResolveSourceFileAsync(
                indexedItem,
                cancellationToken)
            .ConfigureAwait(false);

        var document = request.DocumentOverride
            ?? await _documentStore
                .GetAsync(request.MediaItemId, cancellationToken)
                .ConfigureAwait(false)
            ?? ImageEditDocument.Empty(request.MediaItemId);

        document.Validate();

        var providers = _decodeRegistry.FindProviders(sourceFile);
        if (providers.Count == 0)
        {
            throw new NotSupportedException(
                $"Für '{sourceFile.Extension}' ist aktuell kein aktiver Bilddecoder verfügbar.");
        }

        var decoder = providers[0];
        var sourceFrame = await decoder
            .DecodeAsync(
                new ImageDecodeRequest(
                    sourceFile,
                    request.MaxEdge,
                    ApplyEmbeddedOrientation: true),
                cancellationToken)
            .ConfigureAwait(false);

        var renderedFrame = await _renderer
            .RenderAsync(sourceFrame, document.Operations, cancellationToken)
            .ConfigureAwait(false);

        return new ImageEditPreviewResult(
            request.MediaItemId,
            sourceFile,
            decoder.ProviderId,
            document,
            sourceFrame,
            renderedFrame);
    }

    private async Task<MediaFile> ResolveSourceFileAsync(
        MediaIndexItem indexedItem,
        CancellationToken cancellationToken)
    {
        var locations = (await _mediaIndexService
                .GetLocationsAsync(indexedItem.Id, cancellationToken)
                .ConfigureAwait(false))
            .Where(location => location.IsPresent)
            .OrderBy(location => location.Id)
            .ToArray();

        if (locations.Length == 0)
        {
            throw new FileNotFoundException(
                $"Für Media-ID '{indexedItem.Id}' ist aktuell kein vorhandener Speicherort registriert.");
        }

        var settings = await _storageSettingsStore
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var location in locations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endpoint = settings.Find(location.EndpointId);
            if (endpoint is null || !endpoint.Enabled)
            {
                continue;
            }

            var resolution = _storageLocationResolver.Resolve(endpoint.Location);
            if (!resolution.IsAvailable || string.IsNullOrWhiteSpace(resolution.ResolvedPath))
            {
                continue;
            }

            if (!TryBuildAbsolutePath(
                    resolution.ResolvedPath,
                    location.RelativePath,
                    out var absolutePath))
            {
                continue;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(absolutePath);
                if (!fileInfo.Exists)
                {
                    continue;
                }

                if (fileInfo.Length != indexedItem.Length)
                {
                    continue;
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
            {
                continue;
            }

            return new MediaFile(
                fileInfo.FullName,
                fileInfo.Name,
                fileInfo.Extension,
                fileInfo.Length,
                fileInfo.CreationTimeUtc,
                fileInfo.LastWriteTimeUtc,
                MediaFileType.Image,
                fileInfo.LinkTarget is not null);
        }

        throw new FileNotFoundException(
            $"Für Media-ID '{indexedItem.Id}' konnte keine lesbare, zum Index passende Quelldatei aufgelöst werden.");
    }

    private static bool TryBuildAbsolutePath(
        string root,
        string relativePath,
        out string absolutePath)
    {
        absolutePath = string.Empty;

        if (string.IsNullOrWhiteSpace(root)
            || string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
            var relativeToRoot = Path.GetRelativePath(fullRoot, candidate);

            if (relativeToRoot.Equals("..", StringComparison.Ordinal)
                || relativeToRoot.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)
                || Path.IsPathRooted(relativeToRoot))
            {
                return false;
            }

            absolutePath = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return false;
        }
    }
}
