namespace TakeoutMediaFixer.Core.Utilities;

public static class CsvUtility
{
    public static string Escape(string? value)
    {
        value ??= string.Empty;
        if (value.Contains('"'))
        {
            value = value.Replace("\"", "\"\"");
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value}\"" : value;
    }
}
