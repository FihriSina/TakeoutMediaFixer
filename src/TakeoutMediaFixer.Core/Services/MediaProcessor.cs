using System.Diagnostics;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public sealed class MediaProcessor
{
    private readonly FileTypeDetector _fileTypeDetector = new();
    private readonly TakeoutJsonParser _jsonParser = new();
    private readonly SafeFileCopier _fileCopier = new();

    public async Task<ProcessingResult> ProcessAsync(
        string sourceRoot,
        IProgress<ProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        sourceRoot = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException("Seçilen kaynak klasör bulunamadı.");
        }

        var outputRoot = CreateOutputRoot(sourceRoot);
        var imageOutputDirectory = Path.Combine(outputRoot, "Tum_Resimler");
        var videoOutputDirectory = Path.Combine(outputRoot, "Tum_Videolar");
        Directory.CreateDirectory(imageOutputDirectory);
        Directory.CreateDirectory(videoOutputDirectory);

        var result = new ProcessingResult
        {
            SourceRoot = sourceRoot,
            OutputRoot = outputRoot,
            ImageOutputDirectory = imageOutputDirectory,
            VideoOutputDirectory = videoOutputDirectory,
            CsvReportPath = Path.Combine(outputRoot, "islem_raporu.csv"),
            SummaryPath = Path.Combine(outputRoot, "ozet.txt"),
            PhoneGuidePath = Path.Combine(outputRoot, "telefona_kopyalama_rehberi.txt")
        };

        try
        {
            Report(progress, "Tarama", "Klasör taranıyor...", 0, 1, result);
            var allFiles = EnumerateFiles(sourceRoot)
                .Where(path => !IsInsideDirectory(path, outputRoot))
                .ToList();
            result.TotalFilesScanned = allFiles.Count;

            var jsonFiles = allFiles
                .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                .ToList();
            result.JsonFilesFound = jsonFiles.Count;

            var metadataIndex = new MetadataIndex();
            for (var index = 0; index < jsonFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var metadata = _jsonParser.TryParse(jsonFiles[index]);
                if (metadata is not null)
                {
                    metadataIndex.Add(metadata);
                }

                Report(progress, "JSON", "Google Takeout meta verileri okunuyor...", index + 1, Math.Max(jsonFiles.Count, 1), result);
            }

            var candidates = new List<MediaCandidate>();
            for (var index = 0; index < allFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filePath = allFiles[index];
                if (string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase)
                    || IsToolArtifact(filePath))
                {
                    continue;
                }

                var (kind, extension) = _fileTypeDetector.Detect(filePath);
                if (kind == MediaKind.Unknown)
                {
                    continue;
                }

                var resolved = metadataIndex.Resolve(filePath);
                candidates.Add(new MediaCandidate(
                    filePath,
                    kind,
                    extension,
                    resolved.Timestamp,
                    resolved.Latitude,
                    resolved.Longitude,
                    resolved.Altitude));

                Report(progress, "Tarama", $"Medya dosyaları belirleniyor: {Path.GetFileName(filePath)}", index + 1, Math.Max(allFiles.Count, 1), result);
            }

            EnsureDiskSpace(outputRoot, candidates);

            var workItems = new List<WorkItem>(candidates.Count);
            for (var index = 0; index < candidates.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = candidates[index];
                try
                {
                    var copyResult = _fileCopier.Copy(candidate, imageOutputDirectory, videoOutputDirectory);
                    if (candidate.Kind == MediaKind.Image)
                    {
                        result.ImagesCopied++;
                    }
                    else
                    {
                        result.VideosCopied++;
                    }

                    if (copyResult.ExtensionCorrected)
                    {
                        result.ExtensionCorrections++;
                    }

                    if (copyResult.NameCollision)
                    {
                        result.NameCollisions++;
                    }

                    var record = new ProcessingRecord
                    {
                        SourcePath = candidate.SourcePath,
                        OutputPath = copyResult.DestinationPath,
                        Kind = candidate.Kind,
                        FileNameStatus = BuildFileNameStatus(copyResult)
                    };
                    result.Records.Add(record);
                    workItems.Add(new WorkItem(candidate, copyResult.DestinationPath, record));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Errors++;
                    result.Records.Add(new ProcessingRecord
                    {
                        SourcePath = candidate.SourcePath,
                        OutputPath = string.Empty,
                        Kind = candidate.Kind,
                        MetadataStatus = "Kopyalanamadı",
                        Error = ex.Message
                    });
                }

                Report(progress, "Kopyalama", $"Dosyalar güvenli çıktı klasörüne kopyalanıyor...", index + 1, Math.Max(candidates.Count, 1), result);
            }

            var exifToolPath = ExifToolLocator.Find();
            result.ExifToolAvailable = exifToolPath is not null;
            var existingMetadata = new Dictionary<string, ExistingMetadata>(StringComparer.OrdinalIgnoreCase);
            if (exifToolPath is not null && workItems.Count > 0)
            {
                Report(progress, "Meta veri", "Mevcut fotoğraf ve video tarihleri kontrol ediliyor...", 0, workItems.Count, result);
                try
                {
                    var reader = new ExifToolMetadataReader(exifToolPath);
                    existingMetadata = await reader.ReadAsync(outputRoot, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.ExifToolAvailable = false;
                    exifToolPath = null;
                }
            }

            ExifToolSession? session = null;
            try
            {
                if (exifToolPath is not null)
                {
                    try
                    {
                        session = new ExifToolSession(exifToolPath);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        result.ExifToolAvailable = false;
                        session = null;
                    }
                }

                for (var index = 0; index < workItems.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = workItems[index];
                    var outputPath = Path.GetFullPath(item.OutputPath);

                    try
                    {
                        if (existingMetadata.TryGetValue(outputPath, out var existing) && existing.Timestamp is not null)
                        {
                            item.Record.Timestamp = existing.Timestamp.Value.ToString("O");
                            item.Record.TimestampSource = $"Mevcut dosya meta verisi: {existing.RawTimestampSource}";
                            item.Record.Precision = TimestampPrecision.Exact.ToString();
                            item.Record.MetadataStatus = "Mevcut meta veri korundu";
                            result.ExistingMetadataPreserved++;

                            AddRecordError(result, item.Record, TryApplyFileSystemDates(outputPath, existing.Timestamp.Value));
                        }
                        else if (item.Candidate.CandidateTimestamp is not null)
                        {
                            var resolution = item.Candidate.CandidateTimestamp;
                            item.Record.Timestamp = resolution.Timestamp.ToString("O");
                            item.Record.TimestampSource = resolution.Source;
                            item.Record.Precision = resolution.Precision.ToString();

                            if (session is not null)
                            {
                                try
                                {
                                    var writeResult = await session.WriteAsync(
                                        outputPath,
                                        item.Candidate.Kind,
                                        resolution.Timestamp,
                                        item.Candidate.Latitude,
                                        item.Candidate.Longitude,
                                        item.Candidate.Altitude,
                                        cancellationToken).ConfigureAwait(false);

                                    if (writeResult.Success)
                                    {
                                        item.Record.MetadataStatus = "Meta veri yazıldı";
                                        result.MetadataWritten++;
                                    }
                                    else
                                    {
                                        item.Record.MetadataStatus = "Yalnızca dosya sistemi tarihi yazıldı";
                                        AddRecordError(
                                            result,
                                            item.Record,
                                            CollapseError(writeResult.StandardError, writeResult.StandardOutput)
                                            ?? "ExifTool dosyayı güncellemedi.");
                                        result.FileSystemDateOnly++;
                                    }
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    item.Record.MetadataStatus = "Yalnızca dosya sistemi tarihi yazıldı (ExifTool hatası)";
                                    AddRecordError(result, item.Record, ex.Message);
                                    result.FileSystemDateOnly++;
                                    result.ExifToolAvailable = false;
                                    await DisposeSessionQuietlyAsync(session).ConfigureAwait(false);
                                    session = null;
                                }
                            }
                            else
                            {
                                item.Record.MetadataStatus = "Yalnızca dosya sistemi tarihi yazıldı (ExifTool bulunamadı)";
                                result.FileSystemDateOnly++;
                            }

                            // ExifTool bazı dosyaları geçici dosya üzerinden yeniden oluşturabilir.
                            // Bu nedenle Windows dosya tarihleri meta veri yazımından sonra uygulanır.
                            AddRecordError(result, item.Record, TryApplyFileSystemDates(outputPath, resolution.Timestamp));
                        }
                        else
                        {
                            item.Record.MetadataStatus = "Güvenilir tarih bulunamadı; meta veri değiştirilmedi";
                            result.Unresolved++;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        item.Record.MetadataStatus = "İşleme hatası; kopya korundu";
                        AddRecordError(result, item.Record, ex.Message);
                    }

                    Report(progress, "Meta veri", $"Tarihler işleniyor: {Path.GetFileName(outputPath)}", index + 1, Math.Max(workItems.Count, 1), result);
                }
            }
            finally
            {
                if (session is not null)
                {
                    await DisposeSessionQuietlyAsync(session).ConfigureAwait(false);
                }
            }

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            await ReportWriter.WriteAsync(result, cancellationToken).ConfigureAwait(false);
            Report(progress, "Tamamlandı", "İşlem tamamlandı. Kaynak dosyalara dokunulmadı.", 1, 1, result);
            return result;
        }
        catch
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            try
            {
                await ReportWriter.WriteAsync(result, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Asıl hatayı gölgelememek için rapor hatası yutulur.
            }

            throw;
        }
    }

    private static bool IsInsideDirectory(string filePath, string directoryPath)
    {
        var fullFilePath = Path.GetFullPath(filePath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsToolArtifact(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return name.EndsWith("_original", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith("_exiftool_tmp", StringComparison.OrdinalIgnoreCase)
               || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
               || name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(root, "*", options);
    }

    private static string CreateOutputRoot(string sourceRoot)
    {
        var preferredParent = Directory.GetParent(sourceRoot)?.FullName ?? sourceRoot;
        var sourceName = new DirectoryInfo(sourceRoot).Name;
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "Medya";
        }

        var baseName = $"{sourceName}_Duzeltilmis_{DateTime.Now:yyyyMMdd_HHmmss}";
        var documentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string?[] parentCandidates =
        {
            preferredParent,
            string.IsNullOrWhiteSpace(documentsRoot)
                ? null
                : Path.Combine(documentsRoot, "Takeout Media Fixer")
        };

        Exception? lastError = null;
        foreach (var parent in parentCandidates.OfType<string>().Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(parent);
                var candidate = Path.Combine(parent, baseName);
                var counter = 0;
                while (Directory.Exists(candidate) || File.Exists(candidate))
                {
                    counter++;
                    candidate = Path.Combine(parent, $"{baseName}_{counter}");
                }

                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }
        }

        throw new IOException("Çıktı klasörü oluşturulamadı. Kaynak klasörün yanında ve Belgeler klasöründe yazma izni bulunamadı.", lastError);
    }

    private static void EnsureDiskSpace(string outputRoot, IEnumerable<MediaCandidate> candidates)
    {
        long required = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                required += new FileInfo(candidate.SourcePath).Length;
            }
            catch
            {
                // Boyutu okunamayan dosya kopyalama aşamasında ele alınır.
            }
        }

        var root = Path.GetPathRoot(outputRoot);
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        long availableFreeSpace;
        try
        {
            availableFreeSpace = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or DriveNotFoundException or UnauthorizedAccessException or IOException)
        {
            // Ağ sürücüsü veya özel yol; kopyalama sırasında işletim sistemi denetler.
            return;
        }

        var safetyMargin = Math.Max(256L * 1024 * 1024, required / 20);
        if (availableFreeSpace < required + safetyMargin)
        {
            throw new IOException("Çıktı için yeterli boş disk alanı yok. Kaynak medya boyutuna ek olarak en az %5 güvenlik payı gerekir.");
        }
    }

    private static string? TryApplyFileSystemDates(string filePath, DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime().LocalDateTime;
        var errors = new List<string>();
        try
        {
            File.SetCreationTime(filePath, local);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            errors.Add($"Oluşturma tarihi yazılamadı: {ex.Message}");
        }

        try
        {
            File.SetLastWriteTime(filePath, local);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            errors.Add($"Değiştirme tarihi yazılamadı: {ex.Message}");
        }

        return errors.Count == 0 ? null : string.Join(" | ", errors);
    }

    private static void AddRecordError(ProcessingResult result, ProcessingRecord record, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(record.Error))
        {
            record.Error = error;
            result.Errors++;
            return;
        }

        if (!record.Error.Contains(error, StringComparison.Ordinal))
        {
            record.Error += " | " + error;
        }
    }

    private static async Task DisposeSessionQuietlyAsync(ExifToolSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // İşlem sonucu ve kopyalar korunur.
        }
    }

    private static string BuildFileNameStatus(CopyResult copyResult)
    {
        if (copyResult.ExtensionCorrected && copyResult.NameCollision)
        {
            return "Uzantı düzeltildi ve çakışma nedeniyle numara eklendi";
        }

        if (copyResult.ExtensionCorrected)
        {
            return "Gerçek dosya türüne göre uzantı düzeltildi";
        }

        return copyResult.NameCollision
            ? "Aynı ad bulundu; güvenli numara eklendi"
            : "Korundu";
    }

    private static string? CollapseError(string standardError, string standardOutput)
    {
        var combined = string.Join(" | ", new[] { standardError, standardOutput }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => string.Join(" ", value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))));

        if (string.IsNullOrWhiteSpace(combined))
        {
            return null;
        }

        return combined.Length <= 1000 ? combined : combined[..1000] + "...";
    }

    private static void Report(
        IProgress<ProcessingProgress>? progress,
        string stage,
        string message,
        int current,
        int total,
        ProcessingResult result)
    {
        progress?.Report(new ProcessingProgress(
            stage,
            message,
            current,
            total,
            result.ImagesCopied,
            result.VideosCopied,
            result.MetadataWritten,
            result.Unresolved));
    }

    private sealed record WorkItem(
        MediaCandidate Candidate,
        string OutputPath,
        ProcessingRecord Record);
}
