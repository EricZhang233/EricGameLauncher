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
        DebugPaths.ApplyIfDebug();
        if (args.Length == 2 && args[0] == "--jump-launch")
        {
            ConfigService.Initialize();
            JumpListService.Initialize();
            var item = ConfigService.LoadItems().FirstOrDefault(x => x.Id == args[1] && x.Status == 0);
            int code = item != null && LaunchService.Launch(item, item.UseAlternativeLaunch).Success ? 0 : 1;
            JumpListService.FlushPending();
            LogService.FlushAndStop();
            return code;
        }

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
        JumpListService.FlushPending();
        LogService.FlushAndStop();
        return 0;
    }
}
