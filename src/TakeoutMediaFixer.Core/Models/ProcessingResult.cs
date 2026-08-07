namespace TakeoutMediaFixer.Core.Models;

public sealed class ProcessingResult
{
    public required string SourceRoot { get; init; }
    public required string OutputRoot { get; init; }
    public required string ImageOutputDirectory { get; init; }
    public required string VideoOutputDirectory { get; init; }
    public required string CsvReportPath { get; init; }
    public required string SummaryPath { get; init; }
    public required string PhoneGuidePath { get; init; }
    public int TotalFilesScanned { get; set; }
    public int JsonFilesFound { get; set; }
    public int ImagesCopied { get; set; }
    public int VideosCopied { get; set; }
    public int ExistingMetadataPreserved { get; set; }
    public int MetadataWritten { get; set; }
    public int FileSystemDateOnly { get; set; }
    public int Unresolved { get; set; }
    public int ExtensionCorrections { get; set; }
    public int NameCollisions { get; set; }
    public int Errors { get; set; }
    public bool ExifToolAvailable { get; set; }
    public TimeSpan Duration { get; set; }
    public List<ProcessingRecord> Records { get; } = [];
}
