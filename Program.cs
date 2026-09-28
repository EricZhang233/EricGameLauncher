using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EricGameLauncher;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        StartupArgs.Parse();

        if (!StartupArgs.IsServiceStart)
        {
            if (ServiceHost.IsHostRunning())
            {
                WindowActivator.AllowAnyForegroundWindow();
                if (ServiceHost.NotifyWake())
                {
                    LogService.Write("QuickStart", "Wake signal handed to service host, launcher process exits");
                    LogService.FlushAndStop();
                    return 0;
                }
            }

            if (!SingleInstance.TryAcquire())
            {
                WindowActivator.AllowAnyForegroundWindow();
                SingleInstance.NotifyRunningInstance();
                LogService.FlushAndStop();
                return 0;
            }
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
