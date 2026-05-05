namespace AlphaPlay.Models
{
    public class AppSettings
    {
        public string MusicFolder { get; set; } = string.Empty;
        public int DefaultVolume { get; set; } = 80;
        public string DefaultPlayMode { get; set; } = "Parar";
        public bool ShowVideoInMainWindow { get; set; } = false;
        public bool AutoShowVideoForMp4 { get; set; } = true;
        public bool AutoHideVideoForMp3 { get; set; } = true;
        public bool AutoFullscreenForVideo { get; set; } = false;
        public string FullscreenMonitorDeviceName { get; set; } = string.Empty;
        public bool CloseFullscreenOnStop { get; set; } = true;
        public bool CloseFullscreenOnAudio { get; set; } = true;
        public int RemoteControlPort { get; set; } = 5000;
        public bool RemoteControlEnabled { get; set; } = false;
        public bool RemoteControlRequirePin { get; set; } = false;
        public string RemoteControlPin { get; set; } = string.Empty;
    }
}
