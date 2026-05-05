using System;
using System.IO;

namespace AlphaPlay.Services
{
    public static class AppFolderService
    {
        public static string GetDefaultMusicFolder()
        {
            string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            return Path.Combine(musicFolder, "AlphaPlay");
        }

        public static string GetMusicFolder()
        {
            string settingsPath = GetSettingsPath();
            string alphaPlayFolder = GetDefaultMusicFolder();

            try
            {
                if (File.Exists(settingsPath))
                {
                    string json = File.ReadAllText(settingsPath);
                    using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);

                    if (document.RootElement.TryGetProperty("MusicFolder", out System.Text.Json.JsonElement element))
                    {
                        string? configuredFolder = element.GetString();

                        if (!string.IsNullOrWhiteSpace(configuredFolder))
                        {
                            alphaPlayFolder = configuredFolder;
                        }
                    }
                }
            }
            catch
            {
                alphaPlayFolder = GetDefaultMusicFolder();
            }

            if (!Directory.Exists(alphaPlayFolder))
            {
                Directory.CreateDirectory(alphaPlayFolder);
            }

            return alphaPlayFolder;
        }

        public static string GetAppDataFolder()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDataFolder = Path.Combine(localAppData, "AlphaPlay");

            if (!Directory.Exists(appDataFolder))
            {
                Directory.CreateDirectory(appDataFolder);
            }

            return appDataFolder;
        }

        public static string GetDatabasePath()
        {
            return Path.Combine(GetAppDataFolder(), "alphaplay.db");
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetAppDataFolder(), "settings.json");
        }
    }
}
