using System.Reflection;
using Avalonia.Controls;

namespace StudioLog
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v2.1.1";
        }
    }
}
