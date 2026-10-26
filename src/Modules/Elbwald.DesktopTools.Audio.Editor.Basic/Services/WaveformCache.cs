using System.Collections.Concurrent;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Session cache for waveform envelopes. A cache key includes size and write
/// timestamp so replacing a source file automatically invalidates stale peaks.
/// </summary>
public sealed class WaveformCache
{
    private readonly WavSampleDecoder _decoder;
    private readonly ConcurrentDictionary<WaveformCacheKey, Lazy<Task<WaveformEnvelope>>> _entries = new();

    public WaveformCache(WavSampleDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        _decoder = decoder;
    }

    public async Task<WaveformEnvelope> GetAsync(
        string filePath,
        int targetPeakCount = 8192,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Die Audio-Quelldatei wurde nicht gefunden.", fullPath);
        }

        var key = new WaveformCacheKey(
            fullPath,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc.Ticks,
            targetPeakCount);

        var lazy = _entries.GetOrAdd(
            key,
            static (cacheKey, decoder) =>
                new Lazy<Task<WaveformEnvelope>>(
                    () => decoder.BuildWaveformAsync(cacheKey.Path, cacheKey.TargetPeakCount),
                    LazyThreadSafetyMode.ExecutionAndPublication),
            _decoder);

        try
        {
            // Do not pass the caller token into the shared cache task: cancelling one
            // UI request must not cancel an envelope another clip is already using.
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _entries.TryRemove(key, out _);
            throw;
        }
    }

    public void Invalidate(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);

        foreach (var key in _entries.Keys.Where(
                     key => string.Equals(key.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            _entries.TryRemove(key, out _);
        }
    }

    private readonly record struct WaveformCacheKey(
        string Path,
        long Length,
        long LastWriteTicks,
        int TargetPeakCount);
}
