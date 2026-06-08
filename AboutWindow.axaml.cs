using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = v != null ? $"Version {v.Major}.{v.Minor}.{v.Build}" : "Version 2.1.2";
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
