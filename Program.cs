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

        if (!SingleInstance.TryAcquire())
        {
            WindowActivator.AllowAnyForegroundWindow();
            SingleInstance.NotifyRunningInstance();
            return 0;
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
