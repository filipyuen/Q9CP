using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Serilog;
using System;

namespace Q9CS_CrossPlatform
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .WriteTo.File("logs/app.log")
                .CreateLogger();
            try
            {
                Log.Information("Starting application...");
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                Log.Information("Application started successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Application failed to start.");
                throw; // Keep for debugging
            }
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}