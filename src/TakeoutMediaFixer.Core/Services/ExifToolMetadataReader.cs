using System.Globalization;
using System.Text.Json;
using TakeoutMediaFixer.Core.Models;
using TakeoutMediaFixer.Core.Utilities;

namespace TakeoutMediaFixer.Core.Services;

public sealed class ExifToolMetadataReader
{
    private readonly string _exifToolPath;

    public ExifToolMetadataReader(string exifToolPath)
    {
        _exifToolPath = exifToolPath;
    }

    public async Task<Dictionary<string, ExistingMetadata>> ReadAsync(
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-json",
            "-r",
            "-n",
            "-charset", "filename=UTF8",
            "-api", "QuickTimeUTC=1",
            "-DateTimeOriginal",
            "-SubSecDateTimeOriginal",
            "-CreateDate",
            "-MediaCreateDate",
            "-TrackCreateDate",
            "-CreationDate",
            "-GPSLatitude",
            "-GPSLongitude",
            outputRoot
        };

        var result = await ProcessRunner.RunAsync(_exifToolPath, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new Dictionary<string, ExistingMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            var metadata = new Dictionary<string, ExistingMetadata>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("SourceFile", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var sourceFile = sourceElement.GetString();
                if (string.IsNullOrWhiteSpace(sourceFile))
                {
                    continue;
                }

                var (timestamp, timestampSource) = FindTimestamp(item);
                var latitude = TryGetDouble(item, "GPSLatitude");
                var longitude = TryGetDouble(item, "GPSLongitude");
                var fullPath = Path.GetFullPath(sourceFile);
                metadata[fullPath] = new ExistingMetadata(fullPath, timestamp, latitude, longitude, timestampSource);
            }

            return metadata;
        }
        catch (JsonException)
        {
            return new Dictionary<string, ExistingMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static (DateTimeOffset? Timestamp, string? Source) FindTimestamp(JsonElement element)
    {
        string[] names =
        [
            "SubSecDateTimeOriginal",
            "DateTimeOriginal",
            "CreationDate",
            "MediaCreateDate",
            "TrackCreateDate",
            "CreateDate"
        ];

        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                continue;
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };

            if (ExifDateParser.TryParse(text, out var parsed))
            {
                return (parsed, name);
            }
        }

        return (null, null);
    }

    private static double? TryGetDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
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
}
