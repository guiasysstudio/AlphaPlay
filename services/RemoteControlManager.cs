using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public static class RemoteControlManager
    {
        private static readonly RemoteControlServerService Server = new();
        private static readonly object SyncRoot = new();

        private static Func<RemotePlayerStatus>? _statusProvider;
        private static Func<string, RemoteCommandResult>? _commandHandler;
        private static Func<int, RemoteCommandResult>? _volumeHandler;
        private static Func<IReadOnlyList<RemoteTrackInfo>>? _tracksProvider;
        private static Func<IReadOnlyList<RemoteQueueItemInfo>>? _queueProvider;
        private static Func<string, int, RemoteCommandResult>? _libraryCommandHandler;
        private static Func<string, int, int, RemoteCommandResult>? _queueCommandHandler;

        public static bool IsRunning => Server.IsRunning;
        public static int CurrentPort => Server.CurrentPort;

        public static void RegisterPlayerController(
            Func<RemotePlayerStatus> statusProvider,
            Func<string, RemoteCommandResult> commandHandler,
            Func<int, RemoteCommandResult> volumeHandler,
            Func<IReadOnlyList<RemoteTrackInfo>> tracksProvider,
            Func<IReadOnlyList<RemoteQueueItemInfo>> queueProvider,
            Func<string, int, RemoteCommandResult> libraryCommandHandler,
            Func<string, int, int, RemoteCommandResult> queueCommandHandler)
        {
            lock (SyncRoot)
            {
                _statusProvider = statusProvider;
                _commandHandler = commandHandler;
                _volumeHandler = volumeHandler;
                _tracksProvider = tracksProvider;
                _queueProvider = queueProvider;
                _libraryCommandHandler = libraryCommandHandler;
                _queueCommandHandler = queueCommandHandler;
            }
        }

        public static void ClearPlayerController()
        {
            lock (SyncRoot)
            {
                _statusProvider = null;
                _commandHandler = null;
                _volumeHandler = null;
                _tracksProvider = null;
                _queueProvider = null;
                _libraryCommandHandler = null;
                _queueCommandHandler = null;
            }
        }

        public static RemotePlayerStatus GetStatus()
        {
            Func<RemotePlayerStatus>? provider;

            lock (SyncRoot)
            {
                provider = _statusProvider;
            }

            if (provider == null)
            {
                return new RemotePlayerStatus
                {
                    ServerOnline = true,
                    PlayerConnected = false,
                    IsPlaying = false,
                    IsPaused = false,
                    IsStopped = true,
                    PlaybackState = "Aguardando player",
                    CurrentTitle = "AlphaPlay aberto, aguardando conexão com o player",
                    PositionSeconds = 0,
                    DurationSeconds = 0,
                    Volume = 50,
                    Message = "Servidor online, mas o player ainda não foi conectado."
                };
            }

            return provider();
        }

        public static IReadOnlyList<RemoteTrackInfo> GetTracks()
        {
            Func<IReadOnlyList<RemoteTrackInfo>>? provider;

            lock (SyncRoot)
            {
                provider = _tracksProvider;
            }

            return provider?.Invoke() ?? Array.Empty<RemoteTrackInfo>();
        }

        public static IReadOnlyList<RemoteQueueItemInfo> GetQueue()
        {
            Func<IReadOnlyList<RemoteQueueItemInfo>>? provider;

            lock (SyncRoot)
            {
                provider = _queueProvider;
            }

            return provider?.Invoke() ?? Array.Empty<RemoteQueueItemInfo>();
        }

        public static RemoteCommandResult ExecuteCommand(string command)
        {
            Func<string, RemoteCommandResult>? handler;

            lock (SyncRoot)
            {
                handler = _commandHandler;
            }

            if (handler == null)
            {
                return new RemoteCommandResult
                {
                    Ok = false,
                    Command = command,
                    Executed = false,
                    Message = "O player ainda não está conectado ao controle remoto."
                };
            }

            return handler(command);
        }

        public static RemoteCommandResult SetVolume(int volume)
        {
            Func<int, RemoteCommandResult>? handler;

            lock (SyncRoot)
            {
                handler = _volumeHandler;
            }

            if (handler == null)
            {
                return new RemoteCommandResult
                {
                    Ok = false,
                    Command = "set-volume",
                    Executed = false,
                    Message = "O player ainda não está conectado ao controle remoto."
                };
            }

            return handler(volume);
        }

        public static RemoteCommandResult ExecuteLibraryCommand(string command, int trackId)
        {
            Func<string, int, RemoteCommandResult>? handler;

            lock (SyncRoot)
            {
                handler = _libraryCommandHandler;
            }

            if (handler == null)
            {
                return new RemoteCommandResult
                {
                    Ok = false,
                    Command = command,
                    Executed = false,
                    Message = "A biblioteca ainda não está conectada ao controle remoto."
                };
            }

            return handler(command, trackId);
        }

        public static RemoteCommandResult ExecuteQueueCommand(string command, int index, int value = 0)
        {
            Func<string, int, int, RemoteCommandResult>? handler;

            lock (SyncRoot)
            {
                handler = _queueCommandHandler;
            }

            if (handler == null)
            {
                return new RemoteCommandResult
                {
                    Ok = false,
                    Command = command,
                    Executed = false,
                    Message = "A fila ainda não está conectada ao controle remoto."
                };
            }

            return handler(command, index, value);
        }

        public static Task StartAsync(int port)
        {
            return Server.StartAsync(port);
        }

        public static Task StopAsync()
        {
            return Server.StopAsync();
        }
    }
}
