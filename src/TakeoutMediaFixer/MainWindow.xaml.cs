using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using TakeoutMediaFixer.Core.Models;
using TakeoutMediaFixer.Core.Services;

namespace TakeoutMediaFixer;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _outputDirectory;
    private bool _isProcessing;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = !_isProcessing && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_isProcessing || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
        var path = dropped?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path);
        }

        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            await StartProcessingAsync(path);
        }
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "İşlenecek medya klasörünü seçin",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await StartProcessingAsync(dialog.FolderName);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        StatusText.Text = "İptal isteği alındı; devam eden dosya güvenli biçimde tamamlanıyor...";
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { _outputDirectory },
            UseShellExecute = true
        });
    }

    private async Task StartProcessingAsync(string sourceDirectory)
    {
        _isProcessing = true;
        _outputDirectory = null;
        _cancellationTokenSource = new CancellationTokenSource();
        SetProcessingUi(true);
        ResetCounters();
        SourcePathText.Text = sourceDirectory;
        StageText.Text = "Başlatılıyor";
        StatusText.Text = "Kaynak klasör hazırlanıyor...";

        var progress = new Progress<ProcessingProgress>(UpdateProgress);
        var processor = new MediaProcessor();

        try
        {
            var result = await processor.ProcessAsync(sourceDirectory, progress, _cancellationTokenSource.Token);
            _outputDirectory = result.OutputRoot;
            ImagesText.Text = result.ImagesCopied.ToString("N0");
            VideosText.Text = result.VideosCopied.ToString("N0");
            MetadataText.Text = result.MetadataWritten.ToString("N0");
            UnresolvedText.Text = result.Unresolved.ToString("N0");
            ProgressBar.Value = 100;
            PercentageText.Text = "100%";
            StageText.Text = "Tamamlandı";
            StatusText.Text = $"{result.ImagesCopied:N0} fotoğraf ve {result.VideosCopied:N0} video işlendi. Kaynak klasöre dokunulmadı.";
            OpenOutputButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            StageText.Text = "İptal edildi";
            StatusText.Text = "İşlem kullanıcı tarafından iptal edildi. Kaynak dosyalar değiştirilmedi; oluşturulan kısmi çıktı klasörü incelenebilir.";
        }
        catch (Exception ex)
        {
            StageText.Text = "Hata";
            StatusText.Text = ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                "Takeout Media Fixer",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isProcessing = false;
            SetProcessingUi(false);
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void UpdateProgress(ProcessingProgress value)
    {
        StageText.Text = value.Stage;
        StatusText.Text = value.Message;
        ProgressBar.Value = value.Percentage;
        PercentageText.Text = $"{value.Percentage:0}%";
        ImagesText.Text = value.Images.ToString("N0");
        VideosText.Text = value.Videos.ToString("N0");
        MetadataText.Text = value.MetadataWritten.ToString("N0");
        UnresolvedText.Text = value.Unresolved.ToString("N0");
    }

    private void SetProcessingUi(bool processing)
    {
        SelectFolderButton.IsEnabled = !processing;
        CancelButton.IsEnabled = processing;
        DropZone.IsEnabled = !processing;
        DropTitleText.Text = processing ? "Klasör işleniyor" : "Klasörü buraya sürükleyip bırakın";
        if (processing)
        {
            OpenOutputButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetCounters()
    {
        ProgressBar.Value = 0;
        PercentageText.Text = "0%";
        ImagesText.Text = "0";
        VideosText.Text = "0";
        MetadataText.Text = "0";
        UnresolvedText.Text = "0";
        OpenOutputButton.Visibility = Visibility.Collapsed;
    }
}
