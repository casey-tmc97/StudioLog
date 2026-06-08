using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace StudioLog
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            // CRITICAL FIX: Configure ReactiveUI to use Avalonia's dispatcher
            RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;

            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var splash = new SplashWindow();
                desktop.MainWindow = splash;
                splash.Show();

                var mainWindow = new MainWindow();
                await Task.Delay(2000);

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                splash.Close();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
