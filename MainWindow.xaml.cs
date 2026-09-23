using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;

namespace EricGameLauncher
{
    public sealed partial class MainWindow : Window
    {
        private bool _startupOverlayLoaded = false;
        private bool _startupDataLoaded = false;
        private bool _startupUiLoaded = false;
        private bool _startupOverlayHidden = false;
        private readonly Stopwatch _startupOverlayStopwatch = Stopwatch.StartNew();

        private IntPtr _hWnd = IntPtr.Zero;
        private IntPtr _customIconHandle = IntPtr.Zero;

        public MainWindow()
        {
            Content = CreateSplashContent();
            PrepareWindowForActivation();
        }

        private FrameworkElement CreateSplashContent()
        {
            var background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Brush;
            return new Grid
            {
                Background = background,
                Children =
                {
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Spacing = 18,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "EricGameLauncher",
                                HorizontalAlignment = HorizontalAlignment.Center,
                                FontSize = 24,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                            },
                            new ProgressRing
                            {
                                IsActive = true,
                                Width = 24,
                                Height = 24,
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        }
                    }
                }
            };
        }

        private void PrepareWindowForActivation()
        {
            try
            {
                this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                this.ExtendsContentIntoTitleBar = true;

                ConfigService.Initialize();

                this.Title = ConfigService.AppTitle;

                var titleBar = this.AppWindow.TitleBar;
                titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
                titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

                RestoreWindowState();

                _hWnd = WindowNative.GetWindowHandle(this);
                ApplyWindowIcon();
                this.Closed += Window_Closing_Cleanup;
                LogService.Write("Startup", "Pre-activation window config and state applied");
            }
            catch (Exception ex) { LogService.Write("Startup", "PrepareWindowForActivation failed", ex); }
        }

        private void ApplyWindowIcon()
        {
            try
            {
                IntPtr hCustomIcon = AppIconService.GetCustomIconHandle();
                if (hCustomIcon != IntPtr.Zero)
                {
                    if (_hWnd != IntPtr.Zero)
                    {
                        SendMessage(_hWnd, WM_SETICON, ICON_BIG, hCustomIcon);
                        SendMessage(_hWnd, WM_SETICON, ICON_SMALL, hCustomIcon);
                        LogService.Write("App", "SetAppIcon via HICON success");
                    }
                    _customIconHandle = hCustomIcon;
                    LogService.Write("App", "Pre-activation window icon applied");
                    return;
                }

                string iconPath = AppIconService.GetIconPath();
                if (string.IsNullOrEmpty(iconPath) || !System.IO.File.Exists(iconPath))
                {
                    LogService.Write("App", "SetAppIcon no icon path available");
                    return;
                }

                if (_hWnd != IntPtr.Zero)
                {
                    IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
                    if (hIcon != IntPtr.Zero)
                    {
                        SendMessage(_hWnd, WM_SETICON, ICON_BIG, hIcon);
                        SendMessage(_hWnd, WM_SETICON, ICON_SMALL, hIcon);
                        LogService.Write("App", "SetAppIcon via file WM_SETICON success");
                    }
                    else
                    {
                        this.AppWindow.SetIcon(iconPath);
                        LogService.Write("App", "SetAppIcon fallback to AppWindow.SetIcon");
                    }
                }
                else
                {
                    this.AppWindow.SetIcon(iconPath);
                }

                LogService.Write("App", "Pre-activation window icon applied");
            }
            catch (Exception ex) { LogService.Write("App", "SetAppIcon failed", ex); }
        }

        private void Window_Closing_Cleanup(object sender, WindowEventArgs args)
        {
            if (_customIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_customIconHandle);
                _customIconHandle = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const uint WM_SETICON = 0x0080;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint LR_DEFAULTSIZE = 0x0040;
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

                if (x >= 0 && y >= 0)
                {
                    var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                        this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);

                    var work = displayArea.WorkArea;
                    int workLeft = work.X;
                    int workTop = work.Y;
                    int workRight = work.X + work.Width;
                    int workBottom = work.Y + work.Height;

                    int targetX = Math.Clamp(x, workLeft, Math.Max(workRight - 100, workLeft));
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
            this.InitializeComponent();
            Main.Start(this);
            _ = EnforceStartupOverlayTimeoutAsync();
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
            StartupOverlayIcon.Source = AppIconService.GetBitmapImage();
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
            TryHideStartupOverlay();
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

                var fadeOut = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(500))
                };
                var storyboard = new Storyboard();
                Storyboard.SetTarget(fadeOut, StartupOverlay);
                Storyboard.SetTargetProperty(fadeOut, "Opacity");
                storyboard.Children.Add(fadeOut);
                storyboard.Completed += (sender, args) =>
                {
                    StartupOverlay.Visibility = Visibility.Collapsed;
                };
                storyboard.Begin();
            }
            catch (Exception ex) { LogService.Write("Startup", "TryHideStartupOverlay failed", ex); }
        }

        private async Task EnforceStartupOverlayTimeoutAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_startupOverlayHidden)
                {
                    LogService.Write("Startup", "Startup overlay timeout reached, forcing main interface");
                    TryHideStartupOverlay();
                }
            });
        }
    }
}
