using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Elbwald.DesktopTools.Contracts.Media.Naming;

namespace Elbwald.DesktopTools.Core.Media.Naming;

public sealed class MediaNamingTemplateEngine : IMediaNamingTemplateEngine
{
    private static readonly Regex TokenRegex = new(
        "\\{(?<name>[a-z]+)(?::(?<format>[^}]+))?\\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "time", "year", "month", "day", "camera", "make", "model",
        "title", "description", "original", "ext", "type", "width", "height",
        "folder", "seq"
    };

    public string RenderFileStem(string pattern, MediaNamingContext context)
    {
        Validate(pattern);

        var rendered = TokenRegex.Replace(pattern, match =>
        {
            var name = match.Groups["name"].Value.ToLowerInvariant();
            var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;

            return name switch
            {
                "date" => context.CaptureDate.ToString(format ?? "yyyyMMdd", CultureInfo.InvariantCulture),
                "time" => context.CaptureDate.ToString(format ?? "HHmmss", CultureInfo.InvariantCulture),
                "year" => context.CaptureDate.ToString(format ?? "yyyy", CultureInfo.InvariantCulture),
                "month" => context.CaptureDate.ToString(format ?? "MM", CultureInfo.InvariantCulture),
                "day" => context.CaptureDate.ToString(format ?? "dd", CultureInfo.InvariantCulture),
                "camera" => Clean(JoinCamera(context.CameraMake, context.CameraModel), "OhneKamera"),
                "make" => Clean(context.CameraMake, "OhneHersteller"),
                "model" => Clean(context.CameraModel, "OhneModell"),
                "title" => Clean(context.Title, "OhneTitel"),
                "description" => Clean(context.Description, "OhneBeschreibung"),
                "original" => Clean(context.OriginalStem, "_"),
                "ext" => Clean(context.Extension.TrimStart('.'), "datei"),
                "type" => Clean(context.MediaType, "Medium"),
                "width" => context.Width?.ToString(CultureInfo.InvariantCulture) ?? "0",
                "height" => context.Height?.ToString(CultureInfo.InvariantCulture) ?? "0",
                "folder" => Clean(context.FolderName, "Ordner"),
                "seq" => context.Sequence.ToString(format ?? "000", CultureInfo.InvariantCulture),
                _ => throw new FormatException($"Unbekannter Platzhalter: {{{name}}}")
            };
        });

        ValidateRenderedStem(rendered);
        return rendered;
    }

    public void Validate(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new FormatException("Die Umbenennungsregel darf nicht leer sein.");
        }

        foreach (Match match in TokenRegex.Matches(pattern))
        {
            var name = match.Groups["name"].Value;
            if (!KnownTokens.Contains(name))
            {
                throw new FormatException($"Unbekannter Platzhalter: {{{name}}}");
            }

            if (name.Equals("seq", StringComparison.OrdinalIgnoreCase) && match.Groups["format"].Success)
            {
                var format = match.Groups["format"].Value;
                if (format.Length is < 1 or > 8 || format.Any(c => c != '0'))
                {
                    throw new FormatException("Für {seq:...} sind nur 1 bis 8 Nullen erlaubt, z. B. {seq:000}.");
                }
            }
        }

        var withoutTokens = TokenRegex.Replace(pattern, string.Empty);
        if (withoutTokens.Contains('{') || withoutTokens.Contains('}'))
        {
            throw new FormatException("Die Regel enthält einen ungültigen Platzhalter.");
        }
    }

    private static void ValidateRenderedStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) throw new FormatException("Die Regel erzeugt einen leeren Dateinamen.");
        if (stem.Length > 180) throw new FormatException("Die Regel erzeugt einen zu langen Dateinamen (> 180 Zeichen vor der Erweiterung).");
        if (stem is "." or "..") throw new FormatException("Die Regel erzeugt einen ungültigen Dateinamen.");
    }

    private static string JoinCamera(string? make, string? model)
    {
        var parts = new[] { make, model }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" ", parts);
    }

    private static string Clean(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var c in "<>:\"/\\|?*") invalid.Add(c);

        var builder = new StringBuilder(value.Trim().Length);
        foreach (var c in value.Trim()) builder.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);

        var result = Regex.Replace(builder.ToString(), "\\s+", " ").Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
