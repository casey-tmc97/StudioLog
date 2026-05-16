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

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
