namespace TakeoutMediaFixer.Core.Models;

public sealed record ExistingMetadata(
    string FilePath,
    DateTimeOffset? Timestamp,
    double? Latitude,
    double? Longitude,
    string? RawTimestampSource);
