using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AlphaPlay.Models;
using AlphaPlay.Services;

namespace AlphaPlay
{
    public partial class RemoteControlWindow : Window
    {
        private const int DefaultRemotePort = 5000;
        private const int MinRemotePort = 1024;
        private const int MaxRemotePort = 65535;

        private readonly AppSettings _settings;
        private string _currentLink = string.Empty;
        private string _localIp = string.Empty;
        private bool _isUpdatingToggle;

        public RemoteControlWindow()
        {
            InitializeComponent();
            _settings = SettingsService.LoadSettings();
            TxtPort.Text = _settings.RemoteControlPort.ToString();
            EnsurePinExists();
            UpdateSecurityState();
            RefreshLocalIp();
            SetToggleWithoutEvent(RemoteControlManager.IsRunning || _settings.RemoteControlEnabled);
            UpdateRemoteState(RemoteControlManager.IsRunning, RemoteControlManager.IsRunning ? "Ativo" : "Desativado");
            RefreshDiagnosticSummary();
            Loaded += RemoteControlWindow_Loaded;
        }

        private async void RemoteControlWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_settings.RemoteControlEnabled && !RemoteControlManager.IsRunning)
            {
                await StartRemoteServerAsync(saveEnabled: true);
                return;
            }

            SetToggleWithoutEvent(RemoteControlManager.IsRunning);
            UpdateRemoteState(RemoteControlManager.IsRunning, RemoteControlManager.IsRunning ? "Ativo" : "Desativado");
        }

        private async void ToggleRemoteControl_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingToggle)
            {
                return;
            }

            bool shouldEnable = ToggleRemoteControl.IsChecked == true;

            if (shouldEnable)
            {
                await StartRemoteServerAsync(saveEnabled: true);
                return;
            }

            await StopRemoteServerAsync(saveDisabled: true);
        }

        private async System.Threading.Tasks.Task StartRemoteServerAsync(bool saveEnabled)
        {
            if (!TrySavePort(showMessage: true))
            {
                SetToggleWithoutEvent(false);
                UpdateRemoteState(false, "Desativado");
                return;
            }

            RefreshLocalIp();
            int port = GetPortOrDefault();

            try
            {
                SetBusy(true, "Iniciando servidor...");
                await RemoteControlManager.StartAsync(port);

                if (saveEnabled)
                {
                    _settings.RemoteControlEnabled = true;
                    _settings.RemoteControlPort = port;
                    SettingsService.SaveSettings(_settings);
                }

                SetToggleWithoutEvent(true);
                UpdateRemoteState(true, "Ativo");
            }
            catch (Exception ex)
            {
                _settings.RemoteControlEnabled = false;
                SettingsService.SaveSettings(_settings);

                SetToggleWithoutEvent(false);
                UpdateRemoteState(false, "Falha ao iniciar");
                System.Windows.MessageBox.Show(
                    $"Não foi possível iniciar o controle remoto na porta {port}.\n\n" +
                    "Essa porta pode estar sendo usada por outro programa, ou o firewall do Windows pode estar bloqueando o acesso.\n\n" +
                    $"Detalhes: {ex.Message}",
                    "AlphaPlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task StopRemoteServerAsync(bool saveDisabled)
        {
            try
            {
                SetBusy(true, "Desativando servidor...");
                await RemoteControlManager.StopAsync();

                if (saveDisabled)
                {
                    _settings.RemoteControlEnabled = false;
                    SettingsService.SaveSettings(_settings);
                }
            }
            finally
            {
                SetBusy(false);
                SetToggleWithoutEvent(false);
                UpdateRemoteState(false, "Desativado");
            }
        }

        private void UpdateRemoteState(bool isEnabled, string statusText)
        {
            int port = GetPortOrDefault();
            bool hasIp = !string.IsNullOrWhiteSpace(_localIp);
            bool serverIsRunning = isEnabled && RemoteControlManager.IsRunning;

            TxtIp.Text = hasIp ? _localIp : "Não encontrado";
            TxtPort.Text = port.ToString();

            if (serverIsRunning)
            {
                TxtRemoteTitle.Text = "Controle remoto: ON";
                TxtStatus.Text = hasIp ? statusText : "Ativo, mas sem IP local encontrado";

                if (hasIp)
                {
                    _currentLink = $"http://{_localIp}:{port}";
                    TxtLink.Text = _currentLink;
                    BtnCopyLink.IsEnabled = true;
                }
                else
                {
                    _currentLink = string.Empty;
                    TxtLink.Text = "Servidor ativo, mas o IP local não foi encontrado";
                    BtnCopyLink.IsEnabled = false;
                }

                BtnShowQrCode.IsEnabled = hasIp;
                BtnSavePort.IsEnabled = false;
                TxtPort.IsEnabled = false;
                RefreshDiagnosticSummary();
                return;
            }

            _currentLink = string.Empty;
            TxtRemoteTitle.Text = "Controle remoto: OFF";
            TxtStatus.Text = statusText;
            TxtLink.Text = "Controle remoto desativado";
            BtnCopyLink.IsEnabled = false;
            BtnShowQrCode.IsEnabled = false;
            BtnSavePort.IsEnabled = true;
            TxtPort.IsEnabled = true;

            RefreshDiagnosticSummary();
        }

        private void SetBusy(bool isBusy, string? statusText = null)
        {
            ToggleRemoteControl.IsEnabled = !isBusy;
            BtnRefreshIp.IsEnabled = !isBusy;
            BtnTestServer.IsEnabled = !isBusy;
            BtnCheckPort.IsEnabled = !isBusy;
            BtnCopyDiagnostics.IsEnabled = !isBusy;
            BtnFirewallHelp.IsEnabled = !isBusy;
            ChkRequirePin.IsEnabled = !isBusy;
            BtnGeneratePin.IsEnabled = !isBusy;
            BtnClose.IsEnabled = !isBusy;

            if (!string.IsNullOrWhiteSpace(statusText))
            {
                TxtStatus.Text = statusText;
            }
        }

        private void SetToggleWithoutEvent(bool isChecked)
        {
            _isUpdatingToggle = true;
            ToggleRemoteControl.IsChecked = isChecked;
            _isUpdatingToggle = false;
        }

        private void RefreshLocalIp()
        {
            _localIp = NetworkInfoService.GetLocalIPv4Address();
        }

        private int GetPortOrDefault()
        {
            if (int.TryParse(TxtPort.Text.Trim(), out int port) && port >= MinRemotePort && port <= MaxRemotePort)
            {
                return port;
            }

            return _settings.RemoteControlPort >= MinRemotePort && _settings.RemoteControlPort <= MaxRemotePort
                ? _settings.RemoteControlPort
                : DefaultRemotePort;
        }

        private bool TrySavePort(bool showMessage)
        {
            string text = TxtPort.Text.Trim();

            if (!int.TryParse(text, out int port) || port < MinRemotePort || port > MaxRemotePort)
            {
                if (showMessage)
                {
                    System.Windows.MessageBox.Show($"Digite uma porta válida entre {MinRemotePort} e {MaxRemotePort}.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                TxtPort.Text = _settings.RemoteControlPort.ToString();
                TxtPort.Focus();
                TxtPort.SelectAll();
                return false;
            }

            _settings.RemoteControlPort = port;
            SettingsService.SaveSettings(_settings);
            return true;
        }

        private void EnsurePinExists()
        {
            if (string.IsNullOrWhiteSpace(_settings.RemoteControlPin) ||
                _settings.RemoteControlPin.Length != 4 ||
                !IsOnlyDigits(_settings.RemoteControlPin))
            {
                _settings.RemoteControlPin = SettingsService.GenerateRemotePin();
                SettingsService.SaveSettings(_settings);
            }
        }

        private void UpdateSecurityState()
        {
            EnsurePinExists();
            ChkRequirePin.IsChecked = _settings.RemoteControlRequirePin;
            TxtPin.Text = _settings.RemoteControlRequirePin ? _settings.RemoteControlPin : "----";
            TxtPinStatus.Text = _settings.RemoteControlRequirePin ? "PIN exigido no celular" : "PIN não exigido";
            BtnGeneratePin.IsEnabled = true;
        }

        private void ChkRequirePin_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            EnsurePinExists();
            _settings.RemoteControlRequirePin = ChkRequirePin.IsChecked == true;
            SettingsService.SaveSettings(_settings);
            UpdateSecurityState();
        }

        private void BtnGeneratePin_Click(object sender, RoutedEventArgs e)
        {
            _settings.RemoteControlPin = SettingsService.GenerateRemotePin();
            SettingsService.SaveSettings(_settings);
            UpdateSecurityState();
            System.Windows.MessageBox.Show($"Novo PIN gerado: {_settings.RemoteControlPin}", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSavePort_Click(object sender, RoutedEventArgs e)
        {
            if (TrySavePort(showMessage: true))
            {
                _settings.RemoteControlEnabled = RemoteControlManager.IsRunning;
                SettingsService.SaveSettings(_settings);
                UpdateRemoteState(RemoteControlManager.IsRunning, RemoteControlManager.IsRunning ? "Ativo" : "Desativado");
                System.Windows.MessageBox.Show("Configuração salva com sucesso.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnRefreshIp_Click(object sender, RoutedEventArgs e)
        {
            RefreshLocalIp();
            UpdateRemoteState(RemoteControlManager.IsRunning, RemoteControlManager.IsRunning ? "Ativo" : "Desativado");
        }

        private void TxtPort_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsOnlyDigits(e.Text);
        }

        private void TxtPort_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
            {
                string text = e.DataObject.GetData(System.Windows.DataFormats.Text)?.ToString() ?? string.Empty;

                if (IsOnlyDigits(text))
                {
                    return;
                }
            }

            e.CancelCommand();
        }

        private static bool IsOnlyDigits(string text)
        {
            foreach (char character in text)
            {
                if (!char.IsDigit(character))
                {
                    return false;
                }
            }

            return true;
        }

        private void BtnCopyLink_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentLink))
            {
                System.Windows.MessageBox.Show("O link ainda não está disponível.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            System.Windows.Clipboard.SetText(_currentLink);
            System.Windows.MessageBox.Show("Link copiado para a área de transferência.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnShowQrCode_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentLink))
            {
                System.Windows.MessageBox.Show("O link ainda não está disponível. Ative o controle remoto primeiro.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            QrCodeWindow qrCodeWindow = new(_currentLink)
            {
                Owner = this
            };
            qrCodeWindow.ShowDialog();
        }


        private async void BtnTestServer_Click(object sender, RoutedEventArgs e)
        {
            await TestServerAsync();
        }

        private void BtnCheckPort_Click(object sender, RoutedEventArgs e)
        {
            CheckPortStatus(showMessage: true);
        }

        private void BtnCopyDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            string diagnostics = BuildDiagnosticText(includeTips: true);
            System.Windows.Clipboard.SetText(diagnostics);
            System.Windows.MessageBox.Show("Informações de diagnóstico copiadas para a área de transferência.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnFirewallHelp_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "Se o link abre no computador, mas não abre no celular, normalmente o problema é rede ou firewall.\n\n" +
                "Verifique:\n" +
                "1. Celular e computador precisam estar na mesma rede.\n" +
                "2. Se o computador está no cabo e o celular no Wi-Fi, os dois precisam estar no mesmo roteador.\n" +
                "3. Evite Wi-Fi de convidados, pois ele pode isolar os aparelhos.\n" +
                "4. No Firewall do Windows, permita o AlphaPlay em redes privadas.\n" +
                "5. Se a porta estiver ocupada, escolha outra porta, por exemplo 5001, 8080 ou 8090.\n\n" +
                "Teste primeiro no próprio computador usando o link localhost. Depois teste o link com IP no celular.",
                "Ajuda de rede e firewall",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async Task TestServerAsync()
        {
            int port = GetPortOrDefault();

            if (!RemoteControlManager.IsRunning)
            {
                TxtDiagnosticInfo.Text = "Servidor offline. Ative o controle remoto antes de testar.";
                System.Windows.MessageBox.Show("O servidor está offline. Ative o controle remoto e tente novamente.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                SetBusy(true, "Testando servidor...");

                using HttpClient client = new()
                {
                    Timeout = TimeSpan.FromSeconds(3)
                };

                string testUrl = $"http://127.0.0.1:{port}/api/ping";
                HttpResponseMessage response = await client.GetAsync(testUrl);

                if (response.IsSuccessStatusCode)
                {
                    TxtDiagnosticInfo.Text =
                        "Servidor online no computador. Se o celular não acessar, verifique se ele está na mesma rede e se o Firewall do Windows permitiu o AlphaPlay em redes privadas.";
                    return;
                }

                TxtDiagnosticInfo.Text = $"Servidor respondeu, mas com status HTTP {(int)response.StatusCode}.";
            }
            catch (Exception ex)
            {
                TxtDiagnosticInfo.Text = $"Falha ao testar o servidor local: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
                UpdateRemoteState(RemoteControlManager.IsRunning, RemoteControlManager.IsRunning ? "Ativo" : "Desativado");
            }
        }

        private void CheckPortStatus(bool showMessage)
        {
            int port = GetPortOrDefault();
            string result;

            if (RemoteControlManager.IsRunning && RemoteControlManager.CurrentPort == port)
            {
                result = $"Porta {port}: em uso pelo AlphaPlay.";
            }
            else if (IsTcpPortAvailable(port))
            {
                result = $"Porta {port}: livre para uso.";
            }
            else
            {
                result = $"Porta {port}: ocupada por outro programa. Escolha outra porta.";
            }

            TxtDiagnosticInfo.Text = result;

            if (showMessage)
            {
                System.Windows.MessageBox.Show(result, "Verificação de porta", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static bool IsTcpPortAvailable(int port)
        {
            TcpListener? listener = null;

            try
            {
                listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                listener?.Stop();
            }
        }

        private void RefreshDiagnosticSummary()
        {
            if (TxtDiagnosticInfo == null)
            {
                return;
            }

            int port = GetPortOrDefault();
            string server = RemoteControlManager.IsRunning ? "Online" : "Offline";
            string ip = string.IsNullOrWhiteSpace(_localIp) ? "Não encontrado" : _localIp;
            string link = RemoteControlManager.IsRunning && !string.IsNullOrWhiteSpace(_currentLink) ? _currentLink : "Indisponível";

            TxtDiagnosticInfo.Text = $"Servidor: {server} | IP: {ip} | Porta: {port} | Link: {link}";
        }

        private string BuildDiagnosticText(bool includeTips)
        {
            int port = GetPortOrDefault();
            string ip = string.IsNullOrWhiteSpace(_localIp) ? "Não encontrado" : _localIp;
            string link = RemoteControlManager.IsRunning && !string.IsNullOrWhiteSpace(_currentLink) ? _currentLink : "Indisponível";
            string portStatus = RemoteControlManager.IsRunning && RemoteControlManager.CurrentPort == port
                ? "Em uso pelo AlphaPlay"
                : IsTcpPortAvailable(port) ? "Livre" : "Ocupada por outro programa";

            StringBuilder builder = new();
            builder.AppendLine("AlphaPlay - Diagnóstico do controle remoto");
            builder.AppendLine($"Servidor: {(RemoteControlManager.IsRunning ? "Online" : "Offline")}");
            builder.AppendLine($"IP local: {ip}");
            builder.AppendLine($"Porta configurada: {port}");
            builder.AppendLine($"Status da porta: {portStatus}");
            builder.AppendLine($"Link: {link}");
            builder.AppendLine($"PIN exigido: {(_settings.RemoteControlRequirePin ? "Sim" : "Não")}");

            if (includeTips)
            {
                builder.AppendLine();
                builder.AppendLine("Checklist:");
                builder.AppendLine("- Celular e computador estão na mesma rede?");
                builder.AppendLine("- O Wi-Fi não está em modo convidado?");
                builder.AppendLine("- O Firewall do Windows permitiu o AlphaPlay em rede privada?");
                builder.AppendLine("- A porta configurada não está sendo usada por outro programa?");
            }

            return builder.ToString();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            TrySavePort(showMessage: false);
            _settings.RemoteControlRequirePin = ChkRequirePin.IsChecked == true;
            SettingsService.SaveSettings(_settings);
            Close();
        }
    }
}
