using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace StudioLog
{
    class Program
    {
        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [STAThread]
        public static void Main(string[] args)
        {
#if DEBUG
            // Debug console — only in development builds
            AllocConsole();
            Console.WriteLine("=== StudioLog Debug Console ===");
            Console.WriteLine("Console output enabled for diagnostics");
            Console.WriteLine();
#endif
            
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
