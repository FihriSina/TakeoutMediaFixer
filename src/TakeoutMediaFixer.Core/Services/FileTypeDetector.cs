using System.Text;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public sealed class FileTypeDetector
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".gif", ".bmp", ".tif", ".tiff",
        ".avif", ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf"
    };

    private static readonly HashSet<string> RawImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dng", ".cr2", ".cr3", ".nef", ".arw", ".orf", ".rw2", ".raf"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".avi", ".mkv", ".3gp", ".3g2", ".m4v", ".webm", ".mts", ".m2ts", ".mpeg", ".mpg"
    };

    public (MediaKind Kind, string SuggestedExtension) Detect(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        var signature = ReadSignature(filePath);
        var detectedExtension = DetectBySignature(signature);

        if (detectedExtension is not null)
        {
            var normalizedOriginal = extension.ToLowerInvariant();

            // Birçok RAW kamera biçimi TIFF veya ISO-BMFF imzası taşır. İmzayı
            // doğrudan .tif/.mp4 saymak veri türünü yanlış sınıflandırabilir.
            if (RawImageExtensions.Contains(normalizedOriginal))
            {
                return (MediaKind.Image, normalizedOriginal);
            }

            var kind = IsVideoExtension(detectedExtension) ? MediaKind.Video : MediaKind.Image;

            if (kind == MediaKind.Video && VideoExtensions.Contains(normalizedOriginal))
            {
                // ISO-BMFF markası tek başına .mp4/.mov/.m4v ayrımını her zaman kesin vermez.
                return (kind, normalizedOriginal);
            }

            if (kind == MediaKind.Image && AreEquivalentImageExtensions(normalizedOriginal, detectedExtension))
            {
                return (kind, normalizedOriginal);
            }

            return (kind, detectedExtension);
        }

        if (ImageExtensions.Contains(extension))
        {
            return (MediaKind.Image, extension.ToLowerInvariant());
        }

        if (VideoExtensions.Contains(extension))
        {
            return (MediaKind.Video, extension.ToLowerInvariant());
        }

        return (MediaKind.Unknown, extension.ToLowerInvariant());
    }

    public static bool IsImageExtension(string extension) => ImageExtensions.Contains(extension);

    public static bool IsVideoExtension(string extension) => VideoExtensions.Contains(extension);

    private static bool AreEquivalentImageExtensions(string original, string detected)
    {
        if (string.Equals(original, detected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (original is ".jpg" or ".jpeg") && (detected is ".jpg" or ".jpeg")
               || (original is ".tif" or ".tiff") && (detected is ".tif" or ".tiff")
               || (original is ".heic" or ".heif") && (detected is ".heic" or ".heif");
    }

    private static byte[] ReadSignature(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[Math.Min(64, (int)Math.Min(stream.Length, 64))];
            _ = stream.Read(buffer, 0, buffer.Length);
            return buffer;
        }
        catch
        {
            return [];
        }
    }

    private static string? DetectBySignature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (bytes.Length >= 6)
        {
            var six = Encoding.ASCII.GetString(bytes[..6]);
            if (six is "GIF87a" or "GIF89a")
            {
                return ".gif";
            }
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return ".bmp";
        }

        if (bytes.Length >= 4 &&
            ((bytes[0] == (byte)'I' && bytes[1] == (byte)'I' && bytes[2] == 0x2A && bytes[3] == 0x00) ||
             (bytes[0] == (byte)'M' && bytes[1] == (byte)'M' && bytes[2] == 0x00 && bytes[3] == 0x2A)))
        {
            return ".tif";
        }

        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes[..4]) == "RIFF" && Encoding.ASCII.GetString(bytes.Slice(8, 4)) == "WEBP")
        {
            return ".webp";
        }

        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes.Slice(4, 4)) == "ftyp")
        {
            var brand = Encoding.ASCII.GetString(bytes.Slice(8, 4)).ToLowerInvariant();
            if (brand is "crx ")
            {
                return ".cr3";
            }

            if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
            {
                return ".heic";
            }

            if (brand is "avif" or "avis")
            {
                return ".avif";
            }

            if (brand.StartsWith("3g", StringComparison.Ordinal))
            {
                return ".3gp";
            }

            if (brand is "qt  ")
            {
                return ".mov";
            }

            return ".mp4";
        }

        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
        {
            return ".mkv";
        }

        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes[..4]) == "RIFF" && Encoding.ASCII.GetString(bytes.Slice(8, 4)) == "AVI ")
        {
            return ".avi";
        }

        return null;
    }
}
