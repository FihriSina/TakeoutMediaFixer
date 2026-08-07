namespace TakeoutMediaFixer.Core.Models;

public sealed record TakeoutMetadata(
    string JsonPath,
    string DirectoryPath,
    string? Title,
    string? FileNameFromJson,
    DateTimeOffset? Timestamp,
    double? Latitude,
    double? Longitude,
    double? Altitude);
