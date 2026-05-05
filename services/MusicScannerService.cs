using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public static class MusicScannerService
    {
        private static readonly string[] SupportedExtensions =
        {
            ".mp3",
            ".mp4",
            ".wav",
            ".m4a",
            ".aac",
            ".flac",
            ".mkv",
            ".avi",
            ".mov",
            ".wmv"
        };

        public static List<MusicFile> ScanMusicFolder()
        {
            string folder = AppFolderService.GetMusicFolder();

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            List<MusicFile> musics = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                .Select((file, index) => new MusicFile
                {
                    Id = index + 1,
                    Title = Path.GetFileNameWithoutExtension(file),
                    FileName = Path.GetFileName(file),
                    FilePath = file,
                    Extension = Path.GetExtension(file).ToLower(),
                    IsMissing = false
                })
                .OrderBy(music => music.Title)
                .ToList();

            DatabaseService.SyncMusicLibrary(musics);

            return musics;
        }
    }
}