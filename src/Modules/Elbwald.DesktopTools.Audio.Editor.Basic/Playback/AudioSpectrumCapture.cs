namespace Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

/// <summary>
/// Immutable snapshot of the latest mono analysis window for one track.
/// The mixer writes only time-domain samples; FFT work stays outside the
/// playback/rendering path.
/// </summary>
public sealed record AudioSpectrumSampleSnapshot(
    int SampleRate,
    long Version,
    float[] PreEqSamples,
    float[] PostEqSamples)
{
    public static AudioSpectrumSampleSnapshot Empty { get; } =
        new(0, 0, [], []);

    public bool HasSamples => SampleRate > 0 && (PreEqSamples.Length > 0 || PostEqSamples.Length > 0);
}

/// <summary>
/// Thread-safe rolling sample tap for the one track currently shown in the
/// detachable analysis window. Keeping this selective avoids analysis work on
/// all 16 tracks when the window only displays one.
/// </summary>
internal sealed class LiveSpectrumCapture
{
    private const int CapacityFrames = 4096;

    private readonly object _gate = new();
    private readonly float[] _preEq = new float[CapacityFrames];
    private readonly float[] _postEq = new float[CapacityFrames];

    private Guid? _targetTrackId;
    private int _preWriteIndex;
    private int _preCount;
    private int _postWriteIndex;
    private int _postCount;
    private int _sampleRate;
    private long _version;

    public void SetTarget(Guid? trackId, int sampleRate)
    {
        lock (_gate)
        {
            if (_targetTrackId == trackId && _sampleRate == sampleRate)
            {
                return;
            }

            _targetTrackId = trackId;
            _sampleRate = sampleRate;
            _preWriteIndex = 0;
            _preCount = 0;
            _postWriteIndex = 0;
            _postCount = 0;
            Array.Clear(_preEq);
            Array.Clear(_postEq);
            _version++;
        }
    }

    public bool IsTarget(Guid trackId)
    {
        lock (_gate)
        {
            return _targetTrackId == trackId;
        }
    }

    public void CapturePreEq(Guid trackId, float[] interleavedStereo)
    {
        lock (_gate)
        {
            if (_targetTrackId != trackId)
            {
                return;
            }

            AppendStereoAsMono(interleavedStereo, _preEq, ref _preWriteIndex, ref _preCount);
        }
    }

    public void CapturePostEq(Guid trackId, float[] interleavedStereo)
    {
        lock (_gate)
        {
            if (_targetTrackId != trackId)
            {
                return;
            }

            AppendStereoAsMono(interleavedStereo, _postEq, ref _postWriteIndex, ref _postCount);
            _version++;
        }
    }

    public void CaptureSilence(Guid trackId, int frameCount)
    {
        if (frameCount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_targetTrackId != trackId)
            {
                return;
            }

            AppendSilence(frameCount, _preEq, ref _preWriteIndex, ref _preCount);
            AppendSilence(frameCount, _postEq, ref _postWriteIndex, ref _postCount);
            _version++;
        }
    }

    public AudioSpectrumSampleSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_targetTrackId is null || _sampleRate <= 0)
            {
                return AudioSpectrumSampleSnapshot.Empty;
            }

            return new AudioSpectrumSampleSnapshot(
                _sampleRate,
                _version,
                CopyOrdered(_preEq, _preWriteIndex, _preCount),
                CopyOrdered(_postEq, _postWriteIndex, _postCount));
        }
    }

    private static void AppendStereoAsMono(
        float[] stereo,
        float[] destination,
        ref int writeIndex,
        ref int count)
    {
        var frames = stereo.Length / 2;
        for (var frame = 0; frame < frames; frame++)
        {
            var sampleIndex = frame * 2;
            var mono = (stereo[sampleIndex] + stereo[sampleIndex + 1]) * 0.5f;
            destination[writeIndex] = mono;
            writeIndex = (writeIndex + 1) % destination.Length;
            count = Math.Min(destination.Length, count + 1);
        }
    }

    private static void AppendSilence(
        int frameCount,
        float[] destination,
        ref int writeIndex,
        ref int count)
    {
        var frames = Math.Min(frameCount, destination.Length);
        for (var frame = 0; frame < frames; frame++)
        {
            destination[writeIndex] = 0;
            writeIndex = (writeIndex + 1) % destination.Length;
            count = Math.Min(destination.Length, count + 1);
        }
    }

    private static float[] CopyOrdered(float[] source, int writeIndex, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var result = new float[count];
        var start = count == source.Length ? writeIndex : 0;
        for (var index = 0; index < count; index++)
        {
            result[index] = source[(start + index) % source.Length];
        }

        return result;
    }
}
