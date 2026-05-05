using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace AlphaPlay
{
    public partial class QrCodeWindow : Window
    {
        private readonly string _link;

        public QrCodeWindow(string link)
        {
            InitializeComponent();
            _link = link;
            TxtQrLink.Text = link;
            ImgQrCode.Source = CreateQrCodeImage(link);
        }

        private static BitmapImage CreateQrCodeImage(string text)
        {
            using QRCodeGenerator generator = new();
            using QRCodeData data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            PngByteQRCode qrCode = new(data);
            byte[] imageBytes = qrCode.GetGraphic(20);

            BitmapImage image = new();
            using MemoryStream stream = new(imageBytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private void BtnCopyLink_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Clipboard.SetText(_link);
            System.Windows.MessageBox.Show("Link copiado para a área de transferência.", "AlphaPlay", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
