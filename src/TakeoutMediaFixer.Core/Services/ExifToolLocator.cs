namespace TakeoutMediaFixer.Core.Services;

public static class ExifToolLocator
{
    public static string? Find()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "tools", "exiftool.exe"),
            Path.Combine(baseDirectory, "exiftool.exe"),
            Path.Combine(Environment.CurrentDirectory, "tools", "exiftool.exe"),
            Path.Combine(Environment.CurrentDirectory, "exiftool.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
