using System.Windows;
using AlphaPlay.Models;
using AlphaPlay.Services;
using LibVLCSharp.Shared;

namespace AlphaPlay
{
    public partial class App : System.Windows.Application
    {
        protected override async void OnStartup(System.Windows.StartupEventArgs e)
        {
            Core.Initialize();
            base.OnStartup(e);

            AppSettings settings = SettingsService.LoadSettings();

            if (settings.RemoteControlEnabled)
            {
                try
                {
                    await RemoteControlManager.StartAsync(settings.RemoteControlPort);
                }
                catch
                {
                    settings.RemoteControlEnabled = false;
                    SettingsService.SaveSettings(settings);
                }
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            await RemoteControlManager.StopAsync();
            base.OnExit(e);
        }
    }
}
