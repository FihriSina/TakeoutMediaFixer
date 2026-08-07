namespace TakeoutMediaFixer.Core.Models;

public sealed class ProcessingRecord
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public required MediaKind Kind { get; init; }
    public string? Timestamp { get; set; }
    public string? TimestampSource { get; set; }
    public string? Precision { get; set; }
    public string MetadataStatus { get; set; } = "Bekliyor";
    public string FileNameStatus { get; set; } = "Korundu";
    public string? Error { get; set; }
}
