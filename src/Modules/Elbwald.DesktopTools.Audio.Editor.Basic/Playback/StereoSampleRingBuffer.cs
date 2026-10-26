namespace Elbwald.DesktopTools.Audio.Editor.Basic.Playback;

/// <summary>
/// Single-producer/single-consumer ring buffer for interleaved stereo float samples.
/// The producer runs outside the realtime callback; the callback only copies ready data.
/// </summary>
internal sealed class StereoSampleRingBuffer
{
    private const int Channels = 2;
    private readonly float[] _samples;
    private readonly int _capacityFrames;
    private long _readFrame;
    private long _writeFrame;

    public StereoSampleRingBuffer(int capacityFrames)
    {
        if (capacityFrames < 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(capacityFrames));
        }

        _capacityFrames = capacityFrames;
        _samples = new float[checked(capacityFrames * Channels)];
    }

    public int AvailableFrames
    {
        get
        {
            var write = Volatile.Read(ref _writeFrame);
            var read = Volatile.Read(ref _readFrame);
            return checked((int)Math.Clamp(write - read, 0, _capacityFrames));
        }
    }

    public int FreeFrames => _capacityFrames - AvailableFrames;

    public void Reset()
    {
        Volatile.Write(ref _readFrame, 0);
        Volatile.Write(ref _writeFrame, 0);
    }

    public int Write(ReadOnlySpan<float> interleavedStereo)
    {
        if ((interleavedStereo.Length & 1) != 0)
        {
            throw new ArgumentException("Stereo samples must be interleaved in pairs.", nameof(interleavedStereo));
        }

        var requestedFrames = interleavedStereo.Length / Channels;
        var write = Volatile.Read(ref _writeFrame);
        var read = Volatile.Read(ref _readFrame);
        var freeFrames = checked((int)Math.Max(0, _capacityFrames - (write - read)));
        var framesToWrite = Math.Min(requestedFrames, freeFrames);
        if (framesToWrite <= 0)
        {
            return 0;
        }

        var writeIndexFrames = checked((int)(write % _capacityFrames));
        var firstFrames = Math.Min(framesToWrite, _capacityFrames - writeIndexFrames);
        var firstSamples = firstFrames * Channels;
        interleavedStereo[..firstSamples].CopyTo(
            _samples.AsSpan(writeIndexFrames * Channels, firstSamples));

        var remainingFrames = framesToWrite - firstFrames;
        if (remainingFrames > 0)
        {
            var remainingSamples = remainingFrames * Channels;
            interleavedStereo.Slice(firstSamples, remainingSamples)
                .CopyTo(_samples.AsSpan(0, remainingSamples));
        }

        Volatile.Write(ref _writeFrame, write + framesToWrite);
        return framesToWrite;
    }

    public int Read(Span<float> destinationStereo, int requestedFrames)
    {
        if (requestedFrames < 0 || destinationStereo.Length < requestedFrames * Channels)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        }

        var read = Volatile.Read(ref _readFrame);
        var write = Volatile.Read(ref _writeFrame);
        var availableFrames = checked((int)Math.Max(0, write - read));
        var framesToRead = Math.Min(requestedFrames, availableFrames);

        if (framesToRead > 0)
        {
            var readIndexFrames = checked((int)(read % _capacityFrames));
            var firstFrames = Math.Min(framesToRead, _capacityFrames - readIndexFrames);
            var firstSamples = firstFrames * Channels;
            _samples.AsSpan(readIndexFrames * Channels, firstSamples)
                .CopyTo(destinationStereo[..firstSamples]);

            var remainingFrames = framesToRead - firstFrames;
            if (remainingFrames > 0)
            {
                var remainingSamples = remainingFrames * Channels;
                _samples.AsSpan(0, remainingSamples)
                    .CopyTo(destinationStereo.Slice(firstSamples, remainingSamples));
            }

            Volatile.Write(ref _readFrame, read + framesToRead);
        }

        destinationStereo.Slice(framesToRead * Channels, (requestedFrames - framesToRead) * Channels).Clear();
        return framesToRead;
    }
}
