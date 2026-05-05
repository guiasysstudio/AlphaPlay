using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AlphaPlay.Models;
using AlphaPlay.Services;
using LibVLCSharp.Shared;
using WinForms = System.Windows.Forms;
using System.Text;

namespace AlphaPlay
{
    public partial class MainWindow : Window
    {
        private List<MusicFile> _allMusics = new();
        private ObservableCollection<MusicFile> _filteredMusics = new();
        private ObservableCollection<SequenceItem> _sequence = new();
        private ObservableCollection<PlaylistInfo> _savedPlaylists = new();

        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private DispatcherTimer _timer;

        private MusicFile? _currentMusic;
        private int _currentSequenceIndex = -1;
        private bool _isSeeking = false;
        private bool _wasStopped = true;
        private bool _isPaused = false;
        private bool _isMuted = false;
        private int _lastVolumeBeforeMute = 80;
        private int _nextModeRemaining = 0;
        private bool _isHandlingEndReached = false;

        private int? _currentPlaylistId = null;
        private string _currentPlaylistName = string.Empty;
        private System.Windows.Point _dragStartPoint;
        private AppSettings _settings = SettingsService.CreateDefaultSettings();
        private bool? _manualVideoVisibilityOverride = null;
        private FullscreenVideoWindow? _fullscreenWindow = null;
        private bool _fullscreenWindowVisible = false;

        public MainWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += Timer_Tick;

            InitializeApp();
            InitializePlayer();
            RegisterRemoteControlController();
        }

        private void RegisterRemoteControlController()
        {
            RemoteControlManager.RegisterPlayerController(
                GetRemotePlayerStatus,
                ExecuteRemoteCommand,
                SetRemoteVolume,
                GetRemoteTracks,
                GetRemoteQueue,
                ExecuteRemoteLibraryCommand,
                ExecuteRemoteQueueCommand);
        }

        private RemotePlayerStatus GetRemotePlayerStatus()
        {
            return Dispatcher.Invoke(() =>
            {
                int durationSeconds = 0;
                int positionSeconds = 0;
                int volume = SliderVolume != null ? (int)SliderVolume.Value : _settings.DefaultVolume;
                bool playerConnected = _mediaPlayer != null;
                bool isPlaying = _mediaPlayer?.IsPlaying == true;
                bool isStopped = _wasStopped || _currentMusic == null;
                string playbackState = isPlaying ? "Tocando" : _isPaused ? "Pausado" : isStopped ? "Parado" : "Aguardando";

                if (_mediaPlayer != null)
                {
                    if (_mediaPlayer.Length > 0)
                    {
                        durationSeconds = (int)Math.Max(0, _mediaPlayer.Length / 1000);
                    }

                    if (_mediaPlayer.Time > 0)
                    {
                        positionSeconds = (int)Math.Max(0, _mediaPlayer.Time / 1000);
                    }
                }

                return new RemotePlayerStatus
                {
                    ServerOnline = true,
                    PlayerConnected = playerConnected,
                    IsPlaying = isPlaying,
                    IsPaused = _isPaused,
                    IsStopped = isStopped,
                    PlaybackState = playbackState,
                    CurrentTitle = _currentMusic?.Title ?? "Nenhuma música tocando",
                    PositionSeconds = positionSeconds,
                    DurationSeconds = durationSeconds,
                    Volume = Math.Max(0, Math.Min(100, volume)),
                    LibraryCount = _allMusics.Count,
                    QueueCount = _sequence.Count,
                    Message = playerConnected ? "Player conectado ao controle remoto." : "Player ainda não inicializado."
                };
            });
        }

        private RemoteCommandResult ExecuteRemoteCommand(string command)
        {
            return Dispatcher.Invoke(() =>
            {
                string normalizedCommand = (command ?? string.Empty).Trim().ToLowerInvariant();

                try
                {
                    switch (normalizedCommand)
                    {
                        case "play-pause":
                        case "playpause":
                            BtnPlayPause_Click(this, new RoutedEventArgs());
                            return RemoteCommandOk(normalizedCommand, "Play/Pause executado.");

                        case "stop":
                            StopPlayback();
                            return RemoteCommandOk(normalizedCommand, "Parar executado.");

                        case "next":
                        case "proxima":
                            BtnNext_Click(this, new RoutedEventArgs());
                            return RemoteCommandOk(normalizedCommand, "Próxima executado.");

                        case "previous":
                        case "anterior":
                            BtnPrevious_Click(this, new RoutedEventArgs());
                            return RemoteCommandOk(normalizedCommand, "Anterior executado.");

                        case "seek-forward":
                        case "forward-10":
                            SeekByMilliseconds(10000);
                            return RemoteCommandOk(normalizedCommand, "Avançar 10 segundos executado.");

                        case "seek-backward":
                        case "backward-10":
                            SeekByMilliseconds(-10000);
                            return RemoteCommandOk(normalizedCommand, "Voltar 10 segundos executado.");

                        case "volume-up":
                            return SetRemoteVolume(GetCurrentVolume() + 5);

                        case "volume-down":
                            return SetRemoteVolume(GetCurrentVolume() - 5);

                        default:
                            return new RemoteCommandResult
                            {
                                Ok = false,
                                Command = normalizedCommand,
                                Executed = false,
                                Message = "Comando não reconhecido pelo AlphaPlay."
                            };
                    }
                }
                catch (Exception ex)
                {
                    return new RemoteCommandResult
                    {
                        Ok = false,
                        Command = normalizedCommand,
                        Executed = false,
                        Message = $"Erro ao executar comando: {ex.Message}"
                    };
                }
            });
        }

