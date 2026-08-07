using System.Diagnostics;
using System.Text;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public static class ProcessRunner
{
    public static async Task<ExifToolCommandResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ExifToolCommandResult(false, string.Empty, "İşlem başlatılamadı.");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            return new ExifToolCommandResult(process.ExitCode == 0, output, error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryKill(process);
            return new ExifToolCommandResult(false, string.Empty, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // En iyi çaba.
        }
    }
}
