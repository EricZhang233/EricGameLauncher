using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;
using Windows.UI.ViewManagement;

namespace EricGameLauncher
{
    public sealed partial class MainWindow : Window
    {
        private bool _startupOverlayLoaded = false;
        private bool _closed;
        private bool _closeApproved;
        private Task<bool>? _saveConfirmation;
        private readonly System.Threading.CancellationTokenSource _lifetime = new();
        private bool _startupDataLoaded = false;
        private bool _startupUiLoaded = false;
        private bool _startupOverlayHidden = false;
        private bool _startupHidePending = false;
        private Storyboard? _startupFadeInStoryboard;
        private TaskCompletionSource<bool>? _startupFadeInCompletion;
        private MainView? _mainView;
        private readonly UISettings _uiSettings = new();
        private readonly Stopwatch _startupOverlayStopwatch = Stopwatch.StartNew();

        private IntPtr _hWnd = IntPtr.Zero;
        private IntPtr _customIconHandle = IntPtr.Zero;
        private bool _startupComplete = false;
        private TaskCompletionSource<bool>? _startupCompletion;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _warmupHideTimer;
        private const int WarmupOffscreenCoordinate = -32000;

        public event Action? StartupComplete;
        public bool IsStartupComplete => _startupComplete;
        public bool WarmupMode { get; private set; } = false;

        public MainWindow()
        {
            InitializeComponent();
            PrepareWindowForActivation();
        }

        private void PrepareWindowForActivation()
        {
            try
            {
                this.SystemBackdrop = new DesktopAcrylicBackdrop();
                LogService.Write("UI", "Desktop acrylic system backdrop applied");
                this.ExtendsContentIntoTitleBar = true;

                this.Title = ConfigService.AppTitle;

                var titleBar = this.AppWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

                RestoreWindowState();

                _hWnd = WindowNative.GetWindowHandle(this);
                ApplyWindowIcon();
                this.Closed += Window_Closing_Cleanup;
                this.AppWindow.Closing += async (_, e) =>
                {
                    if (_closeApproved || WarmupMode) return;
                    e.Cancel = true;
                    if (await ConfirmSaveBeforeCloseAsync() && !_closed && !_closeApproved) { _closeApproved = true; Close(); }
                };
                LogService.Write("Startup", "Pre-activation window config and state applied");
            }
            catch (Exception ex) { LogService.Write("Startup", "PrepareWindowForActivation failed", ex); }
        }

        private void ApplyWindowIcon()
        {
            try
            {
                IntPtr icon = AppIconService.GetCustomIconHandle();
                if (icon == IntPtr.Zero) icon = AppIconService.GetDefaultIconHandle();
                if (icon == IntPtr.Zero)
                {
                    LogService.Write("App", "SetAppIcon no icon handle available");
                    return;
                }
                if (_hWnd == IntPtr.Zero)
                {
                    DestroyIcon(icon);
                    return;
                }
                SendMessage(_hWnd, WM_SETICON, ICON_BIG, icon);
                SendMessage(_hWnd, WM_SETICON, ICON_SMALL, icon);
                if (_customIconHandle != IntPtr.Zero) DestroyIcon(_customIconHandle);
                _customIconHandle = icon;
                LogService.Write("App", "Pre-activation window icon applied");
            }
            catch (Exception ex) { LogService.Write("App", "SetAppIcon failed", ex); }
        }

        private void Window_Closing_Cleanup(object sender, WindowEventArgs args)
        {
            _closed = true;
            _lifetime.Cancel();
            _startupFadeInStoryboard?.Stop();
            _startupFadeInCompletion?.TrySetResult(false);
            _startupCompletion?.TrySetResult(false);
            if (_customIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_customIconHandle);
                _customIconHandle = IntPtr.Zero;
            }
            if (_warmupHideTimer != null)
            {
                try { _warmupHideTimer.Stop(); } catch { }
                _warmupHideTimer = null;
            }
        }

        public void PrepareOffscreenWarmup()
        {
            WarmupMode = true;
            try
            {
                WindowStyleHelper.HideFromTaskbar(_hWnd);
                this.AppWindow.Move(new Windows.Graphics.PointInt32(WarmupOffscreenCoordinate, WarmupOffscreenCoordinate));
                this.Activate();
                this.AppWindow.Hide();
                ScheduleWarmupHide();
                LogService.Write("Startup", "Warmup window activated offscreen and hidden");
            }
            catch (Exception ex) { LogService.Write("Startup", "Warmup window preparation failed", ex); }
        }

