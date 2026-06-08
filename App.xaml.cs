using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;

namespace EricGameLauncher
{
    public partial class App : Application
    {
        public App()
        {
                try { StartupArgs.Parse(); DebugPaths.ApplyIfDebug(); } catch { }
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            try { LogService.Write("App", "App constructed"); } catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            using (LogService.StartOperation("Startup", "OnLaunched"))
            {
                try
                {
                    LogService.Write("Startup", "OnLaunched start");
                    m_window = new MainWindow();
                    m_window.Activate();
                    LogService.Write("Startup", "OnLaunched complete");
                }
                catch (Exception ex)
                {
                    LogService.Write("Startup", "OnLaunched failed", ex);
                    throw;
                }
            }
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try { LogService.Write("App", "UnhandledException", e.Exception); } catch { }
            e.Handled = false;
        }
        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try { LogService.Write("App", "UnobservedTaskException", e.Exception); } catch { }
        }

        private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
        {
            try { LogService.Write("App", "DomainUnhandledException", e.ExceptionObject as Exception); } catch { }
        }
        private Window? m_window;
    }
}