        private RemoteCommandResult SetRemoteVolume(int volume)
        {
            return Dispatcher.Invoke(() =>
            {
                int safeVolume = Math.Max(0, Math.Min(100, volume));

                if (SliderVolume != null)
                {
                    SliderVolume.Value = safeVolume;
                }

                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Volume = safeVolume;
                }

                return RemoteCommandOk("set-volume", $"Volume ajustado para {safeVolume}%.");
            });
        }

        private IReadOnlyList<RemoteTrackInfo> GetRemoteTracks()
        {
            return Dispatcher.Invoke(() =>
            {
                HashSet<string> queuedPaths = _sequence
                    .Select(item => item.FilePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                return _allMusics
                    .Select(music => new RemoteTrackInfo
                    {
                        Id = music.Id,
                        Title = music.Title,
                        FileName = music.FileName,
                        Extension = music.Extension,
                        IsVideo = music.IsVideo,
                        IsMissing = music.IsMissing || !File.Exists(music.FilePath),
                        IsInQueue = queuedPaths.Contains(music.FilePath)
                    })
                    .ToList();
            });
        }

        private IReadOnlyList<RemoteQueueItemInfo> GetRemoteQueue()
        {
            return Dispatcher.Invoke(() =>
            {
                return _sequence
                    .Select((item, index) => new RemoteQueueItemInfo
                    {
                        Index = index,
                        Position = index + 1,
                        Title = item.Title,
                        FileName = item.FileName,
                        Extension = item.Extension,
                        IsVideo = item.Music.IsVideo,
                        IsMissing = item.IsMissing || !File.Exists(item.FilePath),
                        IsCurrent = !_wasStopped && index == _currentSequenceIndex,
                        IsPaused = !_wasStopped && _isPaused && index == _currentSequenceIndex
                    })
                    .ToList();
            });
        }

        private RemoteCommandResult ExecuteRemoteLibraryCommand(string command, int trackId)
        {
            return Dispatcher.Invoke(() =>
            {
                MusicFile? music = _allMusics.FirstOrDefault(item => item.Id == trackId);

                if (music == null)
                {
                    return new RemoteCommandResult
                    {
                        Ok = false,
                        Command = command,
                        Executed = false,
                        Message = "Música não encontrada na biblioteca."
                    };
                }

                switch ((command ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "add":
                    case "add-track":
                        _sequence.Add(new SequenceItem { Music = music });
                        RefreshSequencePositions();
                        return RemoteCommandOk(command, $"Adicionado à fila: {music.Title}");

                    case "play":
                    case "play-track":
                        PlayMusic(music);
                        return RemoteCommandOk(command, $"Tocando agora: {music.Title}");

                    default:
                        return new RemoteCommandResult
                        {
                            Ok = false,
                            Command = command,
                            Executed = false,
                            Message = "Comando de biblioteca não reconhecido."
                        };
                }
            });
        }

        private RemoteCommandResult ExecuteRemoteQueueCommand(string command, int index, int value)
        {
            return Dispatcher.Invoke(() =>
            {
                string normalizedCommand = (command ?? string.Empty).Trim().ToLowerInvariant();

                if (normalizedCommand == "clear")
                {
                    _sequence.Clear();
                    _currentSequenceIndex = -1;
                    RefreshSequencePositions();
                    return RemoteCommandOk(normalizedCommand, "Fila limpa.");
                }

                if (index < 0 || index >= _sequence.Count)
                {
                    return new RemoteCommandResult
                    {
                        Ok = false,
                        Command = normalizedCommand,
                        Executed = false,
                        Message = "Item não encontrado na fila."
                    };
                }

                switch (normalizedCommand)
                {
                    case "play":
                        PlaySequenceIndex(index);
                        return RemoteCommandOk(normalizedCommand, $"Tocando: {_sequence[index].Title}");

                    case "remove":
                        string removedTitle = _sequence[index].Title;
                        _sequence.RemoveAt(index);
                        RefreshSequencePositions();
                        return RemoteCommandOk(normalizedCommand, $"Removido da fila: {removedTitle}");

                    case "move":
                        int newIndex = Math.Max(0, Math.Min(_sequence.Count - 1, value));

                        if (newIndex == index)
                        {
                            return RemoteCommandOk(normalizedCommand, "A música já está nessa posição.");
                        }

                        _sequence.Move(index, newIndex);
                        ListSequence.SelectedIndex = newIndex;
                        RefreshSequencePositions();
                        return RemoteCommandOk(normalizedCommand, "Ordem da fila atualizada.");

                    case "up":
                        if (index <= 0)
                        {
                            return RemoteCommandOk(normalizedCommand, "A música já está no início da fila.");
                        }

                        _sequence.Move(index, index - 1);
                        ListSequence.SelectedIndex = index - 1;
                        RefreshSequencePositions();
                        return RemoteCommandOk(normalizedCommand, "Música movida para cima.");

                    case "down":
                        if (index >= _sequence.Count - 1)
                        {
                            return RemoteCommandOk(normalizedCommand, "A música já está no final da fila.");
                        }

                        _sequence.Move(index, index + 1);
                        ListSequence.SelectedIndex = index + 1;
                        RefreshSequencePositions();
                        return RemoteCommandOk(normalizedCommand, "Música movida para baixo.");

                    default:
                        return new RemoteCommandResult
                        {
                            Ok = false,
                            Command = normalizedCommand,
                            Executed = false,
                            Message = "Comando de fila não reconhecido."
                        };
                }
            });
        }

        private int GetCurrentVolume()
        {
            return SliderVolume != null ? (int)SliderVolume.Value : _settings.DefaultVolume;
        }

        private static RemoteCommandResult RemoteCommandOk(string command, string message)
        {
            return new RemoteCommandResult
            {
                Ok = true,
                Command = command,
                Executed = true,
                Message = message
            };
        }

        private void InitializeApp()
        {
            AppFolderService.GetAppDataFolder();
            _settings = SettingsService.LoadSettings();
            ApplySettingsToInterface(reloadMusics: false);

            ListLibrary.ItemsSource = _filteredMusics;
            ListSequence.ItemsSource = _sequence;
            ComboSavedSequences.ItemsSource = _savedPlaylists;

            DatabaseService.InitializeDatabase();

            LoadMusics();
            LoadSavedPlaylists();
        }

        private void InitializePlayer()
        {
            _libVLC = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.Volume = _settings.DefaultVolume;

            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;

            VideoView.MediaPlayer = null;
            UpdateVideoVisibilityForCurrentState();
            _timer.Start();
        }


        private static void SetButtonIconContent(System.Windows.Controls.Button? button, string iconPath, string text, double iconSize = 16)
        {
            if (button == null)
            {
                return;
            }

            var panel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            var image = new System.Windows.Controls.Image
            {
                Width = iconSize,
                Height = iconSize,
                Margin = new System.Windows.Thickness(0, 0, string.IsNullOrWhiteSpace(text) ? 0 : 7, 0),
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri($"pack://application:,,,/{iconPath}", UriKind.Absolute))
            };

            panel.Children.Add(image);

            if (!string.IsNullOrWhiteSpace(text))
            {
                panel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
            }

            button.Content = panel;
        }

        private void UpdateVolumeButtonIcon()
        {
            if (BtnMuteVolume == null)
            {
                return;
            }

            bool muted = _isMuted || SliderVolume == null || SliderVolume.Value <= 0;
            string icon = muted ? "assets/icons/Desmutar_Volume.png" : "assets/icons/Mutar_Volume.png";
            BtnMuteVolume.ToolTip = muted ? "Desmutar volume" : "Mutar volume";
            SetButtonIconContent(BtnMuteVolume, icon, string.Empty, 20);
        }

        private void SeekByMilliseconds(long delta)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0)
            {
                return;
            }

            long newTime = Math.Max(0, Math.Min(_mediaPlayer.Length, _mediaPlayer.Time + delta));
            _mediaPlayer.Time = newTime;
            SliderProgress.Value = newTime;
            TxtTime.Text = $"{FormatTime(newTime)} / {FormatTime(_mediaPlayer.Length)}";
        }

        private void ApplySettingsToInterface(bool reloadMusics)
        {
            SettingsService.NormalizeSettings(_settings);
            TxtMusicFolder.Text = $"Pasta das músicas: {_settings.MusicFolder}";

            if (SliderVolume != null)
            {
                SliderVolume.Value = _settings.DefaultVolume;
            }

            if (TxtVolumePercent != null)
            {
                TxtVolumePercent.Text = $"{_settings.DefaultVolume}%";
            }

            _isMuted = _settings.DefaultVolume <= 0;
            _lastVolumeBeforeMute = _settings.DefaultVolume > 0 ? _settings.DefaultVolume : 80;
            UpdateVolumeButtonIcon();

            CloseFullscreenVideo();

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = _settings.DefaultVolume;
            }

            SetComboPlayMode(_settings.DefaultPlayMode);
            UpdateVideoVisibilityForCurrentState();

            if (reloadMusics)
            {
                LoadMusics();
            }
        }

        private void SetComboPlayMode(string playMode)
        {
            if (ComboPlayMode == null)
            {
                return;
            }

            foreach (System.Windows.Controls.ComboBoxItem item in ComboPlayMode.Items)
            {
                if (item.Content?.ToString() == playMode)
                {
                    ComboPlayMode.SelectedItem = item;
                    return;
                }
            }

            ComboPlayMode.SelectedIndex = 0;
        }

        private void SetVideoVisible(bool visible)
        {
            bool canShowVideo = CurrentMediaIsVideo();
            bool finalVisible = visible && canShowVideo;

            VideoPanel.Visibility = finalVisible ? Visibility.Visible : Visibility.Collapsed;
            VideoColumn.Width = finalVisible ? new GridLength(265) : new GridLength(0);

            if (BtnToggleVideo != null)
            {
                SetButtonIconContent(BtnToggleVideo, "assets/icons/Video.png", finalVisible ? "Ocultar vídeo" : "Mostrar vídeo");
            }

            if (!finalVisible && !_fullscreenWindowVisible && VideoView.MediaPlayer != null)
            {
                VideoView.MediaPlayer = null;
            }

            if (_fullscreenWindow == null && finalVisible && _mediaPlayer != null && VideoView.MediaPlayer == null)
            {
                VideoView.MediaPlayer = _mediaPlayer;
            }
        }

        private MusicFile? GetSelectedMediaCandidate()
        {
            if (!_wasStopped && _currentMusic != null)
            {
                return _currentMusic;
            }

            if (ListSequence?.SelectedItem is SequenceItem sequenceItem)
            {
                return sequenceItem.Music;
            }

            if (ListLibrary?.SelectedItem is MusicFile libraryMusic)
            {
                return libraryMusic;
            }

            return _currentMusic;
        }

        private bool CurrentMediaIsVideo()
        {
            MusicFile? media = GetSelectedMediaCandidate();
            return media != null && IsVideoFile(media);
        }

        private void UpdateVideoControlsVisibility()
        {
            bool showControls = CurrentMediaIsVideo();
            Visibility visibility = showControls ? Visibility.Visible : Visibility.Collapsed;

            if (BtnToggleVideo != null)
            {
                BtnToggleVideo.Visibility = visibility;
            }

            if (BtnFullscreenVideo != null)
            {
                BtnFullscreenVideo.Visibility = visibility;
            }

            if (!showControls)
            {
                _manualVideoVisibilityOverride = null;
                SetVideoVisible(false);
                HideFullscreenVideo();
            }
        }

        private WinForms.Screen GetConfiguredScreen()
        {
            if (!string.IsNullOrWhiteSpace(_settings.FullscreenMonitorDeviceName))
            {
                foreach (WinForms.Screen screen in WinForms.Screen.AllScreens)
                {
                    if (screen.DeviceName == _settings.FullscreenMonitorDeviceName)
                    {
                        return screen;
                    }
                }
            }

            return WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens.First();
        }

        private void OpenFullscreenVideo()
        {
            if (_mediaPlayer == null || _currentMusic == null || !IsVideoFile(_currentMusic))
            {
                return;
            }

            WinForms.Screen screen = GetConfiguredScreen();

            if (_fullscreenWindow == null)
            {
                FullscreenVideoWindow window = new FullscreenVideoWindow();
                _fullscreenWindow = window;

                window.RequestCloseFullscreen += (_, _) => HideFullscreenVideo();
                window.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_fullscreenWindow, window))
                    {
                        _fullscreenWindow = null;
                        _fullscreenWindowVisible = false;
                    }

                    UpdateFullscreenButtonText();
                };
            }

            // Para evitar duas janelas de vídeo e travamentos, o player fica ligado
            // em apenas uma superfície de vídeo por vez.
            VideoView.MediaPlayer = null;
            _fullscreenWindow.AttachPlayer(_mediaPlayer);
            _fullscreenWindow.ShowOnScreen(screen);
            _fullscreenWindowVisible = true;
            UpdateFullscreenButtonText();
        }

        private void HideFullscreenVideo()
        {
            if (_fullscreenWindow == null)
            {
                return;
            }

            try
            {
                _fullscreenWindow.Hide();
            }
            catch
            {
                // Não deixa o fechamento da tela cheia derrubar o AlphaPlay.
            }

            _fullscreenWindowVisible = false;
            UpdateFullscreenButtonText();
        }

        private void DestroyFullscreenVideo()
        {
            if (_fullscreenWindow == null)
            {
                return;
            }

            FullscreenVideoWindow window = _fullscreenWindow;
            _fullscreenWindow = null;
            _fullscreenWindowVisible = false;

            try
            {
                window.DetachPlayer();
                window.Close();
            }
            catch
            {
                // Evita travamento por exceção durante fechamento da janela de vídeo.
            }

            RestoreVideoToMainWindowIfNeeded();
            UpdateFullscreenButtonText();
        }

        private void CloseFullscreenVideo()
        {
            HideFullscreenVideo();
        }

        private void RestoreVideoToMainWindowIfNeeded()
        {
            if (_mediaPlayer != null && VideoPanel.Visibility == Visibility.Visible && !_fullscreenWindowVisible)
            {
                VideoView.MediaPlayer = _mediaPlayer;
            }
        }

        private void UpdateFullscreenButtonText()
        {
            if (BtnFullscreenVideo != null)
            {
                SetButtonIconContent(BtnFullscreenVideo, "assets/icons/TelaCheia.png", _fullscreenWindowVisible ? "Fechar tela" : "Tela cheia");
            }
        }

        private void ApplyFullscreenRulesForCurrentMedia()
        {
            if (_currentMusic == null)
            {
                return;
            }

            bool isVideo = IsVideoFile(_currentMusic);

            if (isVideo && _settings.AutoFullscreenForVideo)
            {
                OpenFullscreenVideo();
                return;
            }

            if (!isVideo && _settings.CloseFullscreenOnAudio)
            {
                CloseFullscreenVideo();
            }
        }

        private void UpdateVideoVisibilityForCurrentState()
        {
            UpdateVideoControlsVisibility();

            if (!CurrentMediaIsVideo())
            {
                SetVideoVisible(false);
                return;
            }

            if (_manualVideoVisibilityOverride.HasValue)
            {
                SetVideoVisible(_manualVideoVisibilityOverride.Value);
                return;
            }

            bool visible = _settings.ShowVideoInMainWindow;

            if (_currentMusic != null && IsVideoFile(_currentMusic) && _settings.AutoShowVideoForMp4)
            {
                visible = true;
            }

            SetVideoVisible(visible);
        }

        private static bool IsVideoFile(MusicFile music)
        {
            string extension = music.Extension.ToLowerInvariant();
            return extension == ".mp4" || extension == ".mkv" || extension == ".avi" || extension == ".mov" || extension == ".wmv";
        }

        private void LoadMusics()
        {
            _allMusics = MusicScannerService.ScanMusicFolder();
            RefreshSequenceMissingStatus();
            ApplySearch();
        }

        private void ApplySearch()
        {
            string search = NormalizeSearchText(TxtSearch.Text);

            List<MusicFile> result = string.IsNullOrWhiteSpace(search)
                ? _allMusics
                : _allMusics
                    .Where(music => MusicMatchesSearch(music, search))
                    .ToList();

            _filteredMusics.Clear();

            foreach (MusicFile music in result)
            {
                _filteredMusics.Add(music);
            }

            TxtLibraryCount.Text = $"{_filteredMusics.Count} música(s) encontrada(s)";
        }

        private static bool MusicMatchesSearch(MusicFile music, string normalizedSearch)
        {
            if (string.IsNullOrWhiteSpace(normalizedSearch))
            {
                return true;
            }

            string title = NormalizeSearchText(music.Title);
            string fileName = NormalizeSearchText(music.FileName);
            string fileNameWithoutExtension = NormalizeSearchText(Path.GetFileNameWithoutExtension(music.FileName));

            return title.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                   fileName.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                   fileNameWithoutExtension.StartsWith(normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSearchText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = value.Trim().Normalize(NormalizationForm.FormD);
            char[] chars = normalized
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray();

            return new string(chars).Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private void LoadSavedPlaylists(int? selectPlaylistId = null)
        {
            _savedPlaylists.Clear();

            foreach (PlaylistInfo playlist in DatabaseService.ListPlaylists())
            {
                _savedPlaylists.Add(playlist);
            }

            if (selectPlaylistId.HasValue)
            {
                ComboSavedSequences.SelectedItem = _savedPlaylists.FirstOrDefault(p => p.Id == selectPlaylistId.Value);
            }
            else if (_currentPlaylistId.HasValue)
            {
                ComboSavedSequences.SelectedItem = _savedPlaylists.FirstOrDefault(p => p.Id == _currentPlaylistId.Value);
            }
        }

        private void PlayMusic(MusicFile music, int sequenceIndex = -1, bool resetNextCounter = true)
        {
            if (_libVLC == null || _mediaPlayer == null)
            {
                System.Windows.MessageBox.Show("O player VLC ainda não foi inicializado.", "AlphaPlay");
                return;
            }

            if (!File.Exists(music.FilePath))
            {
                music.IsMissing = true;
                RefreshSequenceMissingStatus();
                System.Windows.MessageBox.Show(
                    $"Esta música não foi encontrada.\n\n{music.FilePath}\n\nVerifique se o arquivo foi apagado, movido ou renomeado.",
                    "Arquivo não encontrado",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            music.IsMissing = false;
            _currentMusic = music;
            _currentSequenceIndex = sequenceIndex;
            _wasStopped = false;
            _isPaused = false;
            _manualVideoVisibilityOverride = null;
            SetButtonIconContent(BtnPlayPause, "assets/icons/Pause.png", string.Empty, 40);
            UpdateVideoVisibilityForCurrentState();
            UpdateCurrentSequenceHighlight();

            if (resetNextCounter)
            {
                _nextModeRemaining = GetSelectedPlayMode() == "Tocar próxima" ? 1 : 0;
            }

            TxtNowPlaying.Text = $"Tocando: {music.Title}";
            TxtStatus.Text = "Tocando";

            try
            {
                bool shouldOpenFullscreen = IsVideoFile(music) && _settings.AutoFullscreenForVideo;

                if (shouldOpenFullscreen)
                {
                    OpenFullscreenVideo();
                }
                else if (!IsVideoFile(music) && _settings.CloseFullscreenOnAudio)
                {
                    HideFullscreenVideo();
                    RestoreVideoToMainWindowIfNeeded();
                }

                using Media media = new Media(_libVLC, new Uri(music.FilePath));
                _mediaPlayer.Play(media);
                DatabaseService.AddPlaybackHistory(music);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Não foi possível tocar este arquivo.\n\n{ex.Message}", "Erro no AlphaPlay");
            }
        }

        private string GetSelectedPlayMode()
        {
            if (ComboPlayMode.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                return item.Content?.ToString() ?? "Parar";
            }

            return "Parar";
        }

        private void PlaySequenceIndex(int index, bool resetNextCounter = true)
        {
            if (index < 0 || index >= _sequence.Count)
            {
                return;
            }

            ListSequence.SelectedIndex = index;
            ListSequence.ScrollIntoView(_sequence[index]);

            PlayMusic(_sequence[index].Music, index, resetNextCounter);
        }

        private void StopPlayback()
        {
            _mediaPlayer?.Stop();

            if (_settings.CloseFullscreenOnStop)
            {
                DestroyFullscreenVideo();
            }
            _wasStopped = true;
            _isPaused = false;
            SetButtonIconContent(BtnPlayPause, "assets/icons/Play.png", string.Empty, 40);

            SliderProgress.Value = 0;
            TxtTime.Text = "00:00 / 00:00";
            ClearSequenceHighlight();
            UpdateVideoVisibilityForCurrentState();

            if (_currentMusic != null)
            {
                TxtNowPlaying.Text = $"Parado: {_currentMusic.Title}";
                TxtStatus.Text = "Parado";
            }
            else
            {
                TxtNowPlaying.Text = "Nenhuma música selecionada";
                TxtStatus.Text = "Aguardando";
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer == null || _isSeeking)
            {
                return;
            }

            long length = _mediaPlayer.Length;
            long time = _mediaPlayer.Time;

            if (length > 0)
            {
                SliderProgress.Maximum = length;
                SliderProgress.Value = Math.Max(0, Math.Min(time, length));
                TxtTime.Text = $"{FormatTime(time)} / {FormatTime(length)}";
            }
        }

        private static string FormatTime(long milliseconds)
        {
            if (milliseconds < 0)
            {
                milliseconds = 0;
            }

            TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);

            if (time.TotalHours >= 1)
            {
                return time.ToString(@"hh\:mm\:ss");
            }

            return time.ToString(@"mm\:ss");
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            if (_isHandlingEndReached)
            {
                return;
            }

            _isHandlingEndReached = true;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(150);
                    HandlePlaybackEndedSafely();
                }
                finally
                {
                    _isHandlingEndReached = false;
                }
            }), DispatcherPriority.Background);
        }

        private void HandlePlaybackEndedSafely()
        {
            string mode = GetSelectedPlayMode();

            if (_currentSequenceIndex < 0 || _sequence.Count == 0)
            {
                StopPlayback();
                return;
            }

            int nextIndex = _currentSequenceIndex + 1;

            if (nextIndex >= _sequence.Count)
            {
                StopPlayback();
                return;
            }

            if (mode == "Parar")
            {
                StopPlayback();
                return;
            }

            if (mode == "Tocar próxima")
            {
                if (_nextModeRemaining > 0)
                {
                    _nextModeRemaining--;
                    PlaySequenceIndex(nextIndex, resetNextCounter: false);
                }
                else
                {
                    StopPlayback();
                }

                return;
            }

            if (mode == "Tocar todas")
            {
                PlaySequenceIndex(nextIndex, resetNextCounter: false);
                return;
            }

            StopPlayback();
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Windows.MessageBox.Show("O VLC encontrou um erro ao tentar tocar este arquivo.", "Erro no AlphaPlay");
            }), DispatcherPriority.Background);
        }

        private void BtnOpenMusicFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = AppFolderService.GetMusicFolder();

            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            LoadMusics();
        }
        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow window = new(_settings)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
            {
                return;
            }

            _settings = window.Settings;
            _manualVideoVisibilityOverride = null;
            SettingsService.SaveSettings(_settings);
            ApplySettingsToInterface(reloadMusics: true);
            ApplyFullscreenRulesForCurrentMedia();
        }



        private void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            UpdateCheckWindow window = new()
            {
                Owner = this
            };

            window.ShowDialog();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearch();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Clear();
            TxtSearch.Focus();
        }

        private void BtnAddToSequence_Click(object sender, RoutedEventArgs e)
        {
            AddSelectedLibraryMusicToSequence();
        }

        private void ListLibrary_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListLibrary.SelectedItem is not MusicFile selectedMusic)
            {
                return;
            }

            PlayMusic(selectedMusic);
        }

        private void AddSelectedLibraryMusicToSequence()
        {
            if (ListLibrary.SelectedItem is not MusicFile selectedMusic)
            {
                return;
            }

            _sequence.Add(new SequenceItem { Music = selectedMusic });
            RefreshSequencePositions();
        }

        private void BtnRemoveFromSequence_Click(object sender, RoutedEventArgs e)
        {
            if (ListSequence.SelectedItem is not SequenceItem selectedItem)
            {
                return;
            }

            _sequence.Remove(selectedItem);
            RefreshSequencePositions();
        }

        private void BtnClearMissingItems_Click(object sender, RoutedEventArgs e)
        {
            int removed = 0;

            for (int i = _sequence.Count - 1; i >= 0; i--)
            {
                if (_sequence[i].Music.IsMissing || !File.Exists(_sequence[i].Music.FilePath))
                {
                    _sequence.RemoveAt(i);
                    removed++;
                }
            }

            RefreshSequencePositions();
            TxtStatus.Text = removed == 0 ? "Nenhum arquivo inválido encontrado" : $"{removed} arquivo(s) inválido(s) removido(s)";
        }

        private void BtnClearSequence_Click(object sender, RoutedEventArgs e)
        {
            _sequence.Clear();
            RefreshSequencePositions();
        }

        private void BtnMoveItemUp_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not SequenceItem item)
            {
                return;
            }

            MoveSequenceItem(item, -1);
        }

        private void BtnMoveItemDown_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as System.Windows.Controls.Button)?.Tag is not SequenceItem item)
            {
                return;
            }

            MoveSequenceItem(item, 1);
        }

        private void MoveSequenceItem(SequenceItem item, int direction)
        {
            int index = _sequence.IndexOf(item);
            int newIndex = index + direction;

            if (index < 0 || newIndex < 0 || newIndex >= _sequence.Count)
            {
                return;
            }

            _sequence.Move(index, newIndex);
            ListSequence.SelectedIndex = newIndex;
            RefreshSequencePositions();
        }

        private void RefreshSequenceMissingStatus()
        {
            for (int i = 0; i < _sequence.Count; i++)
            {
                _sequence[i].Music.IsMissing = !File.Exists(_sequence[i].Music.FilePath);
            }

            RefreshSequencePositions();
        }

        private int CountMissingSequenceItems()
        {
            return _sequence.Count(item => item.Music.IsMissing || !File.Exists(item.Music.FilePath));
        }

        private void RefreshSequencePositions()
        {
            SynchronizeCurrentSequenceIndex();

            for (int i = 0; i < _sequence.Count; i++)
            {
                _sequence[i] = new SequenceItem
                {
                    Music = _sequence[i].Music,
                    Position = i + 1,
                    CanMoveUp = i > 0,
                    CanMoveDown = i < _sequence.Count - 1,
                    IsCurrent = !_wasStopped && i == _currentSequenceIndex,
                    IsPaused = !_wasStopped && _isPaused && i == _currentSequenceIndex
                };
            }
        }

        private void SynchronizeCurrentSequenceIndex()
        {
            if (_currentMusic == null || _sequence.Count == 0)
            {
                _currentSequenceIndex = -1;
                return;
            }

            int index = _sequence.ToList().FindIndex(item => item.FilePath == _currentMusic.FilePath);

            _currentSequenceIndex = index;
        }

        private void UpdateCurrentSequenceHighlight()
        {
            for (int i = 0; i < _sequence.Count; i++)
            {
                bool isCurrent = !_wasStopped && i == _currentSequenceIndex;
                _sequence[i].IsCurrent = isCurrent;
                _sequence[i].IsPaused = isCurrent && _isPaused;
            }
        }

        private void ClearSequenceHighlight()
        {
            foreach (SequenceItem item in _sequence)
            {
                item.IsCurrent = false;
                item.IsPaused = false;
            }
        }

        private void ListSequence_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            int index = ListSequence.SelectedIndex;

            if (index < 0)
            {
                return;
            }

            PlaySequenceIndex(index);
        }

        private void ListSequence_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void ListSequence_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (ListSequence.SelectedItem is SequenceItem selectedItem)
            {
                System.Windows.DragDrop.DoDragDrop(ListSequence, selectedItem, System.Windows.DragDropEffects.Move);
            }
        }

        private void ListSequence_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(SequenceItem)))
            {
                return;
            }

            SequenceItem droppedItem = (SequenceItem)e.Data.GetData(typeof(SequenceItem))!;
            SequenceItem? targetItem = GetSequenceItemFromPoint(e.GetPosition(ListSequence));

            if (targetItem == null || ReferenceEquals(droppedItem, targetItem))
            {
                return;
            }

            int oldIndex = _sequence.IndexOf(droppedItem);
            int newIndex = _sequence.IndexOf(targetItem);

            if (oldIndex < 0 || newIndex < 0 || oldIndex == newIndex)
            {
                return;
            }

            _sequence.Move(oldIndex, newIndex);
            ListSequence.SelectedIndex = newIndex;
            RefreshSequencePositions();
        }

        private SequenceItem? GetSequenceItemFromPoint(System.Windows.Point point)
        {
            UIElement? element = ListSequence.InputHitTest(point) as UIElement;

            while (element != null)
            {
                if (element is ListBoxItem item)
                {
                    return item.DataContext as SequenceItem;
                }

                element = System.Windows.Media.VisualTreeHelper.GetParent(element) as UIElement;
            }

            return null;
        }

        private void ListLibrary_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentMusic == null)
            {
                _manualVideoVisibilityOverride = null;
                UpdateVideoVisibilityForCurrentState();
            }
        }

        private void ListSequence_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentMusic == null)
            {
                _manualVideoVisibilityOverride = null;
                UpdateVideoVisibilityForCurrentState();
            }
        }

        private void BtnToggleVideo_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentMediaIsVideo())
            {
                UpdateVideoVisibilityForCurrentState();
                return;
            }

            bool currentlyVisible = VideoPanel.Visibility == Visibility.Visible;
            _manualVideoVisibilityOverride = !currentlyVisible;
            SetVideoVisible(!currentlyVisible);
            UpdateVideoControlsVisibility();
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_sequence.Count == 0)
            {
                return;
            }

            int previousIndex = _currentSequenceIndex > 0 ? _currentSequenceIndex - 1 : 0;
            PlaySequenceIndex(previousIndex);
        }

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null)
            {
                return;
            }

            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Pause();
                _isPaused = true;
                SetButtonIconContent(BtnPlayPause, "assets/icons/Play.png", string.Empty, 40);

                if (_currentMusic != null)
                {
                    TxtNowPlaying.Text = $"Pausado: {_currentMusic.Title}";
                }

                TxtStatus.Text = "Pausado";
                UpdateCurrentSequenceHighlight();
                return;
            }

            if (_currentMusic == null)
            {
                if (ListSequence.SelectedIndex >= 0)
                {
                    PlaySequenceIndex(ListSequence.SelectedIndex);
                    return;
                }

                if (ListLibrary.SelectedItem is MusicFile selectedMusic)
                {
                    PlayMusic(selectedMusic);
                    return;
                }

                return;
            }

            if (_wasStopped)
            {
                PlayMusic(_currentMusic, _currentSequenceIndex);
                return;
            }

            _mediaPlayer.Play();
            _isPaused = false;
            SetButtonIconContent(BtnPlayPause, "assets/icons/Pause.png", string.Empty, 40);
            TxtNowPlaying.Text = $"Tocando: {_currentMusic.Title}";
            TxtStatus.Text = "Tocando";
            UpdateCurrentSequenceHighlight();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_sequence.Count == 0)
            {
                return;
            }

            int nextIndex = _currentSequenceIndex >= 0 ? _currentSequenceIndex + 1 : ListSequence.SelectedIndex + 1;

            if (nextIndex < 0)
            {
                nextIndex = 0;
            }

            if (nextIndex >= _sequence.Count)
            {
                nextIndex = _sequence.Count - 1;
            }

            PlaySequenceIndex(nextIndex);
        }

        private void SliderProgress_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isSeeking = true;
            UpdateSliderPositionFromMouse(e);
        }

        private void SliderProgress_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            UpdateSliderPositionFromMouse(e);

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Time = (long)SliderProgress.Value;
            }

            _isSeeking = false;
        }

        private void UpdateSliderPositionFromMouse(MouseButtonEventArgs e)
        {
            if (SliderProgress.ActualWidth <= 0 || SliderProgress.Maximum <= 0)
            {
                return;
            }

            double x = e.GetPosition(SliderProgress).X;
            double percent = Math.Max(0, Math.Min(1, x / SliderProgress.ActualWidth));
            SliderProgress.Value = SliderProgress.Minimum + percent * (SliderProgress.Maximum - SliderProgress.Minimum);
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int volume = (int)SliderVolume.Value;

            if (volume > 0)
            {
                _lastVolumeBeforeMute = volume;
                _isMuted = false;
            }
            else
            {
                _isMuted = true;
            }

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = volume;
            }

            if (TxtVolumePercent != null)
            {
                TxtVolumePercent.Text = $"{volume}%";
            }

            UpdateVolumeButtonIcon();
        }

        private void SliderVolume_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = e.Delta > 0 ? 5 : -5;
            SliderVolume.Value = Math.Max(SliderVolume.Minimum, Math.Min(SliderVolume.Maximum, SliderVolume.Value + step));
            e.Handled = true;
        }

        private void BtnMuteVolume_Click(object sender, RoutedEventArgs e)
        {
            if (SliderVolume.Value > 0 && !_isMuted)
            {
                _lastVolumeBeforeMute = (int)SliderVolume.Value;
                _isMuted = true;
                SliderVolume.Value = 0;
            }
            else
            {
                _isMuted = false;
                int restoreVolume = _lastVolumeBeforeMute > 0 ? _lastVolumeBeforeMute : 80;
                SliderVolume.Value = Math.Max(SliderVolume.Minimum, Math.Min(SliderVolume.Maximum, restoreVolume));
            }

            UpdateVolumeButtonIcon();
        }

        private void BtnRewind10_Click(object sender, RoutedEventArgs e)
        {
            SeekByMilliseconds(-10000);
        }

        private void BtnForward10_Click(object sender, RoutedEventArgs e)
        {
            SeekByMilliseconds(10000);
        }

        private void BtnNewSequence_Click(object sender, RoutedEventArgs e)
        {
            _currentPlaylistId = null;
            _currentPlaylistName = string.Empty;
            ComboSavedSequences.SelectedItem = null;
            _sequence.Clear();
            RefreshSequencePositions();
            TxtNowPlaying.Text = "Nova sequência criada. Adicione músicas e salve com um nome.";
            TxtStatus.Text = "Nova sequência";
        }

        private void BtnSaveSequence_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPlaylistId == null || string.IsNullOrWhiteSpace(_currentPlaylistName))
            {
                SaveSequenceAs();
                return;
            }

            SaveSequence(_currentPlaylistName, _currentPlaylistId);
        }

        private void BtnSaveSequenceAs_Click(object sender, RoutedEventArgs e)
        {
            SaveSequenceAs();
        }

        private void SaveSequenceAs()
        {
            PlaylistNameWindow window = new(_currentPlaylistName)
            {
                Owner = this
            };

            if (window.ShowDialog() != true)
            {
                return;
            }

            SaveSequence(window.PlaylistName, null);
        }

        private void SaveSequence(string name, int? playlistId)
        {
            if (_sequence.Count == 0)
            {
                System.Windows.MessageBox.Show("Adicione pelo menos uma música na sequência antes de salvar.", "AlphaPlay");
                return;
            }

            PlaylistInfo saved = DatabaseService.SavePlaylist(name, _sequence.Select(item => item.Music), playlistId);
            _currentPlaylistId = saved.Id;
            _currentPlaylistName = saved.Name;
            LoadSavedPlaylists(saved.Id);
            System.Windows.MessageBox.Show($"Sequência '{saved.Name}' salva com sucesso.", "AlphaPlay");
        }

        private void BtnLoadSequence_Click(object sender, RoutedEventArgs e)
        {
            if (ComboSavedSequences.SelectedItem is not PlaylistInfo selectedPlaylist)
            {
                System.Windows.MessageBox.Show("Selecione uma sequência salva para carregar.", "AlphaPlay");
                return;
            }

            LoadPlaylist(selectedPlaylist);
        }

        private void LoadPlaylist(PlaylistInfo playlist)
        {
            _sequence.Clear();

            foreach (MusicFile music in DatabaseService.LoadPlaylistItems(playlist.Id))
            {
                _sequence.Add(new SequenceItem { Music = music });
            }

            _currentPlaylistId = playlist.Id;
            _currentPlaylistName = playlist.Name;
            RefreshSequenceMissingStatus();
            int missingCount = CountMissingSequenceItems();
            TxtNowPlaying.Text = $"Sequência carregada: {playlist.Name}";
            TxtStatus.Text = missingCount > 0
                ? $"Sequência carregada com {missingCount} arquivo(s) não encontrado(s)"
                : "Sequência carregada";
        }

        private void BtnDeleteSequence_Click(object sender, RoutedEventArgs e)
        {
            if (ComboSavedSequences.SelectedItem is not PlaylistInfo selectedPlaylist)
            {
                System.Windows.MessageBox.Show("Selecione uma sequência salva para excluir.", "AlphaPlay");
                return;
            }

            System.Windows.MessageBoxResult result = System.Windows.MessageBox.Show(
                $"Deseja excluir a sequência '{selectedPlaylist.Name}'?",
                "Excluir sequência",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            DatabaseService.DeletePlaylist(selectedPlaylist.Id);

            if (_currentPlaylistId == selectedPlaylist.Id)
            {
                _currentPlaylistId = null;
                _currentPlaylistName = string.Empty;
                _sequence.Clear();
                RefreshSequencePositions();
            }

            LoadSavedPlaylists();
        }

        private void ComboSavedSequences_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // A seleção apenas escolhe qual sequência será carregada/excluída.
            // O carregamento acontece no botão "Carregar" para evitar trocar a lista sem querer.
        }


        private void BtnFullscreenVideo_Click(object sender, RoutedEventArgs e)
        {
            if (_fullscreenWindowVisible)
            {
                HideFullscreenVideo();
                return;
            }

            if (_currentMusic == null || !IsVideoFile(_currentMusic))
            {
                System.Windows.MessageBox.Show("A tela cheia é usada para arquivos de vídeo, como MP4.", "AlphaPlay");
                return;
            }

            OpenFullscreenVideo();
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            RemoteControlManager.ClearPlayerController();
            _timer.Stop();
            DestroyFullscreenVideo();

            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Dispose();
            }

            _libVLC?.Dispose();
        }
    }
}
