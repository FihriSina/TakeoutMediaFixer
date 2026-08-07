namespace TakeoutMediaFixer.Core.Models;

public sealed record ExifToolCommandResult(
    bool Success,
    string StandardOutput,
    string StandardError);
