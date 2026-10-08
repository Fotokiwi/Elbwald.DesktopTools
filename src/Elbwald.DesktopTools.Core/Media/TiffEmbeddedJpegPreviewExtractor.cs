using Elbwald.DesktopTools.Contracts.Media;

namespace Elbwald.DesktopTools.Core.Media;

public sealed class TiffEmbeddedJpegPreviewExtractor
    : IRawPreviewExtractor
{
    private const ushort TiffMagic = 42;

    private const ushort TagSubIfds = 0x014A;
    private const ushort TagJpegOffset = 0x0201;
    private const ushort TagJpegLength = 0x0202;
    private const ushort TagExifIfd = 0x8769;
    private const ushort TagGpsIfd = 0x8825;
    private const ushort TagInteropIfd = 0xA005;

    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeIfd = 13;

    private const int MaxIfdCount = 64;
    private const int MaxEntriesPerIfd = 4096;
    private const int MaxPointerValues = 64;

    private const long MaxEmbeddedJpegBytes =
        128L * 1024 * 1024;

    private const long MaxFallbackScanBytes =
        256L * 1024 * 1024;

    private static readonly HashSet<string> RawExtensions =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            ".arw",
            ".dng",
            ".cr2",
            ".cr3",
            ".nef",
            ".raf",
            ".orf",
            ".rw2",
            ".pef"
        };

    public Task<RawPreviewResult> ExtractAsync(
        MediaFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!RawExtensions.Contains(
                file.Extension))
        {
            return Task.FromResult(
                new RawPreviewResult(
                    RawPreviewState.NotRaw));
        }

        return Task.Run(
            () => ExtractCore(
                file,
                cancellationToken),
            cancellationToken);
    }

    private static RawPreviewResult ExtractCore(
        MediaFile file,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var stream =
                new FileStream(
                    file.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    128 * 1024,
                    FileOptions.SequentialScan);

            if (TryFindTiffEmbeddedJpeg(
                    stream,
                    cancellationToken,
                    out var offset,
                    out var length)
                || TryFindJpegByMarkerScan(
                    stream,
                    cancellationToken,
                    out offset,
                    out length))
            {
                var bytes =
                    ReadJpeg(
                        stream,
                        offset,
                        length,
                        cancellationToken);

                if (bytes is not null)
                {
                    return new RawPreviewResult(
                        RawPreviewState.Success,
                        bytes,
                        "image/jpeg");
                }
            }

            return new RawPreviewResult(
                RawPreviewState.PreviewNotFound,
                errorMessage:
                    "Keine eingebettete JPEG-Vorschau gefunden.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (FileNotFoundException exception)
        {
            return Failure(
                RawPreviewState.FileNotFound,
                exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Failure(
                RawPreviewState.FileNotFound,
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                RawPreviewState.AccessDenied,
                exception);
        }
        catch (IOException exception)
        {
            return Failure(
                RawPreviewState.IoError,
                exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or OverflowException)
        {
            return Failure(
                RawPreviewState.InvalidFormat,
                exception);
        }
    }

    private static bool TryFindTiffEmbeddedJpeg(
        FileStream stream,
        CancellationToken cancellationToken,
        out long bestOffset,
        out long bestLength)
    {
        bestOffset = 0;
        bestLength = 0;

        if (stream.Length < 8)
        {
            return false;
        }

        stream.Position = 0;

        var byteOrder =
            new byte[2];

        if (!ReadExact(
                stream,
                byteOrder,
                cancellationToken))
        {
            return false;
        }

        var littleEndian =
            byteOrder[0] == (byte)'I'
            && byteOrder[1] == (byte)'I';

        var bigEndian =
            byteOrder[0] == (byte)'M'
            && byteOrder[1] == (byte)'M';

        if (!littleEndian
            && !bigEndian)
        {
            return false;
        }

        if (!TryReadUInt16(
                stream,
                littleEndian,
                cancellationToken,
                out var magic)
            || magic != TiffMagic)
        {
            return false;
        }

        if (!TryReadUInt32(
                stream,
                littleEndian,
                cancellationToken,
                out var firstIfd)
            || firstIfd == 0)
        {
            return false;
        }

        var pending =
            new Queue<uint>();

        var visited =
            new HashSet<uint>();

        pending.Enqueue(
            firstIfd);

        var processed = 0;

        while (pending.Count > 0
               && processed < MaxIfdCount)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ifdOffset =
                pending.Dequeue();

            if (ifdOffset == 0
                || !visited.Add(ifdOffset))
            {
                continue;
            }

            processed++;

            if (!TryReadIfd(
                    stream,
                    littleEndian,
                    ifdOffset,
                    pending,
                    cancellationToken,
                    out var jpegOffset,
                    out var jpegLength))
            {
                continue;
            }

            if (jpegOffset is null
                || jpegLength is null)
            {
                continue;
            }

            var candidateOffset =
                (long)jpegOffset.Value;

            var candidateLength =
                (long)jpegLength.Value;

            if (!IsValidJpegRange(
                    stream,
                    candidateOffset,
                    candidateLength))
            {
                continue;
            }

            if (candidateLength > bestLength)
            {
                bestOffset = candidateOffset;
                bestLength = candidateLength;
            }
        }

        return bestLength > 0;
    }

    private static bool TryReadIfd(
        FileStream stream,
        bool littleEndian,
        uint ifdOffset,
        Queue<uint> pending,
        CancellationToken cancellationToken,
        out uint? jpegOffset,
        out uint? jpegLength)
    {
        jpegOffset = null;
        jpegLength = null;

        var absoluteOffset =
            (long)ifdOffset;

        if (absoluteOffset < 0
            || absoluteOffset + 2 > stream.Length)
        {
            return false;
        }

        stream.Position =
            absoluteOffset;

        if (!TryReadUInt16(
                stream,
                littleEndian,
                cancellationToken,
                out var entryCount)
            || entryCount > MaxEntriesPerIfd)
        {
            return false;
        }

        var entriesStart =
            stream.Position;

        var entriesEnd =
            entriesStart
            + entryCount * 12L;

        if (entriesEnd + 4 > stream.Length)
        {
            return false;
        }

        for (var index = 0;
             index < entryCount;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            stream.Position =
                entriesStart
                + index * 12L;

            if (!TryReadUInt16(
                    stream,
                    littleEndian,
                    cancellationToken,
                    out var tag)
                || !TryReadUInt16(
                    stream,
                    littleEndian,
                    cancellationToken,
                    out var type)
                || !TryReadUInt32(
                    stream,
                    littleEndian,
                    cancellationToken,
                    out var count))
            {
                return false;
            }

            var valueField =
                new byte[4];

            if (!ReadExact(
                    stream,
                    valueField,
                    cancellationToken))
            {
                return false;
            }

            if (tag == TagJpegOffset)
            {
                var values =
                    ReadUnsignedValues(
                        stream,
                        littleEndian,
                        type,
                        count,
                        valueField,
                        cancellationToken);

                jpegOffset =
                    values.FirstOrDefault();

                if (jpegOffset == 0)
                {
                    jpegOffset = null;
                }

                continue;
            }

            if (tag == TagJpegLength)
            {
                var values =
                    ReadUnsignedValues(
                        stream,
                        littleEndian,
                        type,
                        count,
                        valueField,
                        cancellationToken);

                jpegLength =
                    values.FirstOrDefault();

                if (jpegLength == 0)
                {
                    jpegLength = null;
                }

                continue;
            }

            if (tag is TagSubIfds
                or TagExifIfd
                or TagGpsIfd
                or TagInteropIfd)
            {
                foreach (var pointer in ReadUnsignedValues(
                             stream,
                             littleEndian,
                             type,
                             count,
                             valueField,
                             cancellationToken))
                {
                    if (pointer != 0)
                    {
                        pending.Enqueue(pointer);
                    }
                }
            }
        }

        stream.Position =
            entriesEnd;

        if (TryReadUInt32(
                stream,
                littleEndian,
                cancellationToken,
                out var nextIfd)
            && nextIfd != 0)
        {
            pending.Enqueue(
                nextIfd);
        }

        return true;
    }

    private static IReadOnlyList<uint> ReadUnsignedValues(
        FileStream stream,
        bool littleEndian,
        ushort type,
        uint count,
        byte[] valueField,
        CancellationToken cancellationToken)
    {
        if (count == 0
            || count > MaxPointerValues)
        {
            return Array.Empty<uint>();
        }

        var elementSize =
            type switch
            {
                TypeShort => 2,
                TypeLong => 4,
                TypeIfd => 4,
                _ => 0
            };

        if (elementSize == 0)
        {
            return Array.Empty<uint>();
        }

        var totalBytes =
            checked(
                (long)elementSize
                * count);

        if (totalBytes <= 4)
        {
            var inlineValues =
                new List<uint>(
                    checked((int)count));

            for (var index = 0;
                 index < count;
                 index++)
            {
                var byteOffset =
                    checked(
                        (int)(index * elementSize));

                inlineValues.Add(
                    type == TypeShort
                        ? DecodeUInt16(
                            valueField.AsSpan(
                                byteOffset,
                                2),
                            littleEndian)
                        : DecodeUInt32(
                            valueField.AsSpan(
                                byteOffset,
                                4),
                            littleEndian));
            }

            return inlineValues;
        }

        var dataOffset =
            DecodeUInt32(
                valueField,
                littleEndian);

        if (dataOffset == 0
            || (long)dataOffset + totalBytes > stream.Length)
        {
            return Array.Empty<uint>();
        }

        var restorePosition =
            stream.Position;

        try
        {
            stream.Position =
                dataOffset;

            var values =
                new List<uint>(
                    checked((int)count));

            for (var index = 0;
                 index < count;
                 index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (type == TypeShort)
                {
                    if (!TryReadUInt16(
                            stream,
                            littleEndian,
                            cancellationToken,
                            out var shortValue))
                    {
                        break;
                    }

                    values.Add(
                        shortValue);
                }
                else
                {
                    if (!TryReadUInt32(
                            stream,
                            littleEndian,
                            cancellationToken,
                            out var longValue))
                    {
                        break;
                    }

                    values.Add(
                        longValue);
                }
            }

            return values;
        }
        finally
        {
            stream.Position =
                restorePosition;
        }
    }

    private static bool TryFindJpegByMarkerScan(
        FileStream stream,
        CancellationToken cancellationToken,
        out long bestOffset,
        out long bestLength)
    {
        bestOffset = 0;
        bestLength = 0;

        stream.Position = 0;

        var scanLimit =
            Math.Min(
                stream.Length,
                MaxFallbackScanBytes);

        var buffer =
            new byte[1024 * 1024];

        long absolutePosition = 0;
        long currentStart = -1;

        byte previous = 0;
        var hasPrevious = false;

        while (absolutePosition < scanLimit)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining =
                scanLimit - absolutePosition;

            var requested =
                (int)Math.Min(
                    buffer.Length,
                    remaining);

            var read =
                stream.Read(
                    buffer,
                    0,
                    requested);

            if (read <= 0)
            {
                break;
            }

            for (var index = 0;
                 index < read;
                 index++)
            {
                var current =
                    buffer[index];

                var currentPosition =
                    absolutePosition + index;

                if (hasPrevious
                    && currentStart < 0
                    && previous == 0xFF
                    && current == 0xD8)
                {
                    currentStart =
                        currentPosition - 1;
                }
                else if (hasPrevious
                         && currentStart >= 0
                         && previous == 0xFF
                         && current == 0xD9)
                {
                    var candidateLength =
                        currentPosition + 1 - currentStart;

                    if (candidateLength > bestLength
                        && candidateLength <= MaxEmbeddedJpegBytes)
                    {
                        bestOffset =
                            currentStart;

                        bestLength =
                            candidateLength;
                    }

                    currentStart = -1;
                }

                previous = current;
                hasPrevious = true;
            }

            absolutePosition += read;
        }

        return bestLength > 0;
    }

    private static byte[]? ReadJpeg(
        FileStream stream,
        long offset,
        long length,
        CancellationToken cancellationToken)
    {
        if (!IsValidJpegRange(
                stream,
                offset,
                length)
            || length > int.MaxValue)
        {
            return null;
        }

        stream.Position =
            offset;

        var bytes =
            new byte[
                checked((int)length)];

        if (!ReadExact(
                stream,
                bytes,
                cancellationToken))
        {
            return null;
        }

        if (bytes.Length < 4
            || bytes[0] != 0xFF
            || bytes[1] != 0xD8)
        {
            return null;
        }

        return bytes;
    }

    private static bool IsValidJpegRange(
        FileStream stream,
        long offset,
        long length)
    {
        return offset >= 0
               && length >= 4
               && length <= MaxEmbeddedJpegBytes
               && offset <= stream.Length
               && length <= stream.Length - offset;
    }

    private static bool TryReadUInt16(
        Stream stream,
        bool littleEndian,
        CancellationToken cancellationToken,
        out ushort value)
    {
        var buffer =
            new byte[2];

        if (!ReadExact(
                stream,
                buffer,
                cancellationToken))
        {
            value = 0;
            return false;
        }

        value =
            DecodeUInt16(
                buffer,
                littleEndian);

        return true;
    }

    private static bool TryReadUInt32(
        Stream stream,
        bool littleEndian,
        CancellationToken cancellationToken,
        out uint value)
    {
        var buffer =
            new byte[4];

        if (!ReadExact(
                stream,
                buffer,
                cancellationToken))
        {
            value = 0;
            return false;
        }

        value =
            DecodeUInt32(
                buffer,
                littleEndian);

        return true;
    }

    private static ushort DecodeUInt16(
        ReadOnlySpan<byte> bytes,
        bool littleEndian)
    {
        return littleEndian
            ? (ushort)(
                bytes[0]
                | bytes[1] << 8)
            : (ushort)(
                bytes[0] << 8
                | bytes[1]);
    }

    private static uint DecodeUInt32(
        ReadOnlySpan<byte> bytes,
        bool littleEndian)
    {
        return littleEndian
            ? (uint)(
                bytes[0]
                | bytes[1] << 8
                | bytes[2] << 16
                | bytes[3] << 24)
            : (uint)(
                bytes[0] << 24
                | bytes[1] << 16
                | bytes[2] << 8
                | bytes[3]);
    }

    private static bool ReadExact(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;

        while (totalRead < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var read =
                stream.Read(
                    buffer,
                    totalRead,
                    buffer.Length - totalRead);

            if (read <= 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static RawPreviewResult Failure(
        RawPreviewState state,
        Exception exception)
    {
        return new RawPreviewResult(
            state,
            errorMessage:
                exception.Message);
    }
}
