using System.Globalization;
using System.Text.RegularExpressions;

namespace TakeoutMediaFixer.Core.Utilities;

public static class ExifDateParser
{
    private static readonly Regex ExifPattern = new(
        @"^(?<year>\d{4}):(?<month>\d{2}):(?<day>\d{2})[ T](?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2})(?:\.(?<fraction>\d+))?(?<offset>Z|[+-]\d{2}:?\d{2})?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = ExifPattern.Match(value.Trim());
        if (!match.Success)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out timestamp);
        }

        try
        {
            var fraction = match.Groups["fraction"].Value;
            var milliseconds = 0;
            if (!string.IsNullOrWhiteSpace(fraction))
            {
                var padded = fraction.PadRight(3, '0');
                milliseconds = int.Parse(padded[..3], CultureInfo.InvariantCulture);
            }

            var date = new DateTime(
                int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture),
                milliseconds,
                DateTimeKind.Unspecified);

            var offsetText = match.Groups["offset"].Value;
            if (string.Equals(offsetText, "Z", StringComparison.OrdinalIgnoreCase))
            {
                timestamp = new DateTimeOffset(date, TimeSpan.Zero);
            }
            else if (!string.IsNullOrWhiteSpace(offsetText))
            {
                if (offsetText.Length == 5 && offsetText[3] != ':')
                {
                    offsetText = offsetText.Insert(3, ":");
                }

                timestamp = new DateTimeOffset(date, TimeSpan.Parse(offsetText, CultureInfo.InvariantCulture));
            }
            else
            {
                if (TimeZoneInfo.Local.IsInvalidTime(date))
                {
                    date = date.AddHours(1);
                }

                timestamp = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
            }

            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
