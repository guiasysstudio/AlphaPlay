namespace AlphaPlay.Models
{
    public sealed class RemotePlayerStatus
    {
        public bool ServerOnline { get; set; } = true;
        public bool PlayerConnected { get; set; }
        public bool IsPlaying { get; set; }
        public bool IsPaused { get; set; }
        public bool IsStopped { get; set; }
        public string PlaybackState { get; set; } = "Parado";
        public string CurrentTitle { get; set; } = "Nenhuma música tocando";
        public int PositionSeconds { get; set; }
        public int DurationSeconds { get; set; }
        public int Volume { get; set; }
        public int LibraryCount { get; set; }
        public int QueueCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class RemoteCommandResult
    {
        public bool Ok { get; set; }
        public string Command { get; set; } = string.Empty;
        public bool Executed { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class RemoteTrackInfo
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public bool IsVideo { get; set; }
        public bool IsMissing { get; set; }
        public bool IsInQueue { get; set; }
    }

    public sealed class RemoteQueueItemInfo
    {
        public int Index { get; set; }
        public int Position { get; set; }
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public bool IsVideo { get; set; }
        public bool IsMissing { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsPaused { get; set; }
    }

}