        private void ScheduleWarmupHide()
        {
            try
            {
                _warmupHideTimer = DispatcherQueue.CreateTimer();
                _warmupHideTimer.Interval = TimeSpan.FromMilliseconds(600);
                _warmupHideTimer.IsRepeating = false;
                _warmupHideTimer.Tick += (sender, args) =>
                {
                    try
                    {
                        sender.Stop();
                        this.AppWindow.Hide();
                        LogService.Write("Startup", "Warmup window re-hidden after deferred show");
                    }
                    catch (Exception ex) { LogService.Write("Startup", "Warmup re-hide failed", ex); }
                };
                _warmupHideTimer.Start();
            }
            catch (Exception ex) { LogService.Write("Startup", "ScheduleWarmupHide failed", ex); }
        }

        public Task<bool> WaitStartupCompleteAsync(TimeSpan timeout)
        {
            if (_startupComplete) return Task.FromResult(true);
            _startupCompletion ??= new TaskCompletionSource<bool>();
            return WaitWithTimeoutAsync(_startupCompletion.Task, timeout);
        }

        private static async Task<bool> WaitWithTimeoutAsync(Task<bool> task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed != task) return false;
            return await task;
        }

        private void MarkStartupComplete()
        {
            if (_startupComplete) return;
            _startupComplete = true;
            try { _startupCompletion?.TrySetResult(true); } catch { }
            try { StartupComplete?.Invoke(); } catch (Exception ex) { LogService.Write("Startup", "StartupComplete handler failed", ex); }
            LogService.Write("Startup", $"Main window startup complete duration={_startupOverlayStopwatch.ElapsedMilliseconds}ms");
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint WM_SETICON = 0x0080;
        private static readonly IntPtr ICON_BIG = (IntPtr)1;
        private static readonly IntPtr ICON_SMALL = (IntPtr)0;

        private void RestoreWindowState()
        {
            try
            {
                var (x, y, width, height) = ConfigService.GetWindowBounds();

                if (width > 0 && height > 0)
                {
                    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
                }
                else
                {
                    this.AppWindow.Resize(new Windows.Graphics.SizeInt32(950, 650));
                }

                if (x != -1 || y != -1)
                {
                    var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
                        new Windows.Graphics.RectInt32(x, y, Math.Max(400, width), Math.Max(300, height)), Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);

                    var work = displayArea.WorkArea;
                    int workLeft = work.X;
                    int workTop = work.Y;
                    int workRight = work.X + work.Width;
                    int workBottom = work.Y + work.Height;

                    int targetX = Math.Clamp(x, workLeft - Math.Max(0, width - 100), Math.Max(workRight - 100, workLeft));
                    int targetY = Math.Clamp(y, workTop, Math.Max(workBottom - 100, workTop));

                    this.AppWindow.Move(new Windows.Graphics.PointInt32(targetX, targetY));
                }
            }
            catch (Exception ex)
            {
                LogService.Write("App", "MainWindow Move/Resize failed", ex);
                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(950, 650));
            }
        }

        public void SaveWindowState(Microsoft.UI.Windowing.AppWindowChangedEventArgs? args)
        {
            using (LogService.StartOperation("App", "SaveWindowState"))
            {
                try
                {
                    LogService.Write("App", $"SaveWindowState Start args={{didSize={args?.DidSizeChange}, didPos={args?.DidPositionChange}}}");
                    var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                    if (presenter != null && (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized ||
                                             presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized))
                    {
                        return;
                    }

                    var current = ConfigService.GetWindowBounds();
                    int x = current.X, y = current.Y, width = current.Width, height = current.Height;

                    var size = this.AppWindow.Size;
                    var position = this.AppWindow.Position;

                    bool changed = false;
                    if (args == null || args.DidSizeChange)
                    {
                        if (width != size.Width || height != size.Height)
                        {
                            width = size.Width;
                            height = size.Height;
                            changed = true;
                        }
                    }

                    if (args == null || args.DidPositionChange)
                    {
                        if (x != position.X || y != position.Y)
                        {
                            x = position.X;
                            y = position.Y;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        ConfigService.SetWindowBounds(x, y, width, height);
                        LogService.Write("App", $"SaveWindowState persisted newBounds x={x} y={y} w={width} h={height}");
                    }
                }
                catch (Exception ex) { LogService.Write("App", "SaveWindowState failed", ex); }
            }
        }

        public void StartInitialization()
        {
            DispatcherQueue.TryEnqueue(StartMainView);
        }

        private void StartMainView()
        {
            if (_closed) return;
            StartStartupOverlayFadeIn();
            if (WarmupMode)
            {
                BeginMainView();
                return;
            }
            _ = BeginMainViewAfterFirstFrameAsync();
        }

        private async Task BeginMainViewAfterFirstFrameAsync()
        {
            bool painted;
            try
            {
                painted = await WaitForFirstFrameAsync();
            }
            catch (Exception ex)
            {
                painted = false;
                LogService.Write("Startup", "First frame wait failed", ex);
            }
            if (_closed) return;
            LogService.Write("Startup", painted
                ? $"Splash first frame rendered at {_startupOverlayStopwatch.ElapsedMilliseconds}ms"
                : $"Splash first frame wait timed out at {_startupOverlayStopwatch.ElapsedMilliseconds}ms");

            await WaitForStartupFadeInAsync();
            if (_closed) return;
            BeginMainView();
        }

        private async Task WaitForStartupFadeInAsync()
        {
            var completion = _startupFadeInCompletion;
            if (completion == null) return;
            try { await completion.Task; }
            catch (Exception ex) { LogService.Write("Startup", "Splash fade-in wait failed", ex); }
        }

        private void BeginMainView()
        {
            if (_closed || _mainView != null) return;
            try
            {
                var main = new MainView { Opacity = 0, IsEnabled = false };
                _mainView = main;
                RootGrid.Children.Insert(0, main);
                main.Start(this);
                LogService.Write("Startup", $"Main view created at {_startupOverlayStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) { LogService.Write("Startup", "BeginMainView failed", ex); }
            _ = EnforceStartupOverlayTimeoutAsync();
        }

        private async Task<bool> WaitForFirstFrameAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            var timer = DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            timer.IsRepeating = false;

            void Cleanup()
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
                try { timer.Stop(); } catch { }
            }

            void OnRendering(object? sender, object args)
            {
                Cleanup();
                tcs.TrySetResult(true);
            }

            void OnTimeout(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
            {
                Cleanup();
                tcs.TrySetResult(false);
            }

            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
            timer.Tick += OnTimeout;
            timer.Start();
            using var registration = _lifetime.Token.Register(() => { Cleanup(); tcs.TrySetResult(false); });
            return await tcs.Task;
        }

        public Task<bool> ConfirmSaveBeforeCloseAsync() => _saveConfirmation ??= ConfirmAndResetAsync();

        private async Task<bool> ConfirmAndResetAsync()
        {
            await Task.Yield();
            try
            {
                return await ExitSaveController.ConfirmAsync(ConfigService.SaveAll, async error =>
                {
                    if (_closed) return ExitSaveDecision.Stay;
                    ActivateAndFocus();
                    var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = Text.T("Exit_SaveFailed"),
                        Content = error.Message, PrimaryButtonText = Text.T("Exit_Retry"), SecondaryButtonText = Text.T("Exit_Discard"),
                        CloseButtonText = Text.T("Exit_Stay"), DefaultButton = ContentDialogButton.Close };
                    var result = await dialog.ShowAsync();
                    return result == ContentDialogResult.Primary ? ExitSaveDecision.Retry : result == ContentDialogResult.Secondary ? ExitSaveDecision.Discard : ExitSaveDecision.Stay;
                });
            }
            catch (Exception ex) { LogService.Write("App", "Exit confirmation failed; window retained", ex); return false; }
            finally { _saveConfirmation = null; }
        }

        public void ActivateAndFocus()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    WindowActivator.Activate(WindowNative.GetWindowHandle(this));
                });
            }
            catch (Exception ex) { LogService.Write("App", "MainWindow ActivateAndFocus failed", ex); }
        }

        public void SetSplashIcon()
        {
            _ = ApplySplashIconAsync();
        }

        private bool _splashIconApplied;

        private async Task ApplySplashIconAsync()
        {
            if (_splashIconApplied || StartupOverlayIcon.Source != null) return;
            _splashIconApplied = true;
            try
            {
                var bitmap = await AppIconService.LoadBitmapImageAsync();
                if (_closed) return;
                if (bitmap == null)
                {
                    _splashIconApplied = false;
                    LogService.Write("Startup", "Splash icon unavailable");
                    return;
                }
                StartupOverlayIcon.Source = bitmap;
                LogService.Write("Startup", "Splash icon applied");
            }
            catch (Exception ex)
            {
                _splashIconApplied = false;
                LogService.Write("Startup", "Splash icon apply failed", ex);
            }
        }

        public void MarkDataReady()
        {
            _startupDataLoaded = true;
            TryHideStartupOverlay();
        }

        public void MarkUiReady()
        {
            _startupUiLoaded = true;
            TryHideStartupOverlay();
        }

        private void StartupOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            _startupOverlayLoaded = true;
            SetSplashIcon();
            TryHideStartupOverlay();
        }

        private void StartStartupOverlayFadeIn()
        {
            if (_startupFadeInCompletion != null) return;
            var completion = new TaskCompletionSource<bool>();
            _startupFadeInCompletion = completion;
            try
            {
                if (WarmupMode)
                {
                    StartupOverlay.Opacity = 1;
                    completion.TrySetResult(true);
                    LogService.Write("Startup", "Warmup window splash shown without fade-in");
                    return;
                }

                if (!_uiSettings.AnimationsEnabled)
                {
                    StartupOverlay.Opacity = 1;
                    completion.TrySetResult(true);
                    LogService.Write("Startup", "Splash fade-in skipped by system animation preference");
                    return;
                }

                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(fadeIn, StartupOverlay);
                Storyboard.SetTargetProperty(fadeIn, "Opacity");
                storyboard.Children.Add(fadeIn);
                storyboard.Completed += (sender, args) =>
                {
                    storyboard.Stop();
                    _startupFadeInStoryboard = null;
                    StartupOverlay.Opacity = 1;
                    completion.TrySetResult(true);
                    LogService.Write("Startup", $"Splash fade-in finished at {_startupOverlayStopwatch.ElapsedMilliseconds}ms");
                    if (_startupHidePending)
                    {
                        _startupHidePending = false;
                        StartStartupOverlayFadeOut();
                    }
                };
                _startupFadeInStoryboard = storyboard;
                storyboard.Begin();
                LogService.Write("Startup", $"Splash fade-in started duration=200ms at={_startupOverlayStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                StartupOverlay.Opacity = 1;
                completion.TrySetResult(true);
                LogService.Write("Startup", "Splash fade-in failed", ex);
            }
        }

        private void TryHideStartupOverlay()
        {
            try
            {
                if (_startupOverlayHidden)
                    return;

                bool startupReady = _startupOverlayLoaded && _startupDataLoaded && _startupUiLoaded;
                bool startupTimedOut = _startupOverlayStopwatch.Elapsed >= TimeSpan.FromSeconds(10);
                if (!startupReady && !startupTimedOut)
                    return;

                _startupOverlayHidden = true;
                StartupOverlay.IsHitTestVisible = false;
                LogService.Write("Startup", startupTimedOut && !startupReady
                    ? $"Startup overlay hidden by timeout after {_startupOverlayStopwatch.ElapsedMilliseconds}ms data={_startupDataLoaded} ui={_startupUiLoaded} loaded={_startupOverlayLoaded}"
                    : $"Startup overlay hidden after {_startupOverlayStopwatch.ElapsedMilliseconds}ms");

                if (WarmupMode)
                {
                    CompleteStartupOverlay();
                    LogService.Write("Startup", "Warmup window overlay collapsed without animation");
                    return;
                }

                if (_startupFadeInStoryboard != null)
                {
                    _startupHidePending = true;
                    LogService.Write("Startup", "Startup overlay fade-out deferred until splash fade-in finishes");
                    return;
                }

                StartStartupOverlayFadeOut();
            }
            catch (Exception ex) { LogService.Write("Startup", "TryHideStartupOverlay failed", ex); }
        }

        private void StartStartupOverlayFadeOut()
        {
            try
            {
                if (!_uiSettings.AnimationsEnabled)
                {
                    CompleteStartupOverlay();
                    LogService.Write("Startup", "Startup overlay hidden without fade-out by system animation preference");
                    return;
                }

                var duration = TimeSpan.FromMilliseconds(500);
                var gridDelay = TimeSpan.FromTicks(duration.Ticks * 2 / 3);
                var fadeOut = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(duration)
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(fadeOut, StartupOverlay);
                Storyboard.SetTargetProperty(fadeOut, "Opacity");
                storyboard.Children.Add(fadeOut);
                if (_mainView != null)
                {
                    var gridFadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        BeginTime = gridDelay,
                        Duration = new Duration(duration - gridDelay)
                    };
                    Storyboard.SetTarget(gridFadeIn, _mainView);
                    Storyboard.SetTargetProperty(gridFadeIn, "Opacity");
                    storyboard.Children.Add(gridFadeIn);
                }
                storyboard.Completed += (sender, args) =>
                {
                    storyboard.Stop();
                    CompleteStartupOverlay();
                };
                storyboard.Begin();
                LogService.Write("Startup", $"Startup overlay fade-out started duration={duration.TotalMilliseconds}ms gridFadeInDelay={gridDelay.TotalMilliseconds:F1}ms at={_startupOverlayStopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                LogService.Write("Startup", "Startup overlay fade-out failed", ex);
                CompleteStartupOverlay();
            }
        }

        private void CompleteStartupOverlay()
        {
            StartupOverlay.Opacity = 0;
            StartupOverlay.Visibility = Visibility.Collapsed;
            if (_mainView != null)
            {
                _mainView.Opacity = 1;
                _mainView.IsEnabled = true;
            }
            LogService.Write("Startup", "Splash finished; main view revealed over transparent backdrop");
            MarkStartupComplete();
        }

        private async Task EnforceStartupOverlayTimeoutAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            if (_closed) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed && !_startupOverlayHidden)
                {
                    LogService.Write("Startup", "Startup overlay timeout reached, forcing main interface");
                    TryHideStartupOverlay();
                }
            });
        }
    }
}
