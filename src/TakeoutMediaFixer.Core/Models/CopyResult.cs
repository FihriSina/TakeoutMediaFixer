namespace TakeoutMediaFixer.Core.Models;

public sealed record CopyResult(
    string DestinationPath,
    bool NameCollision,
    bool ExtensionCorrected);
