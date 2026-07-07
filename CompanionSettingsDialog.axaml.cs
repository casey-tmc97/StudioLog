using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class CompanionSettingsDialog : Window
    {
        public bool EnabledResult { get; private set; }
        public int PortResult { get; private set; }

        public CompanionSettingsDialog(bool enabled, int port)
        {
            InitializeComponent();
            EnableCheckBox.IsChecked = enabled;
            PortTextBox.Text = port.ToString();
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortTextBox.Text, out var port) || port < 1024 || port > 65535)
            {
                ErrorText.Text = "Port must be a number between 1024 and 65535.";
                ErrorText.IsVisible = true;
                return;
            }

            EnabledResult = EnableCheckBox.IsChecked == true;
            PortResult = port;
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
