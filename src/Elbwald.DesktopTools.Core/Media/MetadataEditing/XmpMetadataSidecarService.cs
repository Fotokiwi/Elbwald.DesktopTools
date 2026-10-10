using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;

namespace Elbwald.DesktopTools.Core.Media.MetadataEditing;

public sealed class XmpMetadataSidecarService : IMetadataSidecarService
{
    private static readonly XNamespace XmpMeta = "adobe:ns:meta/";
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Elbwald = "https://elbwald.digital/ns/metadata/1.0/";

    public Task<MetadataSidecarSnapshot> ReadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        return Task.Run(() => ReadCore(mediaPath, cancellationToken), cancellationToken);
    }

    public Task<MetadataSidecarWriteResult> WriteAsync(
        string mediaPath,
        MetadataEditValues values,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaPath);
        ArgumentNullException.ThrowIfNull(values);
        return Task.Run(() => WriteCore(mediaPath, values, cancellationToken), cancellationToken);
    }

    private static MetadataSidecarSnapshot ReadCore(
        string mediaPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sidecarPath = GetSidecarPath(mediaPath);

        if (!File.Exists(sidecarPath))
        {
            return new MetadataSidecarSnapshot(sidecarPath, false, false, null, null, null, null, null, false);
        }

        try
        {
            using var stream = new FileStream(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.SequentialScan);
            var document = XDocument.Load(stream, LoadOptions.None);
            cancellationToken.ThrowIfCancellationRequested();

            var description = document.Descendants(Rdf + "Description").FirstOrDefault();
            if (description is null)
            {
                return new MetadataSidecarSnapshot(sidecarPath, true, false, null, null, null, null, null, false, "Sidecar enthält keine RDF-Beschreibung.");
            }

            var managedBy = (string?)description.Attribute(Elbwald + "managedBy");
            var managed = string.Equals(managedBy, "Elbwald Digital", StringComparison.Ordinal);
            if (!managed)
            {
                return new MetadataSidecarSnapshot(sidecarPath, true, false, null, null, null, null, null, false, "Vorhandene Sidecar stammt nicht eindeutig von Elbwald Digital.");
            }

            var schemaVersion = (string?)description.Attribute(Elbwald + "schemaVersion");
            if (!string.Equals(schemaVersion, "1", StringComparison.Ordinal)
                && !string.Equals(schemaVersion, "2", StringComparison.Ordinal))
            {
                return new MetadataSidecarSnapshot(sidecarPath, true, false, null, null, null, null, null, false, "Elbwald-Sidecar verwendet eine unbekannte Schema-Version und wird nicht überschrieben.");
            }

            DateTime? captureDate = TryParseDate((string?)description.Attribute(Elbwald + "captureDate"));
            double? latitude = TryParseDouble((string?)description.Attribute(Elbwald + "latitude"));
            double? longitude = TryParseDouble((string?)description.Attribute(Elbwald + "longitude"));
            var removeGps = bool.TryParse((string?)description.Attribute(Elbwald + "removeGps"), out var remove) && remove;

            var title = ReadAltText(description.Element(Dc + "title"));
            var text = ReadAltText(description.Element(Dc + "description"));

            return new MetadataSidecarSnapshot(sidecarPath, true, true, captureDate, title, text, latitude, longitude, removeGps);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
        {
            return new MetadataSidecarSnapshot(sidecarPath, true, false, null, null, null, null, null, false, exception.GetBaseException().Message);
        }
    }

    private static MetadataSidecarWriteResult WriteCore(
        string mediaPath,
        MetadataEditValues values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(mediaPath))
        {
            return new MetadataSidecarWriteResult(false, GetSidecarPath(mediaPath), ErrorMessage: "Mediendatei existiert nicht mehr.");
        }

        var existing = ReadCore(mediaPath, cancellationToken);
        if (existing.Exists && !existing.IsElbwaldManaged)
        {
            return new MetadataSidecarWriteResult(false, existing.SidecarPath, ErrorMessage: "Vorhandene fremde oder unklare Sidecar wird nicht überschrieben.");
        }

        var sidecarPath = existing.SidecarPath;
        var directory = Path.GetDirectoryName(sidecarPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return new MetadataSidecarWriteResult(false, sidecarPath, ErrorMessage: "Zielverzeichnis der Sidecar ist nicht verfügbar.");
        }

        var document = CreateDocument(values);
        var tempPath = sidecarPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        string? backupPath = null;

        try
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = false,
                CloseOutput = false
            };

            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    document.Save(writer);
                }

                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(sidecarPath))
            {
                backupPath = sidecarPath + ".bak-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                File.Replace(tempPath, sidecarPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, sidecarPath, overwrite: false);
            }

            return new MetadataSidecarWriteResult(true, sidecarPath, backupPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            TryDeleteTemp(tempPath);
            return new MetadataSidecarWriteResult(false, sidecarPath, backupPath, exception.GetBaseException().Message);
        }
    }

    private static XDocument CreateDocument(MetadataEditValues values)
    {
        var description = new XElement(
            Rdf + "Description",
            new XAttribute(XNamespace.Xmlns + "elbwald", Elbwald),
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(Elbwald + "managedBy", "Elbwald Digital"),
            new XAttribute(Elbwald + "schemaVersion", "1"),
            new XAttribute(Elbwald + "updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

        if (values.CaptureDate.HasValue)
        {
            description.Add(new XAttribute(Elbwald + "captureDate", values.CaptureDate.Value.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(values.Title))
        {
            description.Add(CreateAltText(Dc + "title", values.Title.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(values.Description))
        {
            description.Add(CreateAltText(Dc + "description", values.Description.Trim()));
        }

        if (values.RemoveGps)
        {
            description.Add(new XAttribute(Elbwald + "removeGps", "true"));
        }
        else if (values.Latitude.HasValue && values.Longitude.HasValue)
        {
            description.Add(new XAttribute(Elbwald + "latitude", values.Latitude.Value.ToString("R", CultureInfo.InvariantCulture)));
            description.Add(new XAttribute(Elbwald + "longitude", values.Longitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                XmpMeta + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XmpMeta),
                new XElement(
                    Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                    description)));
    }

    private static XElement CreateAltText(XName name, string value) =>
        new(name,
            new XElement(
                Rdf + "Alt",
                new XElement(
                    Rdf + "li",
                    new XAttribute(XNamespace.Xml + "lang", "x-default"),
                    value)));

    private static string? ReadAltText(XElement? element) =>
        element?.Descendants(Rdf + "li").FirstOrDefault()?.Value;

    private static DateTime? TryParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static double? TryParseDouble(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string GetSidecarPath(string mediaPath) => Path.GetFullPath(mediaPath) + ".xmp";

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort only. A leftover .partial is explicit and never treated as committed metadata.
        }
    }
}
