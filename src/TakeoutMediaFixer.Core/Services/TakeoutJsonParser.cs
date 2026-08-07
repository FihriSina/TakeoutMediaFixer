using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public sealed class TakeoutJsonParser
{
    public TakeoutMetadata? TryParse(string jsonPath)
    {
        try
        {
            using var stream = new FileStream(jsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            var root = document.RootElement;
            var title = TryGetString(root, "title");
            var timestamp = TryReadTimestamp(root, "photoTakenTime")
                            ?? TryReadTimestamp(root, "creationTime")
                            ?? TryReadTimestamp(root, "photoLastModifiedTime");

            var (latitude, longitude, altitude) = TryReadGeo(root, "geoDataExif");
            if (latitude is null || longitude is null)
            {
                (latitude, longitude, altitude) = TryReadGeo(root, "geoData");
            }

            if (latitude == 0 && longitude == 0)
            {
                latitude = null;
                longitude = null;
                altitude = null;
            }

            var fileNameFromJson = DeriveTargetFileName(jsonPath);
            return new TakeoutMetadata(
                jsonPath,
                Path.GetDirectoryName(jsonPath) ?? string.Empty,
                title,
                fileNameFromJson,
                timestamp,
                latitude,
                longitude,
                altitude);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset? TryReadTimestamp(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var timeElement) || timeElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (timeElement.TryGetProperty("timestamp", out var timestampElement))
        {
            string? raw = timestampElement.ValueKind switch
            {
                JsonValueKind.String => timestampElement.GetString(),
                JsonValueKind.Number => timestampElement.GetRawText(),
                _ => null
            };

            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            {
                try
                {
                    var utc = raw?.Length >= 13
                        ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        : DateTimeOffset.FromUnixTimeSeconds(epoch);
                    return TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Formatted alanı denenir.
                }
            }
        }

        if (timeElement.TryGetProperty("formatted", out var formattedElement) && formattedElement.ValueKind == JsonValueKind.String)
        {
            var formatted = formattedElement.GetString();
            if (DateTimeOffset.TryParse(
                    formatted,
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                return parsed;
            }

            if (DateTimeOffset.TryParse(
                    formatted,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                    out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static (double? Latitude, double? Longitude, double? Altitude) TryReadGeo(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var geo) || geo.ValueKind != JsonValueKind.Object)
        {
            return (null, null, null);
        }

        return (
            TryGetDouble(geo, "latitude"),
            TryGetDouble(geo, "longitude"),
            TryGetDouble(geo, "altitude"));
    }

    private static string? TryGetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static string DeriveTargetFileName(string jsonPath)
    {
        var name = Path.GetFileName(jsonPath);
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        // Takeout, uzun yan dosya adlarını bazen "supplemental-metadata"
        // ifadesinin ortasında keser veya sonuna (1)/(2) ekler. JSON içindeki
        // title alanı birincil eşleşmedir; bu temizleme ilave geri dönüş sağlar.
        name = Regex.Replace(
            name,
            @"\.supplemental-meta(?:data)?(?:\(\d+\))?$|\.supplemental-metadat(?:\(\d+\))?$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return name;
    }
}
