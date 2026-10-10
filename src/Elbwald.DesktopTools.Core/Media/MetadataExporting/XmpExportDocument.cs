using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Elbwald.DesktopTools.Contracts.Media.MetadataEditing;
using Elbwald.DesktopTools.Contracts.Media.MetadataExporting;

namespace Elbwald.DesktopTools.Core.Media.MetadataExporting;

internal static class XmpExportDocument
{
    internal static readonly XNamespace XmpMeta = "adobe:ns:meta/";
    internal static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    internal static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    internal static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    internal static readonly XNamespace Exif = "http://ns.adobe.com/exif/1.0/";
    internal static readonly XNamespace Tiff = "http://ns.adobe.com/tiff/1.0/";
    internal static readonly XNamespace IptcCore = "http://iptc.org/std/Iptc4xmpCore/1.0/xmlns/";
    internal static readonly XNamespace IptcExt = "http://iptc.org/std/Iptc4xmpExt/2008-02-29/";
    internal static readonly XNamespace Elbwald = "https://elbwald.digital/ns/metadata/1.0/";

    public static byte[] CreatePacket(MetadataExportValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var description = new XElement(
            Rdf + "Description",
            new XAttribute(XNamespace.Xmlns + "dc", Dc),
            new XAttribute(XNamespace.Xmlns + "xmp", Xmp),
            new XAttribute(XNamespace.Xmlns + "exif", Exif),
            new XAttribute(XNamespace.Xmlns + "tiff", Tiff),
            new XAttribute(XNamespace.Xmlns + "Iptc4xmpCore", IptcCore),
            new XAttribute(XNamespace.Xmlns + "Iptc4xmpExt", IptcExt),
            new XAttribute(XNamespace.Xmlns + "elbwald", Elbwald),
            new XAttribute(Elbwald + "managedBy", "Elbwald Digital"),
            new XAttribute(Elbwald + "schemaVersion", "3"),
            new XAttribute(Elbwald + "updatedUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));

        if (values.CaptureDateOverridden)
        {
            description.Add(new XAttribute(Elbwald + "captureDateOverridden", "true"));
            if (values.CaptureDate.HasValue)
            {
                var text = values.CaptureDate.Value.ToString("O", CultureInfo.InvariantCulture);
                description.Add(new XAttribute(Xmp + "CreateDate", text));
                description.Add(new XAttribute(Exif + "DateTimeOriginal", text));
                description.Add(new XAttribute(Elbwald + "captureDate", text));
            }
        }

        if (values.TitleOverridden)
        {
            description.Add(new XAttribute(Elbwald + "titleOverridden", "true"));
            description.Add(CreateAltText(Dc + "title", values.Title ?? string.Empty));
        }

        if (values.DescriptionOverridden)
        {
            description.Add(new XAttribute(Elbwald + "descriptionOverridden", "true"));
            description.Add(CreateAltText(Dc + "description", values.Description ?? string.Empty));
        }

        if (values.GpsMode == MetadataGpsMode.Remove)
        {
            description.Add(new XAttribute(Elbwald + "removeGps", "true"));
        }
        else if (values.GpsMode == MetadataGpsMode.Set
                 && values.Latitude.HasValue
                 && values.Longitude.HasValue)
        {
            description.Add(new XAttribute(Elbwald + "gpsMode", "set"));
            description.Add(new XAttribute(Exif + "GPSLatitude", FormatCoordinate(values.Latitude.Value, true)));
            description.Add(new XAttribute(Exif + "GPSLongitude", FormatCoordinate(values.Longitude.Value, false)));
            description.Add(new XAttribute(Elbwald + "latitude", values.Latitude.Value.ToString("R", CultureInfo.InvariantCulture)));
            description.Add(new XAttribute(Elbwald + "longitude", values.Longitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (values.Rating is >= 1 and <= 3)
        {
            description.Add(new XAttribute(Xmp + "Rating", values.Rating.ToString(CultureInfo.InvariantCulture)));
            description.Add(new XAttribute(Elbwald + "ratingScale", "3"));
        }

        if (!string.IsNullOrWhiteSpace(values.CameraMake))
        {
            description.Add(new XAttribute(Tiff + "Make", values.CameraMake.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(values.CameraModel))
        {
            description.Add(new XAttribute(Tiff + "Model", values.CameraModel.Trim()));
        }

        AddBag(description, Dc + "subject", values.Tags);
        AddBag(description, IptcExt + "PersonInImage", values.Persons);
        AddBag(description, Elbwald + "places", values.Places);
        AddBag(description, Elbwald + "events", values.Events);

        var firstPlace = values.Places?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(firstPlace))
        {
            description.Add(new XAttribute(IptcCore + "Location", firstPlace.Trim()));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                XmpMeta + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", XmpMeta),
                new XElement(
                    Rdf + "RDF",
                    new XAttribute(XNamespace.Xmlns + "rdf", Rdf),
                    description)));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false,
            CloseOutput = false
        };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    public static bool IsElbwaldManaged(ReadOnlySpan<byte> packet)
    {
        try
        {
            using var stream = new MemoryStream(packet.ToArray(), writable: false);
            var document = XDocument.Load(stream, LoadOptions.None);
            var description = document.Descendants(Rdf + "Description").FirstOrDefault();
            return string.Equals((string?)description?.Attribute(Elbwald + "managedBy"), "Elbwald Digital", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void AddBag(XElement description, XName name, IReadOnlyList<string>? values)
    {
        if (values is not { Count: > 0 })
        {
            return;
        }

        var items = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Select(value => new XElement(Rdf + "li", value))
            .ToArray();

        if (items.Length > 0)
        {
            description.Add(new XElement(name, new XElement(Rdf + "Bag", items)));
        }
    }

    private static XElement CreateAltText(XName name, string value) =>
        new(name,
            new XElement(
                Rdf + "Alt",
                new XElement(
                    Rdf + "li",
                    new XAttribute(XNamespace.Xml + "lang", "x-default"),
                    value)));

    private static string FormatCoordinate(double value, bool latitude)
    {
        var direction = latitude
            ? (value < 0 ? "S" : "N")
            : (value < 0 ? "W" : "E");
        var absolute = Math.Abs(value);
        var degrees = Math.Floor(absolute);
        var minutes = (absolute - degrees) * 60d;
        return string.Create(CultureInfo.InvariantCulture, $"{degrees:0},{minutes:0.######}{direction}");
    }
}
