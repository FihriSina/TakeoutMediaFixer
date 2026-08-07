using System.Globalization;
using System.Text.RegularExpressions;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Utilities;

public static class DatePatternParser
{
    private static readonly Regex CameraPattern = new(
        @"(?:^|[^0-9])(?:IMG|VID|PXL)_(?<date>\d{8})_(?<time>\d{6})(?<ms>\d{3})?(?:\D|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ScreenshotPattern = new(
        @"Screenshot[_-](?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})-(?<hour>\d{2})-(?<minute>\d{2})-(?<second>\d{2})(?:-(?<ms>\d{3}))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CompactScreenshotPattern = new(
        @"Screenshot[_-](?<date>\d{8})[_-](?<time>\d{6})(?<ms>\d{3})?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericCameraPattern = new(
        @"^(?<date>\d{8})[_-](?<time>\d{6})(?<ms>\d{3})?(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhatsAppPattern = new(
        @"(?:^|[^A-Z])(?:IMG|VID)-(?<date>\d{8})-WA\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FacebookEpochPattern = new(
        @"FB_IMG_(?<epoch>\d{13})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RawEpochPattern = new(
        @"^(?<epoch>\d{13})(?:\D|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PhotoboothPattern = new(
        @"(?:^|.*[_-])Photobooth_(?<year>\d{4})-(?<month>\d{1,2})-(?<day>\d{1,2})--(?<hour>\d{1,2})-(?<minute>\d{1,2})-(?<second>\d{1,2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static TimestampResolution? TryResolve(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        var facebook = FacebookEpochPattern.Match(fileName);
        if (facebook.Success && long.TryParse(facebook.Groups["epoch"].Value, out var facebookEpoch))
        {
            return FromEpochMilliseconds(facebookEpoch, "Dosya adı: Facebook Unix zamanı", 90);
        }

        var rawEpoch = RawEpochPattern.Match(fileName);
        if (rawEpoch.Success && long.TryParse(rawEpoch.Groups["epoch"].Value, out var epoch))
        {
            return FromEpochMilliseconds(epoch, "Dosya adı: Unix milisaniye zamanı", 90);
        }

        var camera = CameraPattern.Match(fileName);
        if (camera.Success)
        {
            var dateText = camera.Groups["date"].Value;
            var timeText = camera.Groups["time"].Value;
            var msText = camera.Groups["ms"].Value;
            var format = string.IsNullOrWhiteSpace(msText) ? "yyyyMMddHHmmss" : "yyyyMMddHHmmssfff";
            var value = dateText + timeText + msText;

            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            {
                return new TimestampResolution(
                    ToLocalOffset(localDate),
                    "Dosya adı: Kamera biçimi",
                    string.IsNullOrWhiteSpace(msText) ? TimestampPrecision.Exact : TimestampPrecision.Milliseconds,
                    80);
            }
        }


        var compactScreenshot = CompactScreenshotPattern.Match(fileName);
        if (compactScreenshot.Success && TryParseCompactDate(compactScreenshot, out var compactScreenshotDate, out var compactScreenshotPrecision))
        {
            return new TimestampResolution(
                ToLocalOffset(compactScreenshotDate),
                "Dosya adı: Kompakt ekran görüntüsü biçimi",
                compactScreenshotPrecision,
                80);
        }

        var genericCamera = GenericCameraPattern.Match(fileName);
        if (genericCamera.Success && TryParseCompactDate(genericCamera, out var genericDate, out var genericPrecision))
        {
            return new TimestampResolution(
                ToLocalOffset(genericDate),
                "Dosya adı: Genel tarih-saat biçimi",
                genericPrecision,
                70);
        }

        var screenshot = ScreenshotPattern.Match(fileName);
        if (screenshot.Success && TryBuildDate(screenshot, out var screenshotDate))
        {
            var milliseconds = ParseInt(screenshot.Groups["ms"].Value);
            screenshotDate = screenshotDate.AddMilliseconds(milliseconds);
            return new TimestampResolution(
                ToLocalOffset(screenshotDate),
                "Dosya adı: Ekran görüntüsü biçimi",
                screenshot.Groups["ms"].Success ? TimestampPrecision.Milliseconds : TimestampPrecision.Exact,
                80);
        }

        var photobooth = PhotoboothPattern.Match(fileName);
        if (photobooth.Success && TryBuildDate(photobooth, out var photoboothDate))
        {
            return new TimestampResolution(
                ToLocalOffset(photoboothDate),
                "Dosya adı: Photobooth biçimi",
                TimestampPrecision.Exact,
                80);
        }

        var whatsapp = WhatsAppPattern.Match(fileName);
        if (whatsapp.Success && DateTime.TryParseExact(
                whatsapp.Groups["date"].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOnly))
        {
            // Saat bilgisi dosya adında bulunmadığı için gün kaymasını önlemek amacıyla öğlen kullanılır.
            var noon = dateOnly.Date.AddHours(12);
            return new TimestampResolution(
                ToLocalOffset(noon),
                "Dosya adı: WhatsApp tarihi (saat bilinmiyor, 12:00 kullanıldı)",
                TimestampPrecision.DateOnly,
                50);
        }

        return null;
    }


    private static bool TryParseCompactDate(Match match, out DateTime value, out TimestampPrecision precision)
    {
        value = default;
        precision = TimestampPrecision.Exact;
        var milliseconds = match.Groups["ms"].Value;
        var format = string.IsNullOrWhiteSpace(milliseconds) ? "yyyyMMddHHmmss" : "yyyyMMddHHmmssfff";
        precision = string.IsNullOrWhiteSpace(milliseconds) ? TimestampPrecision.Exact : TimestampPrecision.Milliseconds;
        return DateTime.TryParseExact(
            match.Groups["date"].Value + match.Groups["time"].Value + milliseconds,
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out value);
    }

    private static TimestampResolution? FromEpochMilliseconds(long epoch, string source, int priority)
    {
        try
        {
            var utc = DateTimeOffset.FromUnixTimeMilliseconds(epoch);
            var local = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.Local);
            if (local.Year is < 1990 or > 2100)
            {
                return null;
            }

            return new TimestampResolution(local, source, TimestampPrecision.Milliseconds, priority);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool TryBuildDate(Match match, out DateTime value)
    {
        value = default;
        try
        {
            value = new DateTime(
                ParseInt(match.Groups["year"].Value),
                ParseInt(match.Groups["month"].Value),
                ParseInt(match.Groups["day"].Value),
                ParseInt(match.Groups["hour"].Value),
                ParseInt(match.Groups["minute"].Value),
                ParseInt(match.Groups["second"].Value),
                DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : 0;

    private static DateTimeOffset ToLocalOffset(DateTime localDate)
    {
        localDate = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(localDate))
        {
            localDate = localDate.AddHours(1);
        }

        var offset = TimeZoneInfo.Local.GetUtcOffset(localDate);
        return new DateTimeOffset(localDate, offset);
    }
}
