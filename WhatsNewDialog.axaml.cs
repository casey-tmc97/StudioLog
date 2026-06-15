using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class WhatsNewDialog : Window
    {
        public WhatsNewDialog(string version, IEnumerable<string> changes)
        {
            InitializeComponent();
            VersionHeader.Text = $"What's New in {version}";
            ChangeList.ItemsSource = changes;
        }

        private void OkButton_Click(object? sender, RoutedEventArgs e) => Close();
    }
}
