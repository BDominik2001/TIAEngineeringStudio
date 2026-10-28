using System.Windows;
using System.Windows.Media;

namespace TiaStudio.Desktop
{
    /// <summary>A témához illő megerősítő / hibaüzenet ablak (a Windows MessageBox helyett).</summary>
    public partial class ThemedDialog : Window
    {
        public enum Kind
        {
            Confirm,
            Error,
            Info,
        }

        private ThemedDialog()
        {
            InitializeComponent();
        }

        public static bool Show(Window owner, string title, string message, Kind kind, string primary, string secondary)
        {
            ThemedDialog dialog = new ThemedDialog
            {
                Title = title,
                Owner = owner != null && owner.IsVisible ? owner : null,
            };
            if (dialog.Owner == null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            dialog.HeadingText.Text = title;
            dialog.MessageText.Text = message;
            dialog.PrimaryButton.Content = primary;
            if (secondary == null)
            {
                dialog.SecondaryButton.Visibility = Visibility.Collapsed;
                dialog.PrimaryButton.IsCancel = true;
            }
            else
            {
                dialog.SecondaryButton.Content = secondary;
            }
            switch (kind)
            {
                case Kind.Error:
                    dialog.IconCircle.Fill = (Brush)dialog.FindResource("Brush.Danger");
                    dialog.IconGlyph.Text = "×";
                    break;
                case Kind.Info:
                    dialog.IconCircle.Fill = (Brush)dialog.FindResource("Brush.TealStrong");
                    dialog.IconGlyph.Text = "i";
                    break;
                default:
                    dialog.IconGlyph.Text = "?";
                    break;
            }
            return dialog.ShowDialog() == true;
        }

        private void OnPrimary(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnSecondary(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
