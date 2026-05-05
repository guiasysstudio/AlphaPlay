using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public static class SequenceFileService
    {
        public static string GetSequencesFolder()
        {
            return AppFolderService.GetSequencesFolder();
        }

        public static List<PlaylistInfo> ListSequences()
        {
            string folder = GetSequencesFolder();

            return Directory.GetFiles(folder, "*.txt")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTime)
                .ThenBy(file => file.Name)
                .Select((file, index) => new PlaylistInfo
                {
                    Id = -(index + 1),
                    Name = Path.GetFileNameWithoutExtension(file.Name),
                    FilePath = file.FullName
                })
                .ToList();
        }

        public static PlaylistInfo SaveSequence(string name, IEnumerable<MusicFile> musics)
        {
            string folder = GetSequencesFolder();
            string safeName = SanitizeFileName(name);
            string path = Path.Combine(folder, safeName + ".txt");

            List<string> lines = new()
            {
                "# AlphaPlay - Sequência salva",
                "# Edite este arquivo deixando uma música por linha.",
                "# Use nomes exatamente iguais aos arquivos da pasta de músicas.",
                "# Linhas iniciadas com # são ignoradas.",
                string.Empty
            };

            foreach (MusicFile music in musics)
            {
                string line = !string.IsNullOrWhiteSpace(music.FileName)
                    ? music.FileName
                    : Path.GetFileName(music.FilePath);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line.Trim());
                }
            }

            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);

            return new PlaylistInfo
            {
                Id = 0,
                Name = safeName,
                FilePath = path
            };
        }

        public static List<string> LoadSequenceLines(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new List<string>();
            }

            return File.ReadAllLines(filePath, System.Text.Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.StartsWith("#"))
                .ToList();
        }

        public static void DeleteSequence(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public static string SanitizeFileName(string name)
        {
            string safe = string.IsNullOrWhiteSpace(name) ? "Sequencia" : name.Trim();

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(invalid, '_');
            }

            while (safe.Contains("  "))
            {
                safe = safe.Replace("  ", " ");
            }

            safe = safe.Trim('.', ' ');
            return string.IsNullOrWhiteSpace(safe) ? "Sequencia" : safe;
        }
    }
}
