using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public static class DatabaseService
    {
        public static void InitializeDatabase()
        {
            string databasePath = AppFolderService.GetDatabasePath();

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            string sql = @"
                CREATE TABLE IF NOT EXISTS playlists (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS playlist_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    playlist_id INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    extension TEXT NOT NULL,
                    position INTEGER NOT NULL,
                    FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS musics (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    file_path TEXT NOT NULL UNIQUE,
                    extension TEXT NOT NULL,
                    media_type TEXT NOT NULL,
                    exists_on_disk INTEGER NOT NULL,
                    found_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS playback_history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    file_path TEXT NOT NULL,
                    extension TEXT NOT NULL,
                    played_at TEXT NOT NULL
                );
            ";

            using SqliteCommand command = new(sql, connection);
            command.ExecuteNonQuery();

            MigrateLegacySavedSequence(connection);
        }

        public static List<PlaylistInfo> ListPlaylists()
        {
            List<PlaylistInfo> playlists = new();
            string databasePath = AppFolderService.GetDatabasePath();

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT id, name FROM playlists ORDER BY updated_at DESC, name ASC;";

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                playlists.Add(new PlaylistInfo
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1)
                });
            }

            return playlists;
        }

        public static PlaylistInfo SavePlaylist(string name, IEnumerable<MusicFile> musics, int? playlistId = null)
        {
            string databasePath = AppFolderService.GetDatabasePath();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            int id = playlistId ?? GetPlaylistIdByName(connection, transaction, name);

            if (id > 0)
            {
                using SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = "UPDATE playlists SET name = $name, updated_at = $updated_at WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$name", name);
                updateCommand.Parameters.AddWithValue("$updated_at", now);
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.ExecuteNonQuery();
            }
            else
            {
                using SqliteCommand insertPlaylist = connection.CreateCommand();
                insertPlaylist.Transaction = transaction;
                insertPlaylist.CommandText = @"
                    INSERT INTO playlists (name, created_at, updated_at)
                    VALUES ($name, $created_at, $updated_at);
                ";
                insertPlaylist.Parameters.AddWithValue("$name", name);
                insertPlaylist.Parameters.AddWithValue("$created_at", now);
                insertPlaylist.Parameters.AddWithValue("$updated_at", now);
                insertPlaylist.ExecuteNonQuery();

                using SqliteCommand idCommand = connection.CreateCommand();
                idCommand.Transaction = transaction;
                idCommand.CommandText = "SELECT last_insert_rowid();";
                id = Convert.ToInt32((long)idCommand.ExecuteScalar()!);
            }

            using SqliteCommand deleteItems = connection.CreateCommand();
            deleteItems.Transaction = transaction;
            deleteItems.CommandText = "DELETE FROM playlist_items WHERE playlist_id = $playlist_id;";
            deleteItems.Parameters.AddWithValue("$playlist_id", id);
            deleteItems.ExecuteNonQuery();

            int position = 1;
            foreach (MusicFile music in musics)
            {
                using SqliteCommand insertItem = connection.CreateCommand();
                insertItem.Transaction = transaction;
                insertItem.CommandText = @"
                    INSERT INTO playlist_items
                    (playlist_id, title, file_name, file_path, extension, position)
                    VALUES
                    ($playlist_id, $title, $file_name, $file_path, $extension, $position);
                ";
                insertItem.Parameters.AddWithValue("$playlist_id", id);
                insertItem.Parameters.AddWithValue("$title", music.Title);
                insertItem.Parameters.AddWithValue("$file_name", music.FileName);
                insertItem.Parameters.AddWithValue("$file_path", music.FilePath);
                insertItem.Parameters.AddWithValue("$extension", music.Extension);
                insertItem.Parameters.AddWithValue("$position", position);
                insertItem.ExecuteNonQuery();
                position++;
            }

            transaction.Commit();

            return new PlaylistInfo { Id = id, Name = name };
        }

        public static List<MusicFile> LoadPlaylistItems(int playlistId)
        {
            List<MusicFile> musics = new();
            string databasePath = AppFolderService.GetDatabasePath();

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                SELECT id, title, file_name, file_path, extension
                FROM playlist_items
                WHERE playlist_id = $playlist_id
                ORDER BY position ASC;
            ";
            command.Parameters.AddWithValue("$playlist_id", playlistId);

            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                musics.Add(new MusicFile
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    FileName = reader.GetString(2),
                    FilePath = reader.GetString(3),
                    Extension = reader.GetString(4),
                    IsMissing = !System.IO.File.Exists(reader.GetString(3))
                });
            }

            return musics;
        }


        public static void SyncMusicLibrary(IEnumerable<MusicFile> musics)
        {
            string databasePath = AppFolderService.GetDatabasePath();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            using SqliteCommand markMissing = connection.CreateCommand();
            markMissing.Transaction = transaction;
            markMissing.CommandText = "UPDATE musics SET exists_on_disk = 0, updated_at = $updated_at;";
            markMissing.Parameters.AddWithValue("$updated_at", now);
            markMissing.ExecuteNonQuery();

            foreach (MusicFile music in musics)
            {
                using SqliteCommand upsert = connection.CreateCommand();
                upsert.Transaction = transaction;
                upsert.CommandText = @"
                    INSERT INTO musics
                    (title, file_name, file_path, extension, media_type, exists_on_disk, found_at, updated_at)
                    VALUES
                    ($title, $file_name, $file_path, $extension, $media_type, 1, $found_at, $updated_at)
                    ON CONFLICT(file_path) DO UPDATE SET
                        title = excluded.title,
                        file_name = excluded.file_name,
                        extension = excluded.extension,
                        media_type = excluded.media_type,
                        exists_on_disk = 1,
                        updated_at = excluded.updated_at;
                ";
                upsert.Parameters.AddWithValue("$title", music.Title);
                upsert.Parameters.AddWithValue("$file_name", music.FileName);
                upsert.Parameters.AddWithValue("$file_path", music.FilePath);
                upsert.Parameters.AddWithValue("$extension", music.Extension);
                upsert.Parameters.AddWithValue("$media_type", music.IsVideo ? "video" : "audio");
                upsert.Parameters.AddWithValue("$found_at", now);
                upsert.Parameters.AddWithValue("$updated_at", now);
                upsert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public static void AddPlaybackHistory(MusicFile music)
        {
            string databasePath = AppFolderService.GetDatabasePath();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();

            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = @"
                INSERT INTO playback_history (title, file_name, file_path, extension, played_at)
                VALUES ($title, $file_name, $file_path, $extension, $played_at);
            ";
            insert.Parameters.AddWithValue("$title", music.Title);
            insert.Parameters.AddWithValue("$file_name", music.FileName);
            insert.Parameters.AddWithValue("$file_path", music.FilePath);
            insert.Parameters.AddWithValue("$extension", music.Extension);
            insert.Parameters.AddWithValue("$played_at", now);
            insert.ExecuteNonQuery();

            using SqliteCommand cleanup = connection.CreateCommand();
            cleanup.CommandText = @"
                DELETE FROM playback_history
                WHERE id NOT IN (
                    SELECT id FROM playback_history ORDER BY played_at DESC, id DESC LIMIT 100
                );
            ";
            cleanup.ExecuteNonQuery();
        }

        public static void DeletePlaylist(int playlistId)
        {
            string databasePath = AppFolderService.GetDatabasePath();

            using SqliteConnection connection = new($"Data Source={databasePath}");
            connection.Open();
            using SqliteTransaction transaction = connection.BeginTransaction();

            using SqliteCommand deleteItems = connection.CreateCommand();
            deleteItems.Transaction = transaction;
            deleteItems.CommandText = "DELETE FROM playlist_items WHERE playlist_id = $playlist_id;";
            deleteItems.Parameters.AddWithValue("$playlist_id", playlistId);
            deleteItems.ExecuteNonQuery();

            using SqliteCommand deletePlaylist = connection.CreateCommand();
            deletePlaylist.Transaction = transaction;
            deletePlaylist.CommandText = "DELETE FROM playlists WHERE id = $playlist_id;";
            deletePlaylist.Parameters.AddWithValue("$playlist_id", playlistId);
            deletePlaylist.ExecuteNonQuery();

            transaction.Commit();
        }

        private static int GetPlaylistIdByName(SqliteConnection connection, SqliteTransaction transaction, string name)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM playlists WHERE name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", name);

            object? result = command.ExecuteScalar();
            return result == null || result == DBNull.Value ? 0 : Convert.ToInt32((long)result);
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table_name;";
            command.Parameters.AddWithValue("$table_name", tableName);
            return Convert.ToInt32((long)command.ExecuteScalar()!) > 0;
        }

        private static void MigrateLegacySavedSequence(SqliteConnection connection)
        {
            if (!TableExists(connection, "saved_sequence"))
            {
                return;
            }

            using SqliteCommand countPlaylists = connection.CreateCommand();
            countPlaylists.CommandText = "SELECT COUNT(*) FROM playlists;";
            int playlistsCount = Convert.ToInt32((long)countPlaylists.ExecuteScalar()!);

            if (playlistsCount > 0)
            {
                return;
            }

            using SqliteCommand countLegacy = connection.CreateCommand();
            countLegacy.CommandText = "SELECT COUNT(*) FROM saved_sequence;";
            int legacyCount = Convert.ToInt32((long)countLegacy.ExecuteScalar()!);

            if (legacyCount == 0)
            {
                return;
            }

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            using SqliteTransaction transaction = connection.BeginTransaction();

            using SqliteCommand insertPlaylist = connection.CreateCommand();
            insertPlaylist.Transaction = transaction;
            insertPlaylist.CommandText = @"
                INSERT INTO playlists (name, created_at, updated_at)
                VALUES ('Sequência antiga', $created_at, $updated_at);
            ";
            insertPlaylist.Parameters.AddWithValue("$created_at", now);
            insertPlaylist.Parameters.AddWithValue("$updated_at", now);
            insertPlaylist.ExecuteNonQuery();

            using SqliteCommand idCommand = connection.CreateCommand();
            idCommand.Transaction = transaction;
            idCommand.CommandText = "SELECT last_insert_rowid();";
            int playlistId = Convert.ToInt32((long)idCommand.ExecuteScalar()!);

            using SqliteCommand migrateItems = connection.CreateCommand();
            migrateItems.Transaction = transaction;
            migrateItems.CommandText = @"
                INSERT INTO playlist_items (playlist_id, title, file_name, file_path, extension, position)
                SELECT $playlist_id, title, file_name, file_path, extension, position
                FROM saved_sequence
                ORDER BY position ASC;
            ";
            migrateItems.Parameters.AddWithValue("$playlist_id", playlistId);
            migrateItems.ExecuteNonQuery();

            transaction.Commit();
        }
    }
}
