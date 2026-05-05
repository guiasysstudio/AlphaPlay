using System.IO;

namespace AlphaPlay.Models
{
    public class MusicFile
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string FilePath { get; set; } = string.Empty;

        public string Extension { get; set; } = string.Empty;

        public bool IsMissing { get; set; }

        public bool IsVideo => Extension.ToLowerInvariant() is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm";

        public bool ExistsOnDisk()
        {
            return !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);
        }
    }
}
