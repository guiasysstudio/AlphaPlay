using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using WinForms = System.Windows.Forms;

namespace AlphaPlay
{
    public partial class FullscreenVideoWindow : Window
    {
        private readonly DispatcherTimer _hintTimer;
        private bool _isCloseRequested = false;

        private const int SWP_NOZORDER = 0x0004;
        private const int SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        public event EventHandler? RequestCloseFullscreen;

        public FullscreenVideoWindow()
        {
            InitializeComponent();

            _hintTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _hintTimer.Tick += (_, _) =>
            {
                HintPanel.Visibility = Visibility.Collapsed;
                _hintTimer.Stop();
            };
        }

        public void AttachPlayer(MediaPlayer mediaPlayer)
        {
            if (!ReferenceEquals(FullscreenVideoView.MediaPlayer, mediaPlayer))
            {
                FullscreenVideoView.MediaPlayer = mediaPlayer;
            }
        }

        public void DetachPlayer()
        {
            FullscreenVideoView.MediaPlayer = null;
        }

        public void ShowOnScreen(WinForms.Screen screen)
        {
            _isCloseRequested = false;
            HintPanel.Visibility = Visibility.Visible;

            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            if (!IsVisible)
            {
                Show();
            }

            FitToScreen(screen);
            Activate();
            Focus();
            Keyboard.Focus(this);
            _hintTimer.Stop();
            _hintTimer.Start();
        }

        private void FitToScreen(WinForms.Screen screen)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    screen.Bounds.Left,
                    screen.Bounds.Top,
                    screen.Bounds.Width,
                    screen.Bounds.Height,
                    SWP_NOZORDER | SWP_SHOWWINDOW);
            }
        }

        private void RequestClose()
        {
            if (_isCloseRequested)
            {
                return;
            }

            _isCloseRequested = true;
            Dispatcher.BeginInvoke(() => RequestCloseFullscreen?.Invoke(this, EventArgs.Empty));
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.F)
            {
                e.Handled = true;
                RequestClose();
            }
        }

        private void Window_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            RequestClose();
        }
    }
}
