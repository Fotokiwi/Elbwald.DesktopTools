using System.Buffers.Binary;
using System.Security.Cryptography;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;
using Elbwald.DesktopTools.Contracts.Paths;

namespace Elbwald.DesktopTools.Core.Media.MetadataExporting;

public sealed class MetadataExportService : IMetadataExportService
{
    private static readonly byte[] XmpJpegPreamble = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    private readonly IAppPaths _appPaths;

    public MetadataExportService(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _appPaths = appPaths;
    }

    public async Task<MetadataExportInspection> InspectAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        var sidecarPath = Path.GetFullPath(mediaPath) + ".xmp";
        var sidecarExists = File.Exists(sidecarPath);
        var sidecarManaged = false;
        if (sidecarExists)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(sidecarPath, cancellationToken);
                sidecarManaged = XmpExportDocument.IsElbwaldManaged(bytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                sidecarManaged = false;
            }
        }

        byte[]? embedded = null;
        try
        {
            embedded = await ReadEmbeddedPacketAsync(mediaPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            embedded = null;
        }
        return new MetadataExportInspection(
            sidecarExists,
            sidecarManaged,
            embedded is not null,
            embedded is not null && XmpExportDocument.IsElbwaldManaged(embedded));
    }

    public MetadataExportCapability GetEmbeddedCapability(string mediaPath, MetadataExportValues values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(values);
        var extension = Path.GetExtension(mediaPath);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return new MetadataExportCapability(false, "Direktes Einbetten ist in 0060 bewusst nur für JPEG aktiviert.");
        }

        if (values.GpsMode == MetadataGpsMode.Remove)
        {
            return new MetadataExportCapability(false, "GPS-Entfernen wird nicht nur als XMP-Hinweis eingebettet. Dafür ist später ein verifiziertes EXIF-Rewrite nötig.");
        }

        return values.HasAnyExportData
            ? new MetadataExportCapability(true, "Verlustfreies Einbetten als standardkonformes XMP im JPEG-Header.")
            : new MetadataExportCapability(false, "Für dieses Medium sind keine exportierbaren Elbwald-Metadaten gespeichert.");
    }

    public async Task<MetadataSidecarExportResult> WriteSidecarAsync(
        string mediaPath,
        MetadataExportValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(values);
        var sidecarPath = Path.GetFullPath(mediaPath) + ".xmp";
        if (!File.Exists(mediaPath))
            return new MetadataSidecarExportResult(false, sidecarPath, ErrorMessage: "Mediendatei existiert nicht mehr.");
        if (!values.HasAnyExportData)
            return new MetadataSidecarExportResult(false, sidecarPath, ErrorMessage: "Keine Elbwald-Metadaten zum Exportieren vorhanden.");

        if (File.Exists(sidecarPath))
        {
            try
            {
                var existing = await File.ReadAllBytesAsync(sidecarPath, cancellationToken);
                if (!XmpExportDocument.IsElbwaldManaged(existing))
                    return new MetadataSidecarExportResult(false, sidecarPath, ErrorMessage: "Vorhandene fremde oder unklare XMP-Sidecar wird nicht überschrieben.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new MetadataSidecarExportResult(false, sidecarPath, ErrorMessage: exception.GetBaseException().Message);
            }
        }

        var packet = XmpExportDocument.CreatePacket(values);
        var tempPath = sidecarPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        string? backupPath = null;
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(packet, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(sidecarPath))
            {
                backupPath = sidecarPath + ".bak-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                File.Replace(tempPath, sidecarPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, sidecarPath, overwrite: false);
            }

            var verify = await File.ReadAllBytesAsync(sidecarPath, cancellationToken);
            if (!XmpExportDocument.IsElbwaldManaged(verify))
                throw new IOException("Geschriebene XMP-Sidecar konnte nicht als Elbwald-XMP verifiziert werden.");
            return new MetadataSidecarExportResult(true, sidecarPath, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            TryDelete(tempPath);
            return new MetadataSidecarExportResult(false, sidecarPath, backupPath, exception.GetBaseException().Message);
        }
    }

    public async Task<MetadataEmbeddedCopyResult> WriteEmbeddedCopyAsync(
        string sourceJpegPath,
        string outputJpegPath,
        MetadataExportValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceJpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputJpegPath);
        ArgumentNullException.ThrowIfNull(values);

        if (!values.HasAnyExportData)
        {
            return new MetadataEmbeddedCopyResult(
                false,
                outputJpegPath,
                ErrorMessage: "Keine exportierbaren Elbwald-Metadaten vorhanden.");
        }

        var source = Path.GetFullPath(sourceJpegPath);
        var output = Path.GetFullPath(outputJpegPath);
        if (!File.Exists(source))
        {
            return new MetadataEmbeddedCopyResult(
                false,
                output,
                ErrorMessage: "JPEG-Quelle existiert nicht mehr.");
        }

        var extension = Path.GetExtension(source);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return new MetadataEmbeddedCopyResult(
                false,
                output,
                ErrorMessage: "XMP kann in 0062 nur in JPEG-Ausgabedateien eingebettet werden.");
        }

        if (File.Exists(output))
        {
            return new MetadataEmbeddedCopyResult(
                false,
                output,
                ErrorMessage: "Exportziel existiert bereits.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var packet = XmpExportDocument.CreatePacket(values);
            await RewriteJpegAsync(source, output, packet, cancellationToken);

            var embedded = await ReadEmbeddedPacketAsync(output, cancellationToken);
            if (embedded is null || !embedded.AsSpan().SequenceEqual(packet))
            {
                throw new IOException("Eingebettetes XMP konnte in der Exportkopie nicht verifiziert werden.");
            }

            var info = new FileInfo(output);
            var hash = await HashFileAsync(output, cancellationToken);
            if (string.IsNullOrWhiteSpace(hash))
            {
                throw new IOException("SHA-256 der XMP-Exportkopie konnte nicht berechnet werden.");
            }
            return new MetadataEmbeddedCopyResult(true, output, hash, info.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or CryptographicException)
        {
            TryDelete(output);
            return new MetadataEmbeddedCopyResult(
                false,
                output,
                ErrorMessage: exception.GetBaseException().Message);
        }
    }

    public async Task<MetadataEmbeddedWriteResult> WriteEmbeddedAsync(
        string mediaItemId,
        string mediaPath,
        string expectedSha256,
        long expectedLength,
        DateTime expectedLastWriteTimeUtc,
        MetadataExportValues values,
        string batchId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        var capability = GetEmbeddedCapability(mediaPath, values);
        if (!capability.Supported)
            return new MetadataEmbeddedWriteResult(false, mediaPath, ErrorMessage: capability.Message);

        var fullPath = Path.GetFullPath(mediaPath);
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != expectedLength || info.LastWriteTimeUtc != expectedLastWriteTimeUtc)
            return new MetadataEmbeddedWriteResult(false, fullPath, ErrorMessage: "Datei wurde seit dem Laden verändert.");
        var currentHash = await HashFileAsync(fullPath, cancellationToken);
        if (!string.Equals(currentHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            return new MetadataEmbeddedWriteResult(false, fullPath, ErrorMessage: "SHA-256 stimmt nicht mehr mit dem geladenen Zustand überein.");

        var packet = XmpExportDocument.CreatePacket(values);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".metadata.partial";
        var recoveryDirectory = Path.Combine(_appPaths.RecoveryDirectory, "metadata-export", batchId);
        Directory.CreateDirectory(recoveryDirectory);
        var backupPath = Path.Combine(recoveryDirectory, mediaItemId + Path.GetExtension(fullPath) + ".backup");

        try
        {
            await CopyWithFlushAsync(fullPath, backupPath, cancellationToken);
            var backupHash = await HashFileAsync(backupPath, cancellationToken);
            if (!string.Equals(backupHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recovery-Kopie stimmt nicht bytegenau mit der Quelle überein.");

            await RewriteJpegAsync(fullPath, tempPath, packet, cancellationToken);
            var tempHash = await HashFileAsync(tempPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(tempHash))
                throw new IOException("Temporäre JPEG-Datei konnte nicht verifiziert werden.");

            cancellationToken.ThrowIfCancellationRequested();
            // Ab hier kein Benutzerabbruch: Replace -> Verify -> ggf. Restore.
            File.Replace(tempPath, fullPath, null, ignoreMetadataErrors: true);

            var embedded = await ReadEmbeddedPacketAsync(fullPath, CancellationToken.None);
            if (embedded is null || !embedded.AsSpan().SequenceEqual(packet))
                throw new IOException("Eingebettetes XMP konnte nach dem Commit nicht bytegenau verifiziert werden.");

            var after = new FileInfo(fullPath);
            var newHash = await HashFileAsync(fullPath, CancellationToken.None);
            return new MetadataEmbeddedWriteResult(
                true,
                fullPath,
                backupPath,
                newHash,
                after.Length,
                after.LastWriteTimeUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or CryptographicException)
        {
            TryDelete(tempPath);
            var recoveryRequired = false;
            if (File.Exists(backupPath) && File.Exists(fullPath))
            {
                var nowHash = await HashFileAsync(fullPath, CancellationToken.None);
                if (!string.Equals(nowHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    recoveryRequired = !await RestoreEmbeddedAsync(fullPath, backupPath, nowHash ?? string.Empty, CancellationToken.None);
                }
            }
            return new MetadataEmbeddedWriteResult(false, fullPath, backupPath, ErrorMessage: exception.GetBaseException().Message, RecoveryRequired: recoveryRequired);
        }
    }

    public async Task<bool> RestoreEmbeddedAsync(
        string mediaPath,
        string backupPath,
        string expectedCurrentSha256,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(mediaPath) || !File.Exists(backupPath)) return false;
            var currentHash = await HashFileAsync(mediaPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(expectedCurrentSha256)
                && !string.Equals(currentHash, expectedCurrentSha256, StringComparison.OrdinalIgnoreCase))
                return false;
            var temp = mediaPath + "." + Guid.NewGuid().ToString("N") + ".restore.partial";
            await CopyWithFlushAsync(backupPath, temp, cancellationToken);
            File.Replace(temp, mediaPath, null, ignoreMetadataErrors: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void FinalizeBackup(string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath)) return;
        TryDelete(backupPath);
        try
        {
            var directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch { }
    }

    private static async Task RewriteJpegAsync(string sourcePath, string targetPath, byte[] packet, CancellationToken cancellationToken)
    {
        var segmentPayloadLength = XmpJpegPreamble.Length + packet.Length;
        if (segmentPayloadLength + 2 > ushort.MaxValue)
            throw new InvalidOperationException("XMP-Paket ist zu groß für ein JPEG-APP1-Segment.");

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        await using var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.WriteThrough);
        if (input.ReadByte() != 0xFF || input.ReadByte() != 0xD8)
            throw new InvalidOperationException("Datei ist kein gültiges JPEG (SOI fehlt).");
        output.WriteByte(0xFF); output.WriteByte(0xD8);

        var inserted = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = input.ReadByte();
            if (prefix != 0xFF) throw new InvalidOperationException("Ungültige JPEG-Markerstruktur.");
            int marker;
            do { marker = input.ReadByte(); } while (marker == 0xFF);
            if (marker < 0) throw new EndOfStreamException();

            if (marker == 0xDA) // SOS
            {
                if (!inserted)
                {
                    WriteXmpSegment(output, packet);
                    inserted = true;
                }
                output.WriteByte(0xFF); output.WriteByte((byte)marker);
                await CopyRestAsync(input, output, cancellationToken);
                break;
            }

            if (marker == 0xD9) throw new InvalidOperationException("JPEG enthält keinen Start-of-Scan-Marker.");
            if (marker is >= 0xD0 and <= 0xD7 || marker == 0x01)
            {
                output.WriteByte(0xFF); output.WriteByte((byte)marker);
                continue;
            }

            var lenBytes = new byte[2];
            input.ReadExactly(lenBytes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);
            if (length < 2) throw new InvalidOperationException("Ungültige JPEG-Segmentlänge.");
            var payload = new byte[length - 2];
            input.ReadExactly(payload);

            if (marker == 0xE1 && payload.AsSpan().StartsWith(XmpJpegPreamble))
            {
                var existingPacket = payload.AsSpan(XmpJpegPreamble.Length);
                if (!XmpExportDocument.IsElbwaldManaged(existingPacket))
                    throw new InvalidOperationException("JPEG enthält bereits fremdes oder unklares eingebettetes XMP. Es wird nicht überschrieben.");
                continue;
            }

            output.WriteByte(0xFF); output.WriteByte((byte)marker);
            output.Write(lenBytes);
            await output.WriteAsync(payload, cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static void WriteXmpSegment(Stream output, byte[] packet)
    {
        output.WriteByte(0xFF); output.WriteByte(0xE1);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, checked((ushort)(XmpJpegPreamble.Length + packet.Length + 2)));
        output.Write(length);
        output.Write(XmpJpegPreamble);
        output.Write(packet);
    }

    private static async Task<byte[]?> ReadEmbeddedPacketAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        if (input.ReadByte() != 0xFF || input.ReadByte() != 0xD8) return null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.ReadByte() != 0xFF) return null;
            int marker;
            do { marker = input.ReadByte(); } while (marker == 0xFF);
            if (marker < 0 || marker == 0xDA || marker == 0xD9) return null;
            if (marker is >= 0xD0 and <= 0xD7 || marker == 0x01) continue;
            var lenBytes = new byte[2]; input.ReadExactly(lenBytes);
            var length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);
            if (length < 2) return null;
            var payload = new byte[length - 2]; input.ReadExactly(payload);
            if (marker == 0xE1 && payload.AsSpan().StartsWith(XmpJpegPreamble))
                return payload.AsSpan(XmpJpegPreamble.Length).ToArray();
        }
    }

    private static async Task CopyWithFlushAsync(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.WriteThrough);
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task CopyRestAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        await input.CopyToAsync(output, 128 * 1024, cancellationToken);
    }

    private static async Task<string?> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
