using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AlphaPlay.Services;

namespace AlphaPlay
{
    public partial class DownloadUpdateWindow : Window
    {
        private readonly GitHubUpdateInfo _updateInfo;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _downloadFinished;

        public DownloadUpdateWindow(GitHubUpdateInfo updateInfo)
        {
            _updateInfo = updateInfo;
            InitializeComponent();
            Loaded += DownloadUpdateWindow_Loaded;
            Closing += DownloadUpdateWindow_Closing;
        }

        private async void DownloadUpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtFileName.Text = _updateInfo.AssetName;
            await StartDownloadAsync();
        }

        private async Task StartDownloadAsync()
        {
            try
            {
                Progress<(long downloadedBytes, long totalBytes)> progress = new(report =>
                {
                    long total = report.totalBytes > 0 ? report.totalBytes : _updateInfo.AssetSizeBytes;
                    double percent = total > 0 ? report.downloadedBytes * 100d / total : 0;

                    ProgressDownload.Value = Math.Max(0, Math.Min(100, percent));
                    TxtProgress.Text = $"{GitHubUpdateService.FormatBytes(report.downloadedBytes)} de {GitHubUpdateService.FormatBytes(total)}";
                    TxtStatus.Text = $"Baixando... {percent:0}%";
                });

                string installerPath = await GitHubUpdateService.DownloadInstallerAsync(
                    _updateInfo,
                    progress,
                    _cancellationTokenSource.Token);

                _downloadFinished = true;
                ProgressDownload.Value = 100;
                TxtProgress.Text = "Download concluído.";
                TxtStatus.Text = "Iniciando instalador e fechando o AlphaPlay...";
                BtnCancel.IsEnabled = false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });

                await Task.Delay(700);
                System.Windows.Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                TxtStatus.Text = "Download cancelado.";
                BtnCancel.Content = "Fechar";
            }
            catch (Exception ex)
            {
                TxtProgress.Text = "Falha no download.";
                TxtStatus.Text = ex.Message;
                BtnCancel.Content = "Fechar";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_downloadFinished && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
            else
            {
                Close();
            }
        }

        private void DownloadUpdateWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_downloadFinished && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }
        }
    }
}
