using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TakeoutMediaFixer.Core.Models;

namespace TakeoutMediaFixer.Core.Services;

public sealed class ExifToolSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentQueue<string> _errors = new();
    private int _commandId;
    private bool _disposed;

    public ExifToolSession(string exifToolPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exifToolPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-stay_open");
        startInfo.ArgumentList.Add("True");
        startInfo.ArgumentList.Add("-@");
        startInfo.ArgumentList.Add("-");

        _process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                _errors.Enqueue(eventArgs.Data);
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException("ExifTool başlatılamadı.");
        }

        _process.BeginErrorReadLine();
    }

    public Task<ExifToolCommandResult> WriteAsync(
        string filePath,
        MediaKind kind,
        DateTimeOffset timestamp,
        double? latitude,
        double? longitude,
        double? altitude,
        CancellationToken cancellationToken)
    {
        var arguments = kind == MediaKind.Video
            ? BuildVideoArguments(filePath, timestamp, latitude, longitude, altitude)
            : BuildImageArguments(filePath, timestamp, latitude, longitude, altitude);

        return ExecuteAsync(arguments, cancellationToken);
    }

    private async Task<ExifToolCommandResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            while (_errors.TryDequeue(out _))
            {
            }

            var id = Interlocked.Increment(ref _commandId);
            foreach (var argument in arguments)
            {
                await _process.StandardInput.WriteLineAsync(argument.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            await _process.StandardInput.WriteLineAsync($"-execute{id}".AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            var output = new StringBuilder();
            var readyMarker = $"{{ready{id}}}";
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.Equals(line.Trim(), readyMarker, StringComparison.Ordinal))
                {
                    break;
                }

                output.AppendLine(line);
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            var error = new StringBuilder();
            while (_errors.TryDequeue(out var errorLine))
            {
                error.AppendLine(errorLine);
            }

            var outputText = output.ToString();
            var errorText = error.ToString();
            var hasFatalError = outputText.Contains("Error:", StringComparison.OrdinalIgnoreCase)
                                || errorText.Contains("Error:", StringComparison.OrdinalIgnoreCase)
                                || _process.HasExited;
            var updated = Regex.IsMatch(
                outputText,
                @"(?im)^\s*[1-9]\d*\s+(?:image\s+)?files?\s+updated\b",
                RegexOptions.CultureInvariant);

            return new ExifToolCommandResult(!hasFatalError && updated, outputText, errorText);
        }
        catch (OperationCanceledException)
        {
            TryKill();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IReadOnlyList<string> BuildImageArguments(
        string filePath,
        DateTimeOffset timestamp,
        double? latitude,
        double? longitude,
        double? altitude)
    {
        var local = timestamp.ToString("yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture);
        var exifLocal = timestamp.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
        var arguments = CommonArguments();
        arguments.Add($"-DateTimeOriginal={exifLocal}");
        arguments.Add($"-CreateDate={exifLocal}");
        arguments.Add($"-ModifyDate={exifLocal}");
        arguments.Add($"-XMP:DateTimeOriginal={local}");
        arguments.Add($"-XMP:CreateDate={local}");
        arguments.Add($"-XMP:ModifyDate={local}");
        AddGps(arguments, latitude, longitude, altitude);
        arguments.Add(filePath);
        return arguments;
    }

    private static IReadOnlyList<string> BuildVideoArguments(
        string filePath,
        DateTimeOffset timestamp,
        double? latitude,
        double? longitude,
        double? altitude)
    {
        var localTimestamp = timestamp.ToLocalTime();
        var quickTimeLocal = localTimestamp.ToString("yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture);
        var localWithOffset = localTimestamp.ToString("yyyy:MM:dd HH:mm:sszzz", CultureInfo.InvariantCulture);
        var arguments = CommonArguments();
        arguments.Add("-api");
        arguments.Add("QuickTimeUTC=1");

        // QuickTimeUTC=1, yazılan yerel saat değerini QuickTime standardındaki
        // UTC depolama biçimine dönüştürür. Buraya önceden UTC verilirse saat
        // dilimi iki kez uygulanır.
        arguments.Add($"-QuickTime:CreateDate={quickTimeLocal}");
        arguments.Add($"-QuickTime:ModifyDate={quickTimeLocal}");
        arguments.Add($"-QuickTime:TrackCreateDate={quickTimeLocal}");
        arguments.Add($"-QuickTime:TrackModifyDate={quickTimeLocal}");
        arguments.Add($"-QuickTime:MediaCreateDate={quickTimeLocal}");
        arguments.Add($"-QuickTime:MediaModifyDate={quickTimeLocal}");
        arguments.Add($"-Keys:CreationDate={localWithOffset}");
        arguments.Add($"-XMP:CreateDate={localWithOffset}");
        arguments.Add($"-XMP:ModifyDate={localWithOffset}");
        AddGps(arguments, latitude, longitude, altitude);
        arguments.Add(filePath);
        return arguments;
    }

    private static List<string> CommonArguments() =>
    [
        "-overwrite_original",
        "-P",
        "-charset",
        "filename=UTF8",
        "-api",
        "LargeFileSupport=1"
    ];

    private static void AddGps(
        ICollection<string> arguments,
        double? latitude,
        double? longitude,
        double? altitude)
    {
        if (latitude is null || longitude is null)
        {
            return;
        }

        arguments.Add($"-GPSLatitude={Math.Abs(latitude.Value).ToString("0.########", CultureInfo.InvariantCulture)}");
        arguments.Add($"-GPSLatitudeRef={(latitude.Value < 0 ? "S" : "N")}");
        arguments.Add($"-GPSLongitude={Math.Abs(longitude.Value).ToString("0.########", CultureInfo.InvariantCulture)}");
        arguments.Add($"-GPSLongitudeRef={(longitude.Value < 0 ? "W" : "E")}");

        if (altitude is not null)
        {
            arguments.Add($"-GPSAltitude={Math.Abs(altitude.Value).ToString("0.###", CultureInfo.InvariantCulture)}");
            arguments.Add($"-GPSAltitudeRef={(altitude.Value < 0 ? 1 : 0)}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("-stay_open").ConfigureAwait(false);
                await _process.StandardInput.WriteLineAsync("False").ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch
        {
            TryKill();
        }
        finally
        {
            _process.Dispose();
            _gate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // En iyi çaba.
        }
    }
}
