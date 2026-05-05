using System.Windows;

namespace AlphaPlay
{
    public partial class PlaylistNameWindow : Window
    {
        public string PlaylistName { get; private set; } = string.Empty;

        public PlaylistNameWindow(string initialName = "")
        {
            InitializeComponent();
            TxtPlaylistName.Text = initialName;
            TxtPlaylistName.SelectAll();
            TxtPlaylistName.Focus();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtPlaylistName.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                System.Windows.MessageBox.Show("Informe um nome para a sequência.", "AlphaPlay");
                return;
            }

            PlaylistName = name;
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
