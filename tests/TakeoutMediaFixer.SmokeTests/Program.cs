using TakeoutMediaFixer.Core.Models;
using TakeoutMediaFixer.Core.Services;
using TakeoutMediaFixer.Core.Utilities;

var failures = new List<string>();

void Assert(bool condition, string message)
{
    if (!condition)
    {
        failures.Add(message);
    }
}

var camera = DatePatternParser.TryResolve("IMG_20240102_030405.jpg");
Assert(camera is not null, "Kamera biçimi çözülemedi.");
Assert(camera?.Timestamp.Year == 2024 && camera.Timestamp.Month == 1 && camera.Timestamp.Day == 2, "Kamera tarihi yanlış.");
Assert(camera?.Timestamp.Hour == 3 && camera.Timestamp.Minute == 4 && camera.Timestamp.Second == 5, "Kamera saati yanlış.");

var screenshot = DatePatternParser.TryResolve("Screenshot_2024-01-02-03-04-05-400_com.example.app.png");
Assert(screenshot is not null, "Ekran görüntüsü biçimi çözülemedi.");
Assert(screenshot?.Timestamp.Millisecond == 400, "Ekran görüntüsü milisaniyesi yanlış.");

var compactScreenshot = DatePatternParser.TryResolve("Screenshot_20240102_030405_Gallery.jpg");
Assert(compactScreenshot is not null && compactScreenshot.Timestamp.Hour == 3, "Kompakt ekran görüntüsü biçimi çözülemedi.");

var generic = DatePatternParser.TryResolve("20240102_030405.mp4");
Assert(generic is not null && generic.Timestamp.Year == 2024 && generic.Timestamp.Hour == 3, "Genel tarih-saat biçimi çözülemedi.");

var facebook = DatePatternParser.TryResolve("FB_IMG_1609459200123.jpg");
Assert(facebook is not null, "Facebook epoch biçimi çözülemedi.");
Assert(facebook?.Timestamp.ToUniversalTime().ToUnixTimeMilliseconds() == 1609459200123, "Facebook epoch dönüşümü yanlış.");

var whatsapp = DatePatternParser.TryResolve("IMG-20240102-WA0001.jpg");
Assert(whatsapp is not null, "WhatsApp biçimi çözülemedi.");
Assert(whatsapp?.Precision == TimestampPrecision.DateOnly, "WhatsApp hassasiyeti yanlış.");
Assert(whatsapp?.Timestamp.Hour == 12, "WhatsApp tarih-only saati 12:00 olmalı.");

var photobooth = DatePatternParser.TryResolve("SampleApp_Photobooth_2024-1-2--03-04-05.png");
Assert(photobooth is not null && photobooth.Timestamp.Hour == 3 && photobooth.Timestamp.Minute == 4, "Photobooth biçimi çözülemedi.");

var tempDirectory = Path.Combine(Path.GetTempPath(), "TakeoutMediaFixer-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
try
{
    var mislabeled = Path.Combine(tempDirectory, "photo.png");
    await File.WriteAllBytesAsync(mislabeled, [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
    var detector = new FileTypeDetector();
    var detected = detector.Detect(mislabeled);
    Assert(detected.Kind == MediaKind.Image && detected.SuggestedExtension == ".jpg", "Hatalı JPEG uzantısı belirlenemedi.");

    var rawPath = Path.Combine(tempDirectory, "camera.cr2");
    await File.WriteAllBytesAsync(rawPath, [(byte)'I', (byte)'I', 0x2A, 0x00, 0x10, 0x00]);
    var rawDetected = detector.Detect(rawPath);
    Assert(rawDetected.Kind == MediaKind.Image && rawDetected.SuggestedExtension == ".cr2", "RAW uzantısı yanlış değiştirildi.");

    var jsonPath = Path.Combine(tempDirectory, "IMG_20240102_030405.jpg.supplemental-metadata.json");
    await File.WriteAllTextAsync(jsonPath,
        """
        {
          "title": "IMG_20240102_030405.jpg",
          "photoTakenTime": { "timestamp": "1746970322" },
          "geoDataExif": { "latitude": 10.0, "longitude": 20.0, "altitude": 30.0 }
        }
        """);

    var parsed = new TakeoutJsonParser().TryParse(jsonPath);
    Assert(parsed is not null, "Takeout JSON okunamadı.");
    Assert(parsed?.Title == "IMG_20240102_030405.jpg", "Takeout JSON başlığı yanlış.");
    Assert(parsed?.Latitude == 10.0 && parsed.Longitude == 20.0, "Takeout GPS bilgisi yanlış.");

    var truncatedJsonPath = Path.Combine(tempDirectory, "very-long-name.jpg.supplemental-metadat.json");
    await File.WriteAllTextAsync(truncatedJsonPath, "{}");
    var truncated = new TakeoutJsonParser().TryParse(truncatedJsonPath);
    Assert(truncated?.FileNameFromJson == "very-long-name.jpg", "Kesilmiş supplemental metadata adı temizlenemedi.");
}
finally
{
    Directory.Delete(tempDirectory, recursive: true);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke test failures:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine("- " + failure);
    }

    Environment.ExitCode = 1;
}
else
{
    Console.WriteLine("All smoke tests passed.");
}
