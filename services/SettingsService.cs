using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public static class SettingsService
    {
        private static readonly Random PinRandom = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static AppSettings LoadSettings()
        {
            string path = AppFolderService.GetSettingsPath();

            if (!File.Exists(path))
            {
                AppSettings defaultSettings = CreateDefaultSettings();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }

            try
            {
                string json = File.ReadAllText(path);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

                if (settings == null)
                {
                    return CreateDefaultSettings();
                }

                NormalizeSettings(settings);
                return settings;
            }
            catch
            {
                AppSettings defaultSettings = CreateDefaultSettings();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }
        }

        public static void SaveSettings(AppSettings settings)
        {
            NormalizeSettings(settings);

            string path = AppFolderService.GetSettingsPath();
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }

        public static AppSettings CreateDefaultSettings()
        {
            return new AppSettings
            {
                MusicFolder = AppFolderService.GetDefaultMusicFolder(),
                DefaultVolume = 80,
                DefaultPlayMode = "Parar",
                ShowVideoInMainWindow = false,
                AutoShowVideoForMp4 = true,
                AutoHideVideoForMp3 = true,
                AutoFullscreenForVideo = false,
                FullscreenMonitorDeviceName = string.Empty,
                CloseFullscreenOnStop = true,
                CloseFullscreenOnAudio = true,
                RemoteControlPort = 5000,
                RemoteControlEnabled = false,
                RemoteControlRequirePin = false,
                RemoteControlPin = GenerateRemotePin()
            };
        }

        public static void NormalizeSettings(AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.MusicFolder))
            {
                settings.MusicFolder = AppFolderService.GetDefaultMusicFolder();
            }

            settings.DefaultVolume = Math.Max(0, Math.Min(100, settings.DefaultVolume));
            if (settings.RemoteControlPort <= 0)
            {
                settings.RemoteControlPort = 5000;
            }
            else
            {
                settings.RemoteControlPort = Math.Max(1024, Math.Min(65535, settings.RemoteControlPort));
            }

            if (string.IsNullOrWhiteSpace(settings.RemoteControlPin) ||
                settings.RemoteControlPin.Length != 4 ||
                !settings.RemoteControlPin.All(char.IsDigit))
            {
                settings.RemoteControlPin = GenerateRemotePin();
            }

            if (settings.DefaultPlayMode != "Parar" &&
                settings.DefaultPlayMode != "Tocar próxima" &&
                settings.DefaultPlayMode != "Tocar todas")
            {
                settings.DefaultPlayMode = "Parar";
            }
        }

        public static string GenerateRemotePin()
        {
            lock (PinRandom)
            {
                return PinRandom.Next(0, 10000).ToString("D4");
            }
        }
    }
}
