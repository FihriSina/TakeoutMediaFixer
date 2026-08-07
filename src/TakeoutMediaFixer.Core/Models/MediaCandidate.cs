namespace TakeoutMediaFixer.Core.Models;

public sealed record MediaCandidate(
    string SourcePath,
    MediaKind Kind,
    string SuggestedExtension,
    TimestampResolution? CandidateTimestamp,
    double? Latitude = null,
    double? Longitude = null,
    double? Altitude = null);
