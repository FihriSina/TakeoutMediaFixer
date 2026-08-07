namespace TakeoutMediaFixer.Core.Models;

public sealed record ProcessingProgress(
    string Stage,
    string Message,
    int Current,
    int Total,
    int Images,
    int Videos,
    int MetadataWritten,
    int Unresolved)
{
    public double Percentage => Total <= 0 ? 0 : Math.Clamp((double)Current / Total * 100, 0, 100);
}
