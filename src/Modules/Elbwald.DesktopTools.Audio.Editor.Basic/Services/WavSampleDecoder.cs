using System.Buffers.Binary;
using Elbwald.DesktopTools.Audio.Editor.Basic.Models;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

/// <summary>
/// Decodes supported WAV sample formats to normalized 32-bit float samples.
/// This is intentionally independent of the output backend so the same decoder
/// can feed waveform generation, analysis and realtime playback later.
/// </summary>
public sealed class WavSampleDecoder
{
    private readonly WavMetadataReader _metadataReader;

    public WavSampleDecoder(WavMetadataReader metadataReader)
    {
        ArgumentNullException.ThrowIfNull(metadataReader);
        _metadataReader = metadataReader;
    }

    public async Task<DecodedAudioBlock> ReadFramesAsync(
        string filePath,
        long startFrame,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        if (startFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        var info = await _metadataReader.ReadAsync(filePath, cancellationToken);
        return await Task.Run(
            () => ReadFrames(info, startFrame, frameCount, cancellationToken),
            cancellationToken);
    }

    public async Task<WaveformEnvelope> BuildWaveformAsync(
        string filePath,
        int targetPeakCount = 8192,
        CancellationToken cancellationToken = default)
    {
        if (targetPeakCount is < 64 or > 16384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetPeakCount),
                "Die Wellenformauflösung muss zwischen 64 und 16384 Peaks liegen.");
        }

        var info = await _metadataReader.ReadAsync(filePath, cancellationToken);
        return await Task.Run(
            () => BuildWaveform(info, targetPeakCount, cancellationToken),
            cancellationToken);
    }

    private DecodedAudioBlock ReadFrames(
        WavAudioInfo info,
        long startFrame,
        int requestedFrameCount,
        CancellationToken cancellationToken)
    {
        using var stream = OpenSource(info.FilePath);
        return ReadFrames(info, stream, startFrame, requestedFrameCount, cancellationToken);
    }

    internal DecodedAudioBlock ReadFrames(
        WavAudioInfo info,
        FileStream stream,
        long startFrame,
        int requestedFrameCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(stream);

        if (startFrame < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        if (requestedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrameCount));
        }

        if (startFrame >= info.SampleFrames || requestedFrameCount == 0)
        {
            return new DecodedAudioBlock(
                info.SampleRate,
                info.Channels,
                startFrame,
                0,
                []);
        }

        var availableFrames = info.SampleFrames - startFrame;
        var frameCount = checked((int)Math.Min(requestedFrameCount, availableFrames));
        var samples = new float[checked(frameCount * info.Channels)];

        stream.Position = checked(info.DataOffset + startFrame * info.BlockAlign);
        var rawBytes = new byte[checked(frameCount * info.BlockAlign)];
        ReadExactly(stream, rawBytes);

        var sampleIndex = 0;
        for (var frame = 0; frame < frameCount; frame++)
        {
            if ((frame & 0x0FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var frameBytes = rawBytes.AsSpan(frame * info.BlockAlign, info.BlockAlign);
            for (var channel = 0; channel < info.Channels; channel++)
            {
                samples[sampleIndex++] = DecodeChannelSample(frameBytes, channel, info);
            }
        }

        return new DecodedAudioBlock(
            info.SampleRate,
            info.Channels,
            startFrame,
            frameCount,
            samples);
    }

    private static WaveformEnvelope BuildWaveform(
        WavAudioInfo info,
        int targetPeakCount,
        CancellationToken cancellationToken)
    {
        if (info.SampleFrames <= 0)
        {
            return new WaveformEnvelope(
                info.SampleRate,
                info.Channels,
                0,
                [WaveformPeak.Empty],
                info.Channels == 2 ? [WaveformPeak.Empty] : null);
        }

        var peakCount = checked((int)Math.Min(targetPeakCount, info.SampleFrames));
        peakCount = Math.Max(1, peakCount);
        var left = new WaveformPeak[peakCount];
        var right = info.Channels == 2 ? new WaveformPeak[peakCount] : null;

        using var stream = OpenSource(info.FilePath);
        stream.Position = info.DataOffset;

        var frameBuffer = new byte[info.BlockAlign];
        long processedFrames = 0;

        for (var peakIndex = 0; peakIndex < peakCount; peakIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bucketEndFrame = (long)(((peakIndex + 1) * (decimal)info.SampleFrames) / peakCount);
            var bucketFrames = bucketEndFrame - processedFrames;

            var leftMin = 1f;
            var leftMax = -1f;
            var rightMin = 1f;
            var rightMax = -1f;

            for (long frame = 0; frame < bucketFrames; frame++)
            {
                if ((frame & 0x0FFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ReadExactly(stream, frameBuffer);

                var leftSample = DecodeChannelSample(frameBuffer, 0, info);
                leftMin = Math.Min(leftMin, leftSample);
                leftMax = Math.Max(leftMax, leftSample);

                if (right is not null)
                {
                    var rightSample = DecodeChannelSample(frameBuffer, 1, info);
                    rightMin = Math.Min(rightMin, rightSample);
                    rightMax = Math.Max(rightMax, rightSample);
                }
            }

            left[peakIndex] = new WaveformPeak(leftMin, leftMax);
            if (right is not null)
            {
                right[peakIndex] = new WaveformPeak(rightMin, rightMax);
            }

            processedFrames += bucketFrames;
        }

        return new WaveformEnvelope(
            info.SampleRate,
            info.Channels,
            info.SampleFrames,
            left,
            right);
    }

    private static FileStream OpenSource(string filePath) =>
        new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.SequentialScan);

    private static float DecodeChannelSample(
        ReadOnlySpan<byte> frame,
        int channel,
        WavAudioInfo info)
    {
        var bytesPerSample = (info.BitsPerSample + 7) / 8;
        var offset = checked(channel * bytesPerSample);
        var sample = frame.Slice(offset, bytesPerSample);

        return info.EffectiveFormatTag switch
        {
            1 => DecodePcm(sample, info.BitsPerSample),
            3 => DecodeFloat(sample, info.BitsPerSample),
            _ => throw new NotSupportedException(
                $"WAV-Format 0x{info.EffectiveFormatTag:X4} kann nicht decodiert werden.")
        };
    }

    private static float DecodePcm(ReadOnlySpan<byte> sample, int bitsPerSample) =>
        bitsPerSample switch
        {
            8 => (sample[0] - 128) / 128f,
            16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
            24 => DecodePcm24(sample),
            32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
            _ => throw new NotSupportedException($"PCM mit {bitsPerSample} Bit wird nicht unterstützt.")
        };

    private static float DecodePcm24(ReadOnlySpan<byte> sample)
    {
        var value = sample[0] | (sample[1] << 8) | (sample[2] << 16);
        if ((value & 0x0080_0000) != 0)
        {
            value |= unchecked((int)0xFF00_0000);
        }

        return value / 8388608f;
    }

    private static float DecodeFloat(ReadOnlySpan<byte> sample, int bitsPerSample)
    {
        var value = bitsPerSample switch
        {
            32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample)),
            64 => (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(sample)),
            _ => throw new NotSupportedException($"IEEE Float mit {bitsPerSample} Bit wird nicht unterstützt.")
        };

        if (!float.IsFinite(value))
        {
            return 0;
        }

        return Math.Clamp(value, -1f, 1f);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer[offset..]);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unerwartetes Dateiende innerhalb der WAV-Samples.");
            }

            offset += read;
        }
    }
}

public sealed record DecodedAudioBlock(
    int SampleRate,
    int Channels,
    long StartFrame,
    int FrameCount,
    float[] InterleavedSamples);
