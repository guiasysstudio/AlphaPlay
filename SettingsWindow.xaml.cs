using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AlphaPlay.Models;
using AlphaPlay.Services;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace AlphaPlay
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Settings { get; private set; }

        public SettingsWindow(AppSettings currentSettings)
        {
            InitializeComponent();
            Settings = new AppSettings
            {
                MusicFolder = currentSettings.MusicFolder,
                DefaultVolume = currentSettings.DefaultVolume,
                DefaultPlayMode = currentSettings.DefaultPlayMode,
                ShowVideoInMainWindow = currentSettings.ShowVideoInMainWindow,
                AutoShowVideoForMp4 = currentSettings.AutoShowVideoForMp4,
                AutoHideVideoForMp3 = currentSettings.AutoHideVideoForMp3,
                AutoFullscreenForVideo = currentSettings.AutoFullscreenForVideo,
                FullscreenMonitorDeviceName = currentSettings.FullscreenMonitorDeviceName,
                CloseFullscreenOnStop = currentSettings.CloseFullscreenOnStop,
                CloseFullscreenOnAudio = currentSettings.CloseFullscreenOnAudio,
                RemoteControlPort = currentSettings.RemoteControlPort,
                RemoteControlEnabled = currentSettings.RemoteControlEnabled
            };

            LoadMonitors();
            LoadSettingsToUi();
        }

        private void LoadSettingsToUi()
        {
            TxtMusicFolder.Text = Settings.MusicFolder;
            SliderDefaultVolume.Value = Settings.DefaultVolume;
            TxtVolumeValue.Text = $"{Settings.DefaultVolume}%";
            SetComboValue(ComboDefaultPlayMode, Settings.DefaultPlayMode);
            ChkShowVideoInMainWindow.IsChecked = Settings.ShowVideoInMainWindow;
            ChkAutoShowVideoForMp4.IsChecked = Settings.AutoShowVideoForMp4;
            ChkAutoHideVideoForMp3.IsChecked = Settings.AutoHideVideoForMp3;
            ChkAutoFullscreenForVideo.IsChecked = Settings.AutoFullscreenForVideo;
            ChkCloseFullscreenOnStop.IsChecked = Settings.CloseFullscreenOnStop;
            ChkCloseFullscreenOnAudio.IsChecked = Settings.CloseFullscreenOnAudio;
            SelectConfiguredMonitor();
        }


        private void LoadMonitors()
        {
            ComboFullscreenMonitor.Items.Clear();

            foreach (WinForms.Screen screen in WinForms.Screen.AllScreens)
            {
                string mainLabel = screen.Primary ? " - Principal" : string.Empty;
                ComboFullscreenMonitor.Items.Add(new MonitorOption
                {
                    DeviceName = screen.DeviceName,
                    DisplayName = $"{screen.DeviceName}{mainLabel} ({screen.Bounds.Width}x{screen.Bounds.Height})"
                });
            }

            if (ComboFullscreenMonitor.Items.Count > 0)
            {
                ComboFullscreenMonitor.SelectedIndex = 0;
            }
        }

        private void SelectConfiguredMonitor()
        {
            if (ComboFullscreenMonitor.Items.Count == 0)
            {
                return;
            }

            foreach (MonitorOption option in ComboFullscreenMonitor.Items)
            {
                if (option.DeviceName == Settings.FullscreenMonitorDeviceName)
                {
                    ComboFullscreenMonitor.SelectedItem = option;
                    return;
                }
            }

            ComboFullscreenMonitor.SelectedIndex = 0;
        }

        private static void SetComboValue(System.Windows.Controls.ComboBox comboBox, string value)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in comboBox.Items)
            {
                if (item.Content?.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private static string GetComboValue(System.Windows.Controls.ComboBox comboBox)
        {
            if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                return item.Content?.ToString() ?? "Parar";
            }

            return "Parar";
        }

        private void BtnChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new()
            {
                Title = "Selecione a pasta das músicas do AlphaPlay",
                InitialDirectory = Directory.Exists(TxtMusicFolder.Text) ? TxtMusicFolder.Text : AppFolderService.GetDefaultMusicFolder()
            };

            if (dialog.ShowDialog(this) == true)
            {
                TxtMusicFolder.Text = dialog.FolderName;
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = TxtMusicFolder.Text.Trim();

            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        private void BtnDefaultFolder_Click(object sender, RoutedEventArgs e)
        {
            TxtMusicFolder.Text = AppFolderService.GetDefaultMusicFolder();
        }

        private void SliderDefaultVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtVolumeValue != null)
            {
                TxtVolumeValue.Text = $"{(int)SliderDefaultVolume.Value}%";
            }
        }

        private void SliderDefaultVolume_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = e.Delta > 0 ? 5 : -5;
            SliderDefaultVolume.Value = Math.Max(SliderDefaultVolume.Minimum, Math.Min(SliderDefaultVolume.Maximum, SliderDefaultVolume.Value + step));
            e.Handled = true;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string folder = TxtMusicFolder.Text.Trim();

            if (string.IsNullOrWhiteSpace(folder))
            {
                System.Windows.MessageBox.Show("Informe a pasta das músicas.", "AlphaPlay");
                return;
            }

            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Não foi possível criar/acessar a pasta informada.\n\n{ex.Message}", "AlphaPlay");
                return;
            }

            Settings.MusicFolder = folder;
            Settings.DefaultVolume = (int)SliderDefaultVolume.Value;
            Settings.DefaultPlayMode = GetComboValue(ComboDefaultPlayMode);
            Settings.ShowVideoInMainWindow = ChkShowVideoInMainWindow.IsChecked == true;
            Settings.AutoShowVideoForMp4 = ChkAutoShowVideoForMp4.IsChecked == true;
            Settings.AutoHideVideoForMp3 = ChkAutoHideVideoForMp3.IsChecked == true;
            Settings.AutoFullscreenForVideo = ChkAutoFullscreenForVideo.IsChecked == true;
            Settings.CloseFullscreenOnStop = ChkCloseFullscreenOnStop.IsChecked == true;
            Settings.CloseFullscreenOnAudio = ChkCloseFullscreenOnAudio.IsChecked == true;
            AppSettings savedRemoteSettings = SettingsService.LoadSettings();
            Settings.RemoteControlPort = savedRemoteSettings.RemoteControlPort;
            Settings.RemoteControlEnabled = savedRemoteSettings.RemoteControlEnabled;

            if (ComboFullscreenMonitor.SelectedItem is MonitorOption monitor)
            {
                Settings.FullscreenMonitorDeviceName = monitor.DeviceName;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnRemoteControl_Click(object sender, RoutedEventArgs e)
        {
            RemoteControlWindow window = new()
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private class MonitorOption
        {
            public string DeviceName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }
    }
}
