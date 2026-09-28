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
            SystemGuard.EnsureSupported();
            this.InitializeComponent();
            this.UnhandledException += App_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            QuickStartService.EnabledChanged += OnQuickStartEnabledChanged;
            try { LogService.Write("App", "App constructed"); } catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            using (LogService.StartOperation("Startup", "OnLaunched"))
            {
                try
                {
                    LogService.Write("Startup", "OnLaunched start");
                    ConfigService.Initialize();
                    QuickStartService.SyncAutoStart();

                    if (StartupArgs.IsServiceStart)
                    {
                        if (!ConfigService.QuickStart)
                        {
                            LogService.Write("QuickStart", "Service start requested while quick start is disabled, exiting");
                            QuickStartService.Unregister();
                            Environment.Exit(0);
                            return;
                        }

                        LogService.Write("QuickStart", "OnLaunched entering service host mode");
                        if (!EnterServiceHostMode(true))
                        {
                            LogService.Write("QuickStart", "Another service host is already running, exiting");
                            Environment.Exit(0);
                        }
                        return;
                    }

                    if (!SingleInstance.TryAcquire())
                    {
                        LogService.Write("Startup", "Another GUI instance is running, notifying and exiting");
                        WindowActivator.AllowAnyForegroundWindow();
                        SingleInstance.NotifyRunningInstance();
                        Environment.Exit(0);
                        return;
                    }

                    if (ConfigService.QuickStart)
                        EnterServiceHostMode(false);

                    ShowMainWindow(true);
                    SingleInstance.StartServer(() => ShowMainWindow(true));
                    LogService.Write("Startup", "OnLaunched complete");
                }
                catch (Exception ex)
                {
                    LogService.Write("Startup", "OnLaunched failed", ex);
                    throw;
                }
            }
        }

        private bool EnterServiceHostMode(bool warmup)
        {
            if (m_serviceHost) return true;
            if (!ServiceHost.TryAcquireHost())
            {
                LogService.Write("QuickStart", $"Service host owned by another process, warmup={warmup}");
                return false;
            }

            m_serviceHost = true;
            EnsureKeeperWindow();
            ServiceHost.StartServer(OnWakeRequested, OnExitRequested);
            LogService.Write("QuickStart", $"Service host mode entered warmup={warmup}");
            if (warmup) _ = RunWarmupAsync();
            _ = RunHostWatchdogAsync();
            return true;
        }

        private async Task RunHostWatchdogAsync()
        {
            while (m_serviceHost)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                try
                {
                    if (!m_serviceHost) return;
                    if (m_window != null) continue;

                    string settingsFile = ConfigService.SettingsFilePath;
                    DateTime stamp = File.Exists(settingsFile) ? File.GetLastWriteTimeUtc(settingsFile) : DateTime.MinValue;
                    if (m_settingsStamp == stamp) continue;
                    m_settingsStamp = stamp;

                    ConfigService.Reload();
                    if (ConfigService.QuickStart)
                    {
                        QuickStartService.SyncAutoStart();
                        continue;
                    }

                    LogService.Write("QuickStart", "Quick start disabled by external change, shutting down service host");
                    ServiceHost.StopServer();
                    m_serviceHost = false;
                    CloseKeeperWindow();
                    return;
                }
                catch (Exception ex) { LogService.Write("QuickStart", "Service host watchdog failed", ex); }
            }
        }

        private void OnWakeRequested()
        {
            try
            {
                var queue = (m_window as Window)?.DispatcherQueue ?? m_keeper?.DispatcherQueue;
                if (queue == null)
                {
                    LogService.Write("QuickStart", "Wake request dropped, no dispatcher available", null, null, LogService.LogLevel.Error);
                    return;
                }
                queue.TryEnqueue(() => ShowMainWindow(true));
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Wake dispatch failed", ex); }
        }

        private void OnExitRequested()
        {
            try
            {
                var queue = (m_window as Window)?.DispatcherQueue ?? m_keeper?.DispatcherQueue;
                if (queue == null)
                {
                    LogService.Write("QuickStart", "Exit request dropped, no dispatcher available", null, null, LogService.LogLevel.Error);
                    return;
                }
                queue.TryEnqueue(RequestFullExit);
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Exit dispatch failed", ex); }
        }

        public void RequestFullExit()
        {
            using (LogService.StartOperation("App", "RequestFullExit"))
            {
                try
                {
                    LogService.Write("App", $"Full exit requested quickStart={ConfigService.QuickStart} host={m_serviceHost} window={m_window != null}");
                    ConfigService.SaveAll();
                    ServiceHost.StopServer();
                    m_serviceHost = false;
                    CloseKeeperWindow();
                }
                catch (Exception ex) { LogService.Write("App", "Full exit cleanup failed", ex); }

                try { Exit(); }
                catch (Exception ex)
                {
                    LogService.Write("App", "Full exit failed, terminating process", ex);
                    Environment.Exit(0);
                }
            }
        }

        private void ShowMainWindow(bool activate)
        {
            try
            {
                if (m_window == null)
                {
                    if (m_serviceHost) ConfigService.Reload();
                    LogService.Write("QuickStart", "Creating main window");
                    m_window = new MainWindow();
                    m_window.Closed += OnMainWindowClosed;
                    if (activate) m_window.Activate();
                    m_window.StartInitialization();
                }
                else if (activate)
                {
                    m_window.ActivateAndFocus();
                }
            }
            catch (Exception ex) { LogService.Write("Startup", "ShowMainWindow failed", ex); }
        }

        private void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            try
            {
                if (sender is MainWindow window) window.Closed -= OnMainWindowClosed;
                m_window = null;
                ConfigService.SaveAll();
                ConfigService.Reload();
                LogService.Write("QuickStart", $"Main window closed quickStart={ConfigService.QuickStart} host={m_serviceHost}");
                if (!ConfigService.QuickStart)
                {
                    ServiceHost.StopServer();
                    m_serviceHost = false;
                    CloseKeeperWindow();
                }
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Main window closed handling failed", ex); }
        }

        private async Task RunWarmupAsync()
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var window = new MainWindow();
                window.PrepareOffscreenWarmup();
                window.StartInitialization();
                bool ready = await window.WaitStartupCompleteAsync(TimeSpan.FromSeconds(30));
                await Task.Delay(500);
                window.Close();
                LogService.Write("QuickStart", $"Service host warmup finished ready={ready} duration={sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Service host warmup failed", ex); }
        }

        private void EnsureKeeperWindow()
        {
            if (m_keeper != null) return;
            try
            {
                m_keeper = new Window();
                m_keeper.Content = new Microsoft.UI.Xaml.Controls.Grid();
                WindowStyleHelper.HideFromTaskbar(WinRT.Interop.WindowNative.GetWindowHandle(m_keeper));
                m_keeper.AppWindow.Move(new Windows.Graphics.PointInt32(KeeperOffscreenCoordinate, KeeperOffscreenCoordinate));
                m_keeper.AppWindow.Hide();
                ScheduleKeeperHide();
                LogService.Write("QuickStart", "Keeper window created for resource retention");
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Keeper window creation failed", ex); }
        }

        private void ScheduleKeeperHide()
        {
            try
            {
                var queue = m_keeper?.DispatcherQueue;
                if (queue == null) return;
                m_keeperHideTimer = queue.CreateTimer();
                m_keeperHideTimer.Interval = TimeSpan.FromMilliseconds(1500);
                m_keeperHideTimer.IsRepeating = false;
                m_keeperHideTimer.Tick += (sender, args) =>
                {
                    try
                    {
                        sender.Stop();
                        m_keeper?.AppWindow.Hide();
                        LogService.Write("QuickStart", "Keeper window re-hidden after deferred show");
                    }
                    catch (Exception ex) { LogService.Write("QuickStart", "Keeper re-hide failed", ex); }
                };
                m_keeperHideTimer.Start();
            }
            catch (Exception ex) { LogService.Write("QuickStart", "ScheduleKeeperHide failed", ex); }
        }

        private void CloseKeeperWindow()
        {
            try
            {
                var keeper = m_keeper;
                m_keeper = null;
                keeper?.Close();
                LogService.Write("QuickStart", "Keeper window closed");
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Keeper window close failed", ex); }
        }

        private void OnQuickStartEnabledChanged(bool enabled)
        {
            try
            {
                LogService.Write("QuickStart", $"Enabled changed to={enabled}");
                if (enabled)
                {
                    if (!m_serviceHost) EnterServiceHostMode(false);
                    return;
                }

                if (m_window == null)
                {
                    ServiceHost.StopServer();
                    m_serviceHost = false;
                    CloseKeeperWindow();
                }
            }
            catch (Exception ex) { LogService.Write("QuickStart", "Quick start setting change failed", ex); }
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
        private MainWindow? m_window;
        private Window? m_keeper;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? m_keeperHideTimer;
        private bool m_serviceHost = false;
        private DateTime m_settingsStamp = DateTime.MinValue;
        private const int KeeperOffscreenCoordinate = -32000;
    }
}
