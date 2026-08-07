using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public sealed class SafeFileCopier
{
    public CopyResult Copy(MediaCandidate candidate, string imageDirectory, string videoDirectory)
    {
        var destinationDirectory = candidate.Kind == MediaKind.Image ? imageDirectory : videoDirectory;
        Directory.CreateDirectory(destinationDirectory);

        var originalExtension = Path.GetExtension(candidate.SourcePath);
        var baseName = Path.GetFileNameWithoutExtension(candidate.SourcePath);
        var chosenExtension = string.IsNullOrWhiteSpace(candidate.SuggestedExtension)
            ? originalExtension
            : candidate.SuggestedExtension;

        var extensionCorrected = !string.Equals(originalExtension, chosenExtension, StringComparison.OrdinalIgnoreCase);
        var destinationPath = Path.Combine(destinationDirectory, baseName + chosenExtension);
        var nameCollision = false;

        var counter = 0;
        while (File.Exists(destinationPath))
        {
            counter++;
            destinationPath = Path.Combine(destinationDirectory, $"{baseName}_{counter}{chosenExtension}");
            nameCollision = true;
        }

        File.Copy(candidate.SourcePath, destinationPath, overwrite: false);

        // Kaynak dosya salt okunur ise kopya üzerinde meta veri yazılabilmesi için
        // yalnızca çıktı kopyasının ReadOnly bayrağını kaldır.
        try
        {
            var attributes = File.GetAttributes(destinationPath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(destinationPath, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Meta veri aşaması ayrıntılı hatayı raporlayacaktır.
        }

        return new CopyResult(destinationPath, nameCollision, extensionCorrected);
    }
}
