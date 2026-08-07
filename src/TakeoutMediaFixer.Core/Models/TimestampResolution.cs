namespace TakeoutMediaFixer.Core.Models;

public sealed record TimestampResolution(
    DateTimeOffset Timestamp,
    string Source,
    TimestampPrecision Precision,
    int Priority,
    double? Latitude = null,
    double? Longitude = null,
    double? Altitude = null,
    string? EvidencePath = null);
