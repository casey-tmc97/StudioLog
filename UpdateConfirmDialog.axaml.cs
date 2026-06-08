using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class UpdateConfirmDialog : Window
    {
        public UpdateConfirmDialog(string latestTag)
        {
            InitializeComponent();
            MessageText.Text =
                $"Version {latestTag} is available.\n\n" +
                "Download and install now?\nStudioLog will close automatically.";
        }

        private void InstallButton_Click(object? sender, RoutedEventArgs e) => Close(true);
        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
