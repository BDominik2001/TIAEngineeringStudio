namespace TiaStudio.Desktop.Services
{
    /// <summary>Fájlválasztók és megerősítések; a ViewModel nem hivatkozik közvetlenül ablakokra.</summary>
    public interface IDialogService
    {
        string OpenFile(string title, string filter);

        string SaveFile(string title, string filter, string defaultName);

        bool Confirm(string title, string message);

        void ShowError(string title, string message);
    }

    public sealed class WpfDialogService : IDialogService
    {
        public string OpenFile(string title, string filter)
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public string SaveFile(string title, string filter, string defaultName)
        {
            Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        public bool Confirm(string title, string message)
        {
            return System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No)
                == System.Windows.MessageBoxResult.Yes;
        }

        public void ShowError(string title, string message)
        {
            System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
