using System.Text;

namespace Elbwald.DesktopTools.Audio.Editor.Basic.Services;

public sealed class WavMetadataReader
{
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    public Task<WavAudioInfo> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => Read(filePath, cancellationToken),
            cancellationToken);
    }

    internal WavAudioInfo Read(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (stream.Length < 12)
        {
            throw new InvalidDataException("Die Datei ist zu klein für eine gültige WAV-Datei.");
        }

        var riff = ReadFourCc(reader);
        _ = reader.ReadUInt32();
        var wave = ReadFourCc(reader);

        if (!string.Equals(riff, "RIFF", StringComparison.Ordinal) ||
            !string.Equals(wave, "WAVE", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Aktuell werden klassische RIFF/WAVE-Dateien unterstützt. RF64 folgt mit der erweiterten Audio-I/O-Schicht.");
        }

        ushort? formatTag = null;
        ushort? effectiveFormatTag = null;
        ushort? channels = null;
        uint? sampleRate = null;
        uint? byteRate = null;
        ushort? blockAlign = null;
        ushort? bitsPerSample = null;
        ushort? validBitsPerSample = null;
        long? dataOffset = null;
        long? dataBytes = null;

        while (stream.Position + 8 <= stream.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkDataStart = stream.Position;
            var chunkDataEnd = checked(chunkDataStart + chunkSize);

            if (chunkDataEnd > stream.Length)
            {
                throw new InvalidDataException($"Der WAV-Chunk '{chunkId}' ist beschädigt oder unvollständig.");
            }

            if (string.Equals(chunkId, "fmt ", StringComparison.Ordinal))
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException("Der fmt-Chunk der WAV-Datei ist ungültig.");
                }

                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
                validBitsPerSample = bitsPerSample;
                effectiveFormatTag = formatTag;

                if (formatTag == 0xFFFE)
                {
                    if (chunkSize < 40)
                    {
                        throw new InvalidDataException("WAVE_FORMAT_EXTENSIBLE enthält keinen vollständigen Formatblock.");
                    }

                    var extensionSize = reader.ReadUInt16();
                    if (extensionSize < 22)
                    {
                        throw new InvalidDataException("WAVE_FORMAT_EXTENSIBLE enthält eine ungültige Erweiterung.");
                    }

                    validBitsPerSample = reader.ReadUInt16();
                    _ = reader.ReadUInt32(); // channel mask; routing follows the declared mono/stereo layout for now.
                    var subFormat = new Guid(reader.ReadBytes(16));

                    effectiveFormatTag = subFormat switch
                    {
                        var value when value == PcmSubFormat => 1,
                        var value when value == IeeeFloatSubFormat => 3,
                        _ => throw new NotSupportedException(
                            $"Der WAVE_FORMAT_EXTENSIBLE-Subtyp '{subFormat}' wird aktuell nicht unterstützt.")
                    };
                }
            }
            else if (string.Equals(chunkId, "data", StringComparison.Ordinal) && dataBytes is null)
            {
                dataOffset = chunkDataStart;
                dataBytes = chunkSize;
            }

            var paddedSize = chunkSize + (chunkSize & 1U);
            stream.Position = checked(chunkDataStart + paddedSize);
        }

        if (formatTag is null ||
            effectiveFormatTag is null ||
            channels is null ||
            sampleRate is null ||
            byteRate is null ||
            blockAlign is null ||
            bitsPerSample is null ||
            validBitsPerSample is null ||
            dataOffset is null ||
            dataBytes is null)
        {
            throw new InvalidDataException("Die WAV-Datei enthält nicht alle erforderlichen fmt-/data-Informationen.");
        }

        if (channels.Value is not 1 and not 2)
        {
            throw new NotSupportedException(
                $"Diese WAV-Datei besitzt {channels} Kanäle. Das Audio-Studio unterstützt aktuell Mono und Stereo.");
        }

        if (sampleRate.Value == 0 || byteRate.Value == 0 || blockAlign.Value == 0)
        {
            throw new InvalidDataException("Die WAV-Datei enthält ungültige Sample-/Blockraten.");
        }

        ValidateSampleFormat(effectiveFormatTag.Value, bitsPerSample.Value);

        var bytesPerSample = checked((bitsPerSample.Value + 7) / 8);
        var minimumBlockAlign = checked(bytesPerSample * channels.Value);
        if (blockAlign.Value < minimumBlockAlign)
        {
            throw new InvalidDataException("Die WAV-Datei besitzt ein ungültiges Block-Alignment.");
        }

        var sampleFrames = dataBytes.Value / blockAlign.Value;
        var durationSeconds = sampleFrames / (double)sampleRate.Value;

        return new WavAudioInfo(
            FilePath: fullPath,
            Channels: channels.Value,
            SampleRate: checked((int)sampleRate.Value),
            BitsPerSample: bitsPerSample.Value,
            ValidBitsPerSample: validBitsPerSample.Value,
            DurationSeconds: durationSeconds,
            SampleFrames: sampleFrames,
            Encoding: GetEncodingName(formatTag.Value, effectiveFormatTag.Value),
            FormatTag: formatTag.Value,
            EffectiveFormatTag: effectiveFormatTag.Value,
            BlockAlign: blockAlign.Value,
            DataOffset: dataOffset.Value,
            DataLength: dataBytes.Value);
    }

    private static void ValidateSampleFormat(ushort effectiveFormatTag, ushort bitsPerSample)
    {
        if (effectiveFormatTag == 1 && bitsPerSample is 8 or 16 or 24 or 32)
        {
            return;
        }

        if (effectiveFormatTag == 3 && bitsPerSample is 32 or 64)
        {
            return;
        }

        if (effectiveFormatTag == 1)
        {
            throw new NotSupportedException(
                $"PCM mit {bitsPerSample} Bit wird aktuell nicht decodiert. Unterstützt sind 8, 16, 24 und 32 Bit.");
        }

        if (effectiveFormatTag == 3)
        {
            throw new NotSupportedException(
                $"IEEE Float mit {bitsPerSample} Bit wird aktuell nicht decodiert. Unterstützt sind 32 und 64 Bit.");
        }

        throw new NotSupportedException(
            $"Das WAV-Encoding 0x{effectiveFormatTag:X4} wird aktuell nicht unterstützt.");
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (bytes.Length != 4)
        {
            throw new EndOfStreamException("Unerwartetes Dateiende in der WAV-Struktur.");
        }

        return Encoding.ASCII.GetString(bytes);
    }

    private static string GetEncodingName(ushort formatTag, ushort effectiveFormatTag)
    {
        var baseName = effectiveFormatTag switch
        {
            1 => "PCM",
            3 => "IEEE Float",
            _ => $"0x{effectiveFormatTag:X4}"
        };

        return formatTag == 0xFFFE
            ? $"WAVE_FORMAT_EXTENSIBLE ({baseName})"
            : baseName;
    }
}

public sealed record WavAudioInfo(
    string FilePath,
    int Channels,
    int SampleRate,
    int BitsPerSample,
    int ValidBitsPerSample,
    double DurationSeconds,
    long SampleFrames,
    string Encoding,
    ushort FormatTag,
    ushort EffectiveFormatTag,
    int BlockAlign,
    long DataOffset,
    long DataLength);
