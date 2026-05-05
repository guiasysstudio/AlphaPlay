using System;
using System.Threading.Tasks;
using System.Windows;
using AlphaPlay.Services;

namespace AlphaPlay
{
    public partial class UpdateCheckWindow : Window
    {
        private GitHubUpdateInfo? _updateInfo;

        public UpdateCheckWindow()
        {
            InitializeComponent();
            Loaded += UpdateCheckWindow_Loaded;
        }

        private async void UpdateCheckWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync();
        }

        private async Task CheckUpdatesAsync()
        {
            SetLoading(true);
            TxtCurrentVersion.Text = GitHubUpdateService.GetCurrentVersion();
            TxtLatestVersion.Text = "-";
            TxtInstaller.Text = "-";
            TxtMessage.Text = "Consultando releases do GitHub...";
            BtnDownload.Visibility = Visibility.Collapsed;

            _updateInfo = await GitHubUpdateService.CheckForUpdateAsync();

            SetLoading(false);
            TxtCurrentVersion.Text = string.IsNullOrWhiteSpace(_updateInfo.CurrentVersion) ? "-" : _updateInfo.CurrentVersion;
            TxtLatestVersion.Text = string.IsNullOrWhiteSpace(_updateInfo.LatestVersion) ? "-" : _updateInfo.LatestVersion;
            TxtInstaller.Text = string.IsNullOrWhiteSpace(_updateInfo.AssetName)
                ? "-"
                : $"{_updateInfo.AssetName} ({GitHubUpdateService.FormatBytes(_updateInfo.AssetSizeBytes)})";
            TxtStatus.Text = _updateInfo.Message;
            TxtMessage.Text = _updateInfo.Success
                ? _updateInfo.HasUpdate
                    ? "Clique em Baixar atualização para baixar o instalador dentro do AlphaPlay. Depois do download, o instalador será aberto e o AlphaPlay será fechado."
                    : "Nenhuma ação necessária. O botão de atualização só aparece quando existe uma versão mais nova."
                : _updateInfo.Message;

            BtnDownload.Visibility = _updateInfo.Success && _updateInfo.HasUpdate
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SetLoading(bool loading)
        {
            TxtStatus.Text = loading ? "Procurando atualização..." : TxtStatus.Text;
            BtnRetry.IsEnabled = !loading;
            BtnDownload.IsEnabled = !loading;
        }

        private async void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            await CheckUpdatesAsync();
        }

        private void BtnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInfo == null || !_updateInfo.Success || !_updateInfo.HasUpdate)
            {
                return;
            }

            DownloadUpdateWindow window = new(_updateInfo)
            {
                Owner = this.Owner ?? this
            };
            window.ShowDialog();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
