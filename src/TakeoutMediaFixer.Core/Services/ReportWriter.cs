using System.Text;
using TakeoutMediaFixer.Core.Models;
using TakeoutMediaFixer.Core.Utilities;

namespace TakeoutMediaFixer.Core.Services;

public static class ReportWriter
{
    public static async Task WriteAsync(ProcessingResult result, CancellationToken cancellationToken)
    {
        var csv = new StringBuilder();
        csv.AppendLine("sep=,");
        csv.AppendLine("Kaynak,Çıktı,Tür,Tarih,Tarih Kaynağı,Hassasiyet,Meta Veri Durumu,Dosya Adı Durumu,Hata");
        foreach (var record in result.Records)
        {
            csv.Append(CsvUtility.Escape(record.SourcePath)).Append(',')
                .Append(CsvUtility.Escape(record.OutputPath)).Append(',')
                .Append(CsvUtility.Escape(record.Kind.ToString())).Append(',')
                .Append(CsvUtility.Escape(record.Timestamp)).Append(',')
                .Append(CsvUtility.Escape(record.TimestampSource)).Append(',')
                .Append(CsvUtility.Escape(record.Precision)).Append(',')
                .Append(CsvUtility.Escape(record.MetadataStatus)).Append(',')
                .Append(CsvUtility.Escape(record.FileNameStatus)).Append(',')
                .Append(CsvUtility.Escape(record.Error)).AppendLine();
        }

        await File.WriteAllTextAsync(
            result.CsvReportPath,
            csv.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken).ConfigureAwait(false);

        var summary = new StringBuilder()
            .AppendLine("TAKEOUT MEDIA FIXER - İŞLEM ÖZETİ")
            .AppendLine(new string('=', 42))
            .AppendLine($"Kaynak klasör: {result.SourceRoot}")
            .AppendLine($"Çıktı klasörü: {result.OutputRoot}")
            .AppendLine($"Yerel saat dilimi: {TimeZoneInfo.Local.Id}")
            .AppendLine($"Toplam taranan dosya: {result.TotalFilesScanned}")
            .AppendLine($"Bulunan JSON: {result.JsonFilesFound}")
            .AppendLine($"Kopyalanan fotoğraf: {result.ImagesCopied}")
            .AppendLine($"Kopyalanan video: {result.VideosCopied}")
            .AppendLine($"Korunan mevcut meta veri: {result.ExistingMetadataPreserved}")
            .AppendLine($"Yazılan meta veri: {result.MetadataWritten}")
            .AppendLine($"Yalnızca dosya sistemi tarihi yazılan: {result.FileSystemDateOnly}")
            .AppendLine($"Tarihi çözülemeyen: {result.Unresolved}")
            .AppendLine($"Düzeltilen uzantı: {result.ExtensionCorrections}")
            .AppendLine($"Dosya adı çakışması: {result.NameCollisions}")
            .AppendLine($"Hata: {result.Errors}")
            .AppendLine($"ExifTool kullanılabildi: {(result.ExifToolAvailable ? "Evet" : "Hayır")}")
            .AppendLine($"Süre: {result.Duration:hh\\:mm\\:ss}")
            .AppendLine()
            .AppendLine("GÜVENLİK")
            .AppendLine("Kaynak klasördeki hiçbir dosya silinmedi, taşınmadı veya değiştirilmedi.")
            .AppendLine("Tüm işlemler yeni çıktı klasöründeki kopyalar üzerinde yapıldı.")
            .AppendLine()
            .AppendLine("NOT")
            .AppendLine("WhatsApp dosya adında yalnızca gün bilgisi varsa saat 12:00 olarak işaretlenir ve raporda belirtilir.")
            .AppendLine("Hasarlı veya yazılamayan videolarda uygulama dosya sistemi tarihini düzeltir; dahili video meta verisi yazılamamış olabilir.");

        await File.WriteAllTextAsync(
            result.SummaryPath,
            summary.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken).ConfigureAwait(false);

        var phoneGuide = new StringBuilder()
            .AppendLine("ANDROID / SAMSUNG TELEFONA KOPYALAMA")
            .AppendLine(new string('=', 42))
            .AppendLine()
            .AppendLine("Tek albüm olarak görünmesi için:")
            .AppendLine("1. Telefonda Dahili depolama\\DCIM altında yeni bir klasör oluşturun (örnek: Aktarılan Fotoğraflar).")
            .AppendLine("2. Tum_Resimler ve Tum_Videolar klasörlerinin KENDİLERİNİ değil, içlerindeki dosyaları bu klasöre kopyalayın.")
            .AppendLine("3. Samsung Galeri dosyaları taradıktan sonra bu klasörü tek albüm olarak gösterir.")
            .AppendLine()
            .AppendLine("Ayrı albümler olarak görünmesi için Tum_Resimler ve Tum_Videolar klasörlerini doğrudan Dahili depolama\\DCIM altına kopyalayın.")
            .AppendLine()
            .AppendLine("WhatsApp veya Screenshots klasörüne kopyalamak yalnızca Galeri albüm adını etkiler; eski WhatsApp sohbetlerini yeniden oluşturmaz.")
            .AppendLine("Telefonun eski içeriğini silmeden önce farklı yıllardan birkaç fotoğrafı ve videoyu açarak tarih sıralamasını doğrulayın.");

        await File.WriteAllTextAsync(
            result.PhoneGuidePath,
            phoneGuide.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            cancellationToken).ConfigureAwait(false);
    }
}
