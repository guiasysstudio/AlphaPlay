using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AlphaPlay.Models
{
    public class SequenceItem : INotifyPropertyChanged
    {
        private bool _isCurrent;
        private bool _isPaused;

        public MusicFile Music { get; set; } = new();
        public int Position { get; set; }
        public bool CanMoveUp { get; set; }
        public bool CanMoveDown { get; set; }

        public string Title => Music.Title;
        public string FileName => Music.FileName;
        public string FilePath => Music.FilePath;
        public string Extension => Music.Extension;
        public bool IsMissing => Music.IsMissing;
        public string MissingMarker => IsMissing ? "⚠" : string.Empty;
        public string MissingToolTip => IsMissing ? "Arquivo não encontrado. Verifique se o arquivo foi apagado, movido ou renomeado." : string.Empty;
        public string DisplayPosition => Position.ToString("00") + ".";
        public string PlayMarker => IsCurrent ? (IsPaused ? "❚❚" : "▶") : string.Empty;

        public bool IsCurrent
        {
            get => _isCurrent;
            set
            {
                if (_isCurrent == value)
                {
                    return;
                }

                _isCurrent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayMarker));
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused == value)
                {
                    return;
                }

                _isPaused = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PlayMarker));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
