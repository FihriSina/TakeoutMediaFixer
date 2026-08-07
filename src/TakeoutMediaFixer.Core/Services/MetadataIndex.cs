using System.Text;
using TakeoutMediaFixer.Core.Models;
using TakeoutMediaFixer.Core.Utilities;

namespace TakeoutMediaFixer.Core.Services;

public sealed class MetadataIndex
{
    private readonly Dictionary<string, List<TakeoutMetadata>> _byDirectoryAndName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TakeoutMetadata>> _byName = new(StringComparer.OrdinalIgnoreCase);

    public int Count { get; private set; }

    public void Add(TakeoutMetadata metadata)
    {
        Count++;
        AddName(metadata, metadata.Title);
        AddName(metadata, metadata.FileNameFromJson);
    }

    public (TimestampResolution? Timestamp, double? Latitude, double? Longitude, double? Altitude) Resolve(string mediaPath)
    {
        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var fileName = Path.GetFileName(mediaPath);
        var metadata = FindExact(directory, fileName) ?? FindGloballyUnique(fileName);
        var fallback = DatePatternParser.TryResolve(mediaPath);

        if (metadata?.Timestamp is not null)
        {
            return (
                new TimestampResolution(
                    metadata.Timestamp.Value,
                    "Google Takeout JSON",
                    TimestampPrecision.Exact,
                    100,
                    metadata.Latitude,
                    metadata.Longitude,
                    metadata.Altitude,
                    metadata.JsonPath),
                metadata.Latitude,
                metadata.Longitude,
                metadata.Altitude);
        }

        if (fallback is not null && metadata is not null)
        {
            fallback = fallback with
            {
                Latitude = metadata.Latitude,
                Longitude = metadata.Longitude,
                Altitude = metadata.Altitude,
                EvidencePath = metadata.JsonPath
            };
        }

        return (fallback, metadata?.Latitude, metadata?.Longitude, metadata?.Altitude);
    }

    private void AddName(TakeoutMetadata metadata, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var normalizedName = NormalizeName(name);
        var exactKey = BuildExactKey(metadata.DirectoryPath, normalizedName);
        AddToDictionary(_byDirectoryAndName, exactKey, metadata);
        AddToDictionary(_byName, normalizedName, metadata);
    }

    private TakeoutMetadata? FindExact(string directory, string fileName)
    {
        var key = BuildExactKey(directory, NormalizeName(fileName));
        return _byDirectoryAndName.TryGetValue(key, out var matches)
            ? ChooseBest(matches)
            : null;
    }

    private TakeoutMetadata? FindGloballyUnique(string fileName)
    {
        var key = NormalizeName(fileName);
        if (!_byName.TryGetValue(key, out var matches))
        {
            return null;
        }

        var distinct = matches.DistinctBy(item => item.JsonPath, StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static TakeoutMetadata? ChooseBest(IEnumerable<TakeoutMetadata> matches) =>
        matches
            .DistinctBy(item => item.JsonPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => item.Timestamp.HasValue)
            .ThenByDescending(item => item.Latitude.HasValue && item.Longitude.HasValue)
            .FirstOrDefault();

    private static void AddToDictionary(
        Dictionary<string, List<TakeoutMetadata>> dictionary,
        string key,
        TakeoutMetadata metadata)
    {
        if (!dictionary.TryGetValue(key, out var list))
        {
            list = [];
            dictionary[key] = list;
        }

        if (!list.Any(item => string.Equals(item.JsonPath, metadata.JsonPath, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(metadata);
        }
    }

    private static string BuildExactKey(string directory, string normalizedName) =>
        NormalizeDirectory(directory) + "|" + normalizedName;

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Normalize(NormalizationForm.FormC);

    private static string NormalizeName(string name) =>
        Path.GetFileName(name).Trim().Normalize(NormalizationForm.FormC);
}
