using EricGameLauncher;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace EricGameLauncher
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ObservableCollection<AppItem> _allItems = new();
        private ObservableCollection<AppItem> _recycleItems = new();
        private ObservableCollection<AppItem> _viewItems = new();
        private ObservableCollection<AnnouncementListItem> _announcementItems = new();
        private AppItem? _currentEditingItem = null;
        private bool _isNewItemMode = false;
        private ObservableCollection<AppItem>? _tempOrderCollection;


        private Slider? _sizeSlider;
        private ListView? _orderItemsControl;
        private StackPanel[] _customSections;
        private TextBox[] _customTitles;
        private TextBox[] _customCommands;
        private CheckBox[] _customAdmins;
        private DropDownButton[] _customBrowses;
        private TextBlock[] _customAdminLabels;
        private TextBlock[] _customSlotLabels;
        private Task<List<ShortcutScanner.FileItem>>? _preloadedStartMenuTask;
        private Task<List<ShortcutScanner.FileItem>>? _preloadedDesktopTask;

        private double _iconSize = 118;
        public double IconSize
        {
            get => _iconSize;
            set
            {
                if (_iconSize != value)
                {
                    LogService.Write("UI", $"IconSize changed from={_iconSize} to={value}");
                    _iconSize = value;
                    OnPropertyChanged(nameof(IconSize));
                    OnPropertyChanged(nameof(DesiredIconWidth));
                }
            }
        }

        public double DesiredIconWidth => IconSize + 16;

        private bool _isFiltered;
        public bool IsFiltered
        {
            get => _isFiltered;
            set
            {
                if (_isFiltered != value)
                {
                    LogService.Write("UI", $"IsFiltered changed from={_isFiltered} to={value}");
                    _isFiltered = value;
                    OnPropertyChanged(nameof(IsFiltered));


                    if (SearchButton != null)
                    {
                        if (value)
                        {

                            SearchButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                        }
                        else
                        {

                            SearchButton.ClearValue(Button.BackgroundProperty);
                        }
                    }
                }
            }
        }

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            set { if (_hasUpdate != value) { LogService.Write("Update", $"HasUpdate changed from={_hasUpdate} to={value}"); _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }
        }

        private UpdateService.ReleaseInfo? _pendingUpdate;
        private bool _isUserInteracting = false;
        private bool _isRefreshPending = false;
        private bool _isRootLoaded = false;
        private bool _pendingInitialRefresh = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        private async Task RevealUIWhenReadyAsync()
        {
            var sw = Stopwatch.StartNew();
            var maxWaitTime = 1000;
            
            while (sw.ElapsedMilliseconds < maxWaitTime)
            {
                if (AppGrid != null && AppGrid.ActualWidth > 0)
                {
                    AppGrid.Visibility = Visibility.Visible;
                    _isRootLoaded = true;
                    LogService.Write("Startup", $"UI revealed early at {sw.ElapsedMilliseconds}ms");
                    
                    if (_pendingInitialRefresh)
                    {
                        _pendingInitialRefresh = false;
                        RefreshView();
                    }
                    return;
                }
                
                await Task.Delay(16);
            }
            
            if (AppGrid != null)
            {
                AppGrid.Visibility = Visibility.Visible;
                LogService.Write("Startup", $"UI revealed after timeout at {sw.ElapsedMilliseconds}ms");
            }
            _isRootLoaded = true;
            
            if (_pendingInitialRefresh)
            {
                _pendingInitialRefresh = false;
                RefreshView();
            }
        }

        public MainWindow()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LogService.Write("Startup", $"Ctor Start [Version: {AppVersion.DisplayVersion}]");
            // Apply debug path overrides early if started with -debug
            try { DebugPaths.ApplyIfDebug(); } catch { }
            this.InitializeComponent();

            _customSections = new StackPanel[] { PropCustomSection1, PropCustomSection2, PropCustomSection3, PropCustomSection4, PropCustomSection5, PropCustomSection6, PropCustomSection7, PropCustomSection8, PropCustomSection9, PropCustomSection10 };
            _customTitles = new TextBox[] { PropCustomTitle1, PropCustomTitle2, PropCustomTitle3, PropCustomTitle4, PropCustomTitle5, PropCustomTitle6, PropCustomTitle7, PropCustomTitle8, PropCustomTitle9, PropCustomTitle10 };
            _customCommands = new TextBox[] { PropCustomCommand1, PropCustomCommand2, PropCustomCommand3, PropCustomCommand4, PropCustomCommand5, PropCustomCommand6, PropCustomCommand7, PropCustomCommand8, PropCustomCommand9, PropCustomCommand10 };
            _customAdmins = new CheckBox[] { PropCustomAdmin1, PropCustomAdmin2, PropCustomAdmin3, PropCustomAdmin4, PropCustomAdmin5, PropCustomAdmin6, PropCustomAdmin7, PropCustomAdmin8, PropCustomAdmin9, PropCustomAdmin10 };
            _customBrowses = new DropDownButton[] { BtnCustomBrowse1, BtnCustomBrowse2, BtnCustomBrowse3, BtnCustomBrowse4, BtnCustomBrowse5, BtnCustomBrowse6, BtnCustomBrowse7, BtnCustomBrowse8, BtnCustomBrowse9, BtnCustomBrowse10 };
            _customAdminLabels = new TextBlock[] { PropCustomAdminLabel1, PropCustomAdminLabel2, PropCustomAdminLabel3, PropCustomAdminLabel4, PropCustomAdminLabel5, PropCustomAdminLabel6, PropCustomAdminLabel7, PropCustomAdminLabel8, PropCustomAdminLabel9, PropCustomAdminLabel10 };
            _customSlotLabels = new TextBlock[] { PropCustomSlotLabel1, PropCustomSlotLabel2, PropCustomSlotLabel3, PropCustomSlotLabel4, PropCustomSlotLabel5, PropCustomSlotLabel6, PropCustomSlotLabel7, PropCustomSlotLabel8, PropCustomSlotLabel9, PropCustomSlotLabel10 };

            for (int i = 0; i < 10; i++)
            {
                int index = i;
                _customTitles[index].TextChanged += (s, e) => UpdateCustomVisibility();
                _customCommands[index].TextChanged += (s, e) => UpdateCustomVisibility();
                _customBrowses[index].Click += (s, e) => { };
            }

            PropExePath.TextChanged += async (s, e) =>
            {
                try
                {
                    string path = PropExePath.Text?.Trim() ?? "";
                    var platform = await GamePlatformHelper.DetectPlatformAsync(path);

                    if (platform != null)
                    {
                        PropPlatformBadge.Text = platform.PlatformName;
                        PropPlatformBadgeContainer.Visibility = Visibility.Visible;
                    }
                    else if (!string.IsNullOrEmpty(path))
                    {
                        PropPlatformBadge.Text = "User";
                        PropPlatformBadgeContainer.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PropPlatformBadgeContainer.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex) { LogService.Write("UI", "PropExePath handler failed", ex); }
            };


            VersionText.Text = AppVersion.DisplayVersion;



            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += (s, e) =>
                {
                    try
                    {
                        _ = RevealUIWhenReadyAsync();
                    }
                    catch (Exception ex) { LogService.Write("UI", "Root element loaded handler failed", ex); }
                };
            }

            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            this.ExtendsContentIntoTitleBar = true;


            this.Title = "EricGameLauncher";


            try
            {
                _ = LoadIconResourceAsync();
            }
            catch (Exception ex) { LogService.Write("App", "Load icon resource async failed", ex); }


            var titleBar = this.AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            this.SetTitleBar(TitleBarGrid);

            ConfigService.Initialize();
            ServerConfigManager.LoadReadIds();

            I18n.Load(ConfigService.Language);
            I18n.LanguageChanged += () =>
            {
                DispatcherQueue.TryEnqueue(() => ApplyLocalization());
            };

            ServerConfigManager.AnnouncementsUpdated += OnAnnouncementsUpdated;

            ConfigService.IconSize = ConfigService.IconSize;
            ConfigService.DataChanged += () =>
            {
                DispatcherQueue.TryEnqueue(() => RefreshView());
            };


            RestoreWindowState();

            this.Closed += MainWindow_Closed;

            LoadSettings();
            ApplyLocalization();
            RefreshAnnouncementList();

            _hWnd = WindowNative.GetWindowHandle(this);
            _oldWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = new WndProc(WindowProcess)));

            _ = LoadDataAsync();
            _ = InitializeNetworkTasksAsync();
            _ = InitializeMenuItemsAsync();
            LogService.Write("Startup", $"Ctor End duration={sw.ElapsedMilliseconds}ms");
        }

        private async Task InitializeMenuItemsAsync()
        {
            await Task.Delay(200);
            
            try
            {
                if (MoreMenuFlyout.Items.Count > 0)
                {
                    var aboutItem = MoreMenuFlyout.Items.LastOrDefault() as MenuFlyoutItem;
                    if (aboutItem != null)
                    {
                        aboutItem.Loaded += (sender, args) =>
                        {
                            var textBlock = FindChildByName(aboutItem, "MenuVersionText") as TextBlock;
                            if (textBlock != null)
                            {
                                textBlock.Text = AppVersion.DisplayVersion;
                            }
                        };
                    }
                }
            }
            catch (Exception ex) { LogService.Write("UI", "InitializeMenuItemsAsync failed", ex); }
        }

        private async Task LoadIconResourceAsync()
        {
            await Task.Delay(100);
            
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("EricGameLauncher.ico.ico");
                if (stream != null)
                {
                    if (!System.IO.Directory.Exists(ConfigService.SystemCachePath))
                        System.IO.Directory.CreateDirectory(ConfigService.SystemCachePath);
                    string tempIconPath = System.IO.Path.Combine(ConfigService.SystemCachePath, "EricGameLauncher_TempIcon.ico");
                    using var fileStream = new System.IO.FileStream(tempIconPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    stream.CopyTo(fileStream);
                    fileStream.Close();

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            this.AppWindow.SetIcon(tempIconPath);
                            var bitmap = new BitmapImage(new Uri(tempIconPath));
                            TitleBarIcon.Source = bitmap;
                            LogService.Write("App", $"Icon resource loaded successfully");
                        }
                        catch (Exception ex) { LogService.Write("App", "Set icon failed", ex); }
                    });
                }
            }
            catch (Exception ex) { LogService.Write("App", "Load icon resource failed", ex); }
        }

        private async Task LoadDataAsync()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            LogService.StartupEnter();
            try
            {
                using (LogService.StartOperation("Startup", "LoadDataAsync"))
                {
                    LogService.Write("Startup", "LoadDataAsync Start");
                    await LoadData();
                    LogService.Write("Startup", $"LoadDataAsync AfterLoadData duration={sw.ElapsedMilliseconds}ms items={_allItems.Count}");
                    AutoCleanRecycleBin();
                    LogService.Write("Startup", $"LoadDataAsync AfterAutoClean totalDuration={sw.ElapsedMilliseconds}ms recycleItems={_recycleItems.Count}");
                }
            }
            finally
            {
                LogService.StartupExit();
            }
        }

        private async Task InitializeNetworkTasksAsync()
        {
            var sw = Stopwatch.StartNew();
            LogService.StartupEnter();
            try
            {
                using (LogService.StartOperation("Startup", "InitializeNetworkTasksAsync"))
                {
                    LogService.Write("Startup", "InitializeNetwork Start");
                    await ServerConfigManager.FetchConfigAsync();
                    LogService.Write("Startup", $"InitializeNetwork AfterFetch {sw.ElapsedMilliseconds}ms");
                    _ = CheckForUpdatesInBackgroundAsync();
                    LogService.Write("Startup", $"InitializeNetwork BackgroundCheckScheduled {sw.ElapsedMilliseconds}ms");
                }
            }
            finally
            {
                LogService.StartupExit();
            }
        }

        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                using (LogService.StartOperation("Update", "BackgroundCheck"))
                {
                    LogService.Write("Update", "BackgroundCheck Start - scheduled for later");
                    await Task.Delay(5000);
                    LogService.Write("Update", "BackgroundCheck AfterDelay - now checking for updates");
                    await CheckForUpdatesQuietlyAsync(skipDelay: true);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "BackgroundCheck Failed", ex);
            }
        }

        private async Task CheckForUpdatesQuietlyAsync(bool skipDelay = false)
        {
            try
            {
                if (DebugPaths.IsDebug())
                {
                    LogService.Write("Update", "QuietCheck skipped because Debug mode is active");
                    return;
                }
                using (LogService.StartOperation("Update", "QuietCheck"))
                {
                    LogService.Write("Update", $"QuietCheck Start skipDelay={skipDelay}");

                    var release = await UpdateService.CheckForUpdateAsync(ConfigService.UpdateChannel);

                    bool isForced = false;
                    if (release != null)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(release.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                        if (match.Success)
                        {
                            Version latestVersion = UpdateService.NormalizeVersion(match.Value);
                            isForced = UpdateService.CheckForceUpdateAsync(latestVersion);
                        }
                        else
                        {
                            isForced = UpdateService.CheckForceUpdateAsync();
                        }

                        _pendingUpdate = release;

                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                        {
                            HasUpdate = true;
                            LogService.Write("Update", "HasUpdate flag set on UI thread");
                        });

                        if (isForced)
                        {
                            LogService.Write("Update", "Forced update detected");
                            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, async () =>
                            {
                                await StartUpdateFlowAsync(release, isForced);
                            });
                        }
                    }
                    LogService.Write("Update", "QuietCheck End");
                }
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "QuietCheck Failed", ex);
            }
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            _pendingUpdate = null;
            HasUpdate = false;
            using (LogService.StartOperation("Update", "ManualCheck"))
            {
                LogService.Write("Update", "ManualCheck Start");
                var release = await UpdateService.GetReleaseAsync(ConfigService.UpdateChannel);
                if (release != null)
                {
                    bool hasUpdate = false;
                    Version latestVersion = new Version(0, 0, 0, 0);
                    var match = System.Text.RegularExpressions.Regex.Match(release.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                    if (match.Success)
                    {
                        latestVersion = UpdateService.NormalizeVersion(match.Value);
                        Version currentVersion = UpdateService.NormalizeVersion(AppVersion.Version);
                        hasUpdate = latestVersion > currentVersion;
                    }

                    if (hasUpdate)
                    {
                        _pendingUpdate = release;
                        bool isForced = DebugPaths.IsDebug() ? false : UpdateService.CheckForceUpdateAsync(latestVersion);

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            HasUpdate = true;
                        });

                        if (isForced)
                        {
                            await StartUpdateFlowAsync(release, true);
                        }
                        else
                        {
                            await ShowReleaseDialogAsync(release, hasUpdate: true);
                        }
                    }
                    else
                    {
                        await ShowReleaseDialogAsync(release, hasUpdate: false);
                    }
                }
                else
                {
                    ContentDialog noUpdateDialog = new ContentDialog
                    {
                        Title = I18n.T("Update_NoUpdateTitle"),
                        Content = I18n.T("Update_NoUpdateContent"),
                        CloseButtonText = I18n.T("Update_OK"),
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = this.Content.XamlRoot
                    };
                    await noUpdateDialog.ShowAsync();
                }
                LogService.Write("Update", "ManualCheck End");
            }
        }

        private async void MenuPrivacyItem_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuPrivacyItem_Click Start");
            ContentDialog privacyDialog = new ContentDialog
            {
                Title = I18n.T("Privacy_DialogTitle"),
                Content = new Microsoft.UI.Xaml.Controls.ScrollViewer
                {
                    VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                    Content = new Microsoft.UI.Xaml.Controls.TextBlock
                    {
                        Text = I18n.T("Privacy_DialogContent"),
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 16, 0)
                    }
                },
                CloseButtonText = I18n.T("Privacy_DialogClose"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };
            await privacyDialog.ShowAsync();
            LogService.Write("UI", "MenuPrivacyItem_Click End");
        }

        #region Win32 Message Interception
        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private WndProc? _wndProcDelegate;
        private DateTime _lastLaunchTime = DateTime.MinValue;

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const int GWLP_WNDPROC = -4;
        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;
        

        private IntPtr WindowProcess(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_ENTERSIZEMOVE)
            {
                _isUserInteracting = true;
                LogService.Write("App", "WindowProcess WM_ENTERSIZEMOVE");
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                _isUserInteracting = false;
                LogService.Write("App", "WindowProcess WM_EXITSIZEMOVE");
                SaveWindowState(null);

                if (_isRefreshPending)
                {
                    RefreshView();
                }
            }
            
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }
        #endregion

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

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            using (LogService.StartOperation("App", "Shutdown"))
            {
                LogService.Write("App", "MainWindow_Closed Start");
                ServerConfigManager.AnnouncementsUpdated -= OnAnnouncementsUpdated;
                if (_oldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(_hWnd, GWLP_WNDPROC, _oldWndProc);
                    _oldWndProc = IntPtr.Zero;
                }
                _wndProcDelegate = null;
                LogService.Write("App", "MainWindow_Closed End");
            }
        }

        private void MenuAuthorIconInternal_Loaded(object sender, RoutedEventArgs e)
        {
            using (LogService.StartOperation("App", "MenuAuthorIconInternal_Loaded"))
            {
            try
            {
                LogService.Write("App", "MenuAuthorIconInternal_Loaded Start");
                if (sender is Image img)
                {
                    string tempIconPath = System.IO.Path.Combine(ConfigService.SystemCachePath, "EricGameLauncher_TempIcon.ico");
                    if (System.IO.File.Exists(tempIconPath))
                    {
                        img.Source = new BitmapImage(new Uri(tempIconPath));
                        LogService.Write("App", $"MenuAuthorIconInternal loaded icon from {tempIconPath}");
                    }
                    else
                    {
                        LogService.Write("App", $"MenuAuthorIconInternal no icon found at {tempIconPath}");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuAuthorIconInternal_Loaded failed", ex); }
            }
        }

        private void SaveWindowState(Microsoft.UI.Windowing.AppWindowChangedEventArgs? args)
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
                    _ = Task.Run(() => ConfigService.SaveConfig());
                    LogService.Write("App", $"SaveWindowState persisted newBounds x={x} y={y} w={width} h={height}");
                }
            }
            catch (Exception ex) { LogService.Write("App", "SaveWindowState failed", ex); }
            }
        }

        private void LoadSettings()
        {
            using (LogService.StartOperation("App", "LoadSettings"))
            {
            try
            {
                LogService.Write("App", $"LoadSettings Start CloseAfterLaunch={ConfigService.CloseAfterLaunch} LaunchMode={ConfigService.LaunchMode} IconSize={ConfigService.IconSize}");

                if (ToggleCloseAfterLaunch != null)
                {
                    ToggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
                }

                if (ComboLaunchMode != null)
                {
                    ComboLaunchMode.SelectedIndex = ConfigService.LaunchMode == "double" ? 1 : 0; AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";
                }

                if (_sizeSlider != null)
                {
                    _sizeSlider.Value = ConfigService.IconSize;
                }
                LogService.Write("App", "LoadSettings applied UI values");
            }
            catch (Exception ex) { LogService.Write("App", "LoadSettings failed", ex); }
            }
        }

        private void RefreshView()
        {
            using (LogService.StartOperation("App", "RefreshView"))
            {
                try
                {
                    if (!_isRootLoaded)
                    {
                        _pendingInitialRefresh = true;
                        LogService.Write("App", "RefreshView postponed until root loaded");
                        return;
                    }

                    if (_isUserInteracting)
                    {
                        _isRefreshPending = true;
                        LogService.Write("App", "RefreshView postponed due to user interaction");
                        return;
                    }
                    _isRefreshPending = false;

                    var sw = Stopwatch.StartNew();
                    var items = ConfigService.LoadItems();
                    var recycleItems = ConfigService.LoadRecycleBinItems();
                    LogService.Write("App", $"RefreshView LoadData duration={sw.ElapsedMilliseconds}ms");
                    
                    var normalItems = items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
                    var normalizedRecycle = new List<AppItem>();
                    bool normalized = false;
                    int normalizeCount = 0;
                    
                    foreach (var item in recycleItems)
                    {
                        if (item.Status == (int)AppItemStatus.Normal)
                        {
                            item.Status = (int)AppItemStatus.Recycled;
                            item.DeletedAt = null;
                            normalized = true;
                            normalizeCount++;
                        }
                        normalizedRecycle.Add(item);
                    }
                    
                    var misplaced = items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
                    if (misplaced.Count > 0)
                    {
                        var recycleIds = new HashSet<string>(normalizedRecycle.Select(x => x.Id));
                        foreach (var item in misplaced)
                        {
                            if (recycleIds.Add(item.Id))
                            {
                                normalizedRecycle.Add(item);
                                normalizeCount++;
                            }
                        }
                        normalized = true;
                    }
                    
                    if (normalized)
                    {
                        LogService.Write("App", $"RefreshView NormalizedItems count={normalizeCount}");
                        ConfigService.SaveItems(normalItems, normalizedRecycle, false);
                    }
                    
                    bool hasChanges = CheckForDataChanges(normalItems, normalizedRecycle);
                    
                    if (hasChanges)
                    {
                        LogService.Write("App", $"RefreshView DataChanged - rebuilding collections");
                        _allItems = new ObservableCollection<AppItem>(normalItems);
                        _recycleItems = new ObservableCollection<AppItem>(normalizedRecycle);
                        _viewItems = new ObservableCollection<AppItem>(_allItems);
                        AppGrid.ItemsSource = _viewItems;
                    }
                    else
                    {
                        LogService.Write("App", $"RefreshView NoChanges - skipping rebuild");
                    }
                    
                    LogService.Write("App", $"RefreshView applied counts all={_allItems.Count} recycle={_recycleItems.Count} view={_viewItems.Count} duration={sw.ElapsedMilliseconds}ms");
                    UpdateEmptyState();

                    this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        LogService.Write("App", "RefreshView UpdateGridItemSizes queued");
                        UpdateGridItemSizes(IconSize);
                    });
                }
                catch (Exception ex) { LogService.Write("App", "RefreshView failed", ex); }
            }
        }

        private bool CheckForDataChanges(List<AppItem> newItems, List<AppItem> newRecycle)
        {
            if (_allItems.Count != newItems.Count || _recycleItems.Count != newRecycle.Count)
            {
                LogService.Write("App", $"CheckForDataChanges CountChanged allOld={_allItems.Count} allNew={newItems.Count} recycleOld={_recycleItems.Count} recycleNew={newRecycle.Count}");
                return true;
            }

            var oldIds = new HashSet<string>(_allItems.Select(x => x.Id));
            var newIds = new HashSet<string>(newItems.Select(x => x.Id));
            
            if (!oldIds.SetEquals(newIds))
            {
                LogService.Write("App", $"CheckForDataChanges ItemsChanged");
                return true;
            }
            
            LogService.Write("App", $"CheckForDataChanges NoChanges");
            return false;
        }

        private void UpdateEmptyState()
        {
            using (LogService.StartOperation("App", "UpdateEmptyState"))
            {
            try
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                LogService.Write("App", "UpdateEmptyState applied: hidden");
            }
            catch (Exception ex) { LogService.Write("App", "UpdateEmptyState failed", ex); }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            try { LogService.Write("App", $"OnPropertyChanged {propertyName}"); } catch { }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadData()
        {
            try
            {
                using (LogService.StartOperation("Startup", "LoadData"))
                {
                    var sw = Stopwatch.StartNew();
                    LogService.Write("Startup", "LoadData Enter");
                if (ConfigService.RequiresMigration)
                {
                    MigrationOverlay.Visibility = Visibility.Visible;
                    await Task.Delay(200);
                    LogService.Write("Startup", $"LoadData AfterMigrationDelay {sw.ElapsedMilliseconds}ms");

                    string configPath = ConfigService.ConfigFilePath;
                    try
                    {
                        string tempDir = System.IO.Path.Combine(ConfigService.SystemCachePath, "updater.cfgver");
                        if (!System.IO.Directory.Exists(tempDir)) System.IO.Directory.CreateDirectory(tempDir);
                        string cfgUpdaterPath = System.IO.Path.Combine(tempDir, "updater.cfgver.exe");

                        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        string[] resources = { "updater.cfgver.exe", "updater.cfgver.dll", "updater.cfgver.runtimeconfig.json" };
                        foreach (var res in resources)
                        {
                            string resName = $"EricGameLauncher.{res}";
                            string outputPath = System.IO.Path.Combine(tempDir, res);
                            using (var stream = assembly.GetManifestResourceStream(resName))
                            {
                                if (stream == null) continue;
                                using (var fileStream = new System.IO.FileStream(outputPath, System.IO.FileMode.Create, System.IO.FileAccess.Write))
                                {
                                    stream.CopyTo(fileStream);
                                }
                            }
                        }

                        var processStartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = cfgUpdaterPath,
                            WorkingDirectory = tempDir,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        processStartInfo.ArgumentList.Add(configPath);
                        var process = System.Diagnostics.Process.Start(processStartInfo);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                        }
                    }
                    catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }

                    try
                    {
                        string? currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(currentExe))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = currentExe,
                                WorkingDirectory = System.IO.Path.GetDirectoryName(currentExe),
                                UseShellExecute = true
                            });
                        }
                    }
                    catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }

                    try
                    {
                        LogService.Write("App", "Exit requested (restart flow)");
                        Microsoft.UI.Xaml.Application.Current.Exit();
                    }
                    catch (Exception ex) { LogService.Write("App", "Exit failed (restart flow)", ex); }
                    return;
                }

                LogService.Write("Startup", $"LoadData BeforeRefreshGlobal {sw.ElapsedMilliseconds}ms");
                await ConfigService.RefreshGlobalAsync();
                LogService.Write("Startup", $"LoadData AfterRefreshGlobal {sw.ElapsedMilliseconds}ms");

                _preloadedStartMenuTask = Task.Run(() => ShortcutScanner.GetStartMenuItems());
                _preloadedDesktopTask = Task.Run(() => ShortcutScanner.GetDesktopItems());
                    LogService.Write("Startup", $"LoadData AfterPreloadTasks {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex) { LogService.Write("App", "LoadData failed", ex); }
        }

        private void SaveData()
        {
            using (LogService.StartOperation("App", "SaveData"))
            {
            try
            {
                LogService.Write("App", $"SaveData Start items={_allItems.Count} recycle={_recycleItems.Count}");
                ConfigService.SaveItems(_allItems.ToList(), _recycleItems.ToList());
                LogService.Write("App", "SaveData completed");
            }
            catch (Exception ex) { LogService.Write("App", "SaveData failed", ex); }
            }
        }


        private DispatcherTimer? _tooltipTimer;
        private AppItem? _hoveredItem;
        private FrameworkElement? _hoveredElement;
        private FrameworkElement? _closeAfterLaunchInputRoot;
        private DispatcherTimer? _closeAfterLaunchTimer;
        private bool _closeAfterLaunchPending = false;

        private void StartCloseAfterLaunchTimer()
        {
            if (_closeAfterLaunchPending) return;
            _closeAfterLaunchPending = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _closeAfterLaunchInputRoot ??= this.Content as FrameworkElement;
                _closeAfterLaunchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
                _closeAfterLaunchTimer.Tick += CloseAfterLaunchTimer_Tick;
                _closeAfterLaunchTimer.Start();
                if (_closeAfterLaunchInputRoot != null)
                {
                    _closeAfterLaunchInputRoot.PointerPressed += OnUserActivity_CloseAfterLaunch;
                    _closeAfterLaunchInputRoot.KeyDown += OnUserActivity_CloseAfterLaunch;
                }
            });
        }

        private void CloseAfterLaunchTimer_Tick(object? sender, object e)
        {
            using (LogService.StartOperation("App", "CloseAfterLaunchTimer_Tick"))
            {
            try
            {
                if (_closeAfterLaunchTimer == null) return;
                _closeAfterLaunchTimer.Stop();
                _closeAfterLaunchTimer.Tick -= CloseAfterLaunchTimer_Tick;
                _closeAfterLaunchTimer = null;

                IntPtr fg = GetForegroundWindow();
                LogService.Write("App", $"CloseAfterLaunchTimer_Tick foreground={fg} hWnd={_hWnd}");
                if (fg != _hWnd)
                {
                    LogService.Write("App", "Exit requested (CloseAfterLaunch)");
                    try { Application.Current.Exit(); } catch (Exception ex) { LogService.Write("App", "Exit failed (CloseAfterLaunch)", ex); }
                }
                else
                {
                    // Window still foreground: cancel and do not retry
                    LogService.Write("App", "CloseAfterLaunchTimer_Tick cancelling because window is foreground");
                    CancelCloseAfterLaunch();
                }
            }
            catch (Exception ex) { LogService.Write("App", "CloseAfterLaunchTimer_Tick failed", ex); }
            }
        }

        private void OnUserActivity_CloseAfterLaunch(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => CancelCloseAfterLaunch();
        private void OnUserActivity_CloseAfterLaunch(object? sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e) => CancelCloseAfterLaunch();

        private void CancelCloseAfterLaunch()
        {
            if (!_closeAfterLaunchPending) return;
            _closeAfterLaunchPending = false;
            if (_closeAfterLaunchTimer != null)
            {
                _closeAfterLaunchTimer.Stop();
                _closeAfterLaunchTimer.Tick -= CloseAfterLaunchTimer_Tick;
                _closeAfterLaunchTimer = null;
            }
            try
            {
                if (_closeAfterLaunchInputRoot != null)
                {
                    _closeAfterLaunchInputRoot.PointerPressed -= OnUserActivity_CloseAfterLaunch;
                    _closeAfterLaunchInputRoot.KeyDown -= OnUserActivity_CloseAfterLaunch;
                }
            }
            catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }
        }

        private void ItemPanel_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AppItem item)
            {
                _hoveredItem = item;
                _hoveredElement = fe;

                if (_tooltipTimer == null)
                {
                    _tooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    _tooltipTimer.Tick += TooltipTimer_Tick;
                }
                _tooltipTimer.Stop();
                _tooltipTimer.Start();
            }
        }

        private void TooltipTimer_Tick(object? sender, object e)
        {
            using (LogService.StartOperation("App", "TooltipTimer_Tick"))
            {
            try
            {
                _tooltipTimer?.Stop();
                if (_hoveredItem != null && _hoveredElement != null && CustomIconToolTip != null && CustomIconToolTipText != null)
                {
                    LogService.Write("App", $"TooltipTimer_Tick showing tooltip for itemId={_hoveredItem.Id} title={_hoveredItem.Title}");
                    CustomIconToolTipText.Text = _hoveredItem.Title;

                    if (this.Content != null)
                    {
                        try
                        {
                            var border = CustomIconToolTip.Child as FrameworkElement;
                            var titleText = (_hoveredElement as StackPanel)?.Children.OfType<TextBlock>().FirstOrDefault();
                            
                            if (border != null && titleText != null)
                            {
                                border.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                                double popupWidth = border.DesiredSize.Width;
                                double popupHeight = border.DesiredSize.Height;

                                var transform = _hoveredElement.TransformToVisual(this.Content);
                                
                                var targetCenter = transform.TransformPoint(new Windows.Foundation.Point(
                                    _hoveredElement.ActualWidth / 2,
                                    _hoveredElement.ActualHeight - (titleText.ActualHeight / 2)
                                ));

                                double targetX = targetCenter.X - (popupWidth / 2);
                                double targetY = targetCenter.Y - (popupHeight / 2);

                                if (this.Content is FrameworkElement contentFE)
                                {
                                    double maxWidth = contentFE.ActualWidth;
                                    double padding = 8.0;

                                    if (targetX < padding)
                                    {
                                        targetX = padding;
                                    }
                                    else if (targetX + popupWidth > maxWidth - padding)
                                    {
                                        targetX = maxWidth - popupWidth - padding;
                                    }
                                }

                                CustomIconToolTip.HorizontalOffset = targetX;
                                CustomIconToolTip.VerticalOffset = targetY;
                                CustomIconToolTip.IsOpen = true;
                                LogService.Write("App", $"TooltipTimer_Tick opened tooltip at x={targetX} y={targetY} w={popupWidth} h={popupHeight}");
                            }
                        }
                        catch (Exception ex) { LogService.Write("App", "TooltipTimer_Tick inner failed", ex); }
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "TooltipTimer_Tick failed", ex); }
            }
        }

        private void ItemPanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AppItem item)
            {
                if (_hoveredItem == item)
                {
                    HideCustomToolTip();
                }
            }
        }

        private void AppGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (LogService.StartOperation("App", "AppGrid_ItemClick"))
            {
            try
            {
                if (ConfigService.LaunchMode == "double")
                {
                    LogService.Write("App", "AppGrid_ItemClick ignored due to double launch mode");
                    return;
                }
                var item = e.ClickedItem as AppItem;
                LogService.Write("App", $"AppGrid_ItemClick clicked itemId={item?.Id} title={item?.Title}");
                if (item != null)
                {
                    LaunchItem(item);
                }
            }
            catch (Exception ex) { LogService.Write("App", "AppGrid_ItemClick failed", ex); }
            }
        }

        private void HideCustomToolTip()
        {
            try { LogService.Write("App", "HideCustomToolTip called"); } catch { }
            _hoveredItem = null;
            _hoveredElement = null;
            _tooltipTimer?.Stop();
            if (CustomIconToolTip != null) CustomIconToolTip.IsOpen = false;
            try { LogService.Write("App", "HideCustomToolTip completed"); } catch { }
        }

        private void AppGrid_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try { LogService.Write("App", "AppGrid_PointerWheelChanged called"); } catch { }
            HideCustomToolTip();
        }

        private void AppGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            using (LogService.StartOperation("App", "AppGrid_DoubleTapped"))
            {
            try
            {
                if (ConfigService.LaunchMode != "double")
                {
                    LogService.Write("App", "AppGrid_DoubleTapped ignored because launch mode != double");
                    return;
                }
                if (AppGrid.SelectedItem is AppItem item)
                {
                    LogService.Write("App", $"AppGrid_DoubleTapped launching selected itemId={item.Id} title={item.Title}");
                    LaunchItem(item);
                }
                else if (e.OriginalSource is FrameworkElement fe && fe.DataContext is AppItem ctxItem)
                {
                    LogService.Write("App", $"AppGrid_DoubleTapped launching context itemId={ctxItem.Id} title={ctxItem.Title}");
                    LaunchItem(ctxItem);
                }
            }
            catch (Exception ex) { LogService.Write("App", "AppGrid_DoubleTapped failed", ex); }
            }
        }




        private async void LaunchItem(AppItem item)
        {
            if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
            _lastLaunchTime = DateTime.Now;

            try
            {
                using (LogService.StartOperation("App", $"LaunchItem {item.Title}"))
                {
                        LogService.Write("App", $"LaunchItem Start id={item.Id} title={item.Title}");
                    TriggerItemLoadingAnimation(item);

                if (item.UseAlternativeLaunch && !string.IsNullOrEmpty(item.AlternativeLaunchCommand))
                {
                    LogService.Write("App", $"LaunchItem alternative command exec command={item.AlternativeLaunchCommand} isAdmin={item.IsAltAdmin}");

                    RunProcess(item.AlternativeLaunchCommand, item.IsAltAdmin);
                    LogService.Write("App", "LaunchItem alternative command invoked");
                }
                else if (!string.IsNullOrEmpty(item.ExePath))
                {
                    LogService.Write("App", $"LaunchItem exe exec path={item.ExePath} isAdmin={item.IsAdmin}");

                    RunProcess(item.ExePath, item.IsAdmin);


                    if (item.RunAlongside && !string.IsNullOrEmpty(item.AlongsideCommand))
                    {
                        LogService.Write("App", $"LaunchItem alongside exec command={item.AlongsideCommand} isAdmin={item.IsAlongsideAdmin}");
                        RunProcess(item.AlongsideCommand, item.IsAlongsideAdmin);
                        LogService.Write("App", "LaunchItem alongside command invoked");
                    }
                }
                }
            }
            catch (Exception ex) { LogService.Write("App", "LaunchItem failed", ex); }
        }

        private void RunProcess(string path, bool admin)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                using (LogService.StartOperation("App", $"RunProcess {path}"))
                {
                    LogService.Write("App", $"RunProcess Start admin={admin} path={path}");

                    path = Environment.ExpandEnvironmentVariables(path);

                    var psi = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };


                if (path.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                {
                    if (admin)
                    {
                        string psScript = $"Start-Process '{path.Replace("'", "''")}' -Verb RunAs";
                        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
                        psi.FileName = "powershell.exe";
                        psi.Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                    }
                    else
                    {
                        psi.FileName = "explorer.exe";
                        psi.Arguments = $"\"{path.Replace("\"", "\\\"")}\"";
                    }
                }
                else if (path.Contains("://"))
                {

                    psi.FileName = path;
                }
                else
                {

                    var (filePath, arguments) = SplitPathAndArguments(path);


                    psi.FileName = filePath;
                    
                    try
                    {
                        string? dir = System.IO.Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            psi.WorkingDirectory = dir;
                        }
                    }
                    catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }

                    if (!string.IsNullOrEmpty(arguments))
                    {
                        var argList = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                               .Select(a => $"\"{a.Trim('"').Replace("\"", "\\\"")}\"");
                        psi.Arguments = string.Join(" ", argList);
                    }
                    if (admin)
                        psi.Verb = "runas";
                }

                Process? process = null;
                try
                {
                    process = Process.Start(psi);
                    if (process != null)
                    {
                        LogService.Write("App", $"RunProcess started pid={process.Id} file={psi.FileName} args={psi.Arguments}");
                    }
                    else
                    {
                        LogService.Write("App", $"RunProcess start returned null for file={psi.FileName} args={psi.Arguments}");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Write("App", $"RunProcess start exception file={psi.FileName} args={psi.Arguments}", ex);
                    throw;
                }

                if (ConfigService.CloseAfterLaunch)
                {
                    StartCloseAfterLaunchTimer();
                    LogService.Write("App", "RunProcess triggered CloseAfterLaunch timer");
                }
                }
            }
            catch (Exception ex) { LogService.Write("App", "RunProcess failed", ex); }
        }









        private (string filePath, string arguments) SplitPathAndArguments(string input)
        {
            try { LogService.Write("App", $"SplitPathAndArguments called inputLen={(input==null?0:input.Length)}"); } catch { }
            if (string.IsNullOrWhiteSpace(input))
                return (string.Empty, string.Empty);

            input = input.Trim();


            if (input.StartsWith("\""))
            {
                int endQuote = input.IndexOf("\"", 1);
                if (endQuote > 0)
                {
                    string filePath = input.Substring(1, endQuote - 1);

                    filePath = Environment.ExpandEnvironmentVariables(filePath);
                    string arguments = endQuote < input.Length - 1 ? input.Substring(endQuote + 1).Trim() : string.Empty;
                    return (filePath, arguments);
                }
            }



            int lastSpaceIndex = input.LastIndexOf(' ');
            if (lastSpaceIndex > 0)
            {

                int currentIndex = lastSpaceIndex;
                while (currentIndex > 0)
                {
                    string potentialPath = input.Substring(0, currentIndex);

                    string expandedPath = Environment.ExpandEnvironmentVariables(potentialPath);
                    if (File.Exists(expandedPath))
                    {
                        string arguments = input.Substring(currentIndex + 1).Trim();
                        return (expandedPath, arguments);
                    }


                    currentIndex = input.LastIndexOf(' ', currentIndex - 1);
                }
            }


            return (Environment.ExpandEnvironmentVariables(input), string.Empty);
        }



        private AppItem? GetTag(object sender)
        {
            try
            {
                try { LogService.Write("App", $"GetTag called senderType={(sender==null?"null":sender.GetType().Name)}"); } catch { }
                if (sender is MenuFlyout menu && menu.Target is FrameworkElement target)
                {
                    return (target.Tag as AppItem) ?? (target.DataContext as AppItem);
                }
                if (sender is FrameworkElement fe) 
                {
                    return (fe.Tag as AppItem) ?? (fe.DataContext as AppItem);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogService.Write("App", "GetTag failed", ex);
                return null;
            }
        }

        private void MenuRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                LogService.Write("App", $"MenuRun_Click invoked itemId={(item?.Id ?? "null")} title={(item?.Title ?? "")}");
                if (item != null)
                {
                    LaunchItem(item);
                    LogService.Write("App", $"MenuRun_Click launched itemId={item.Id}");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuRun_Click failed", ex); }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
            HideCustomToolTip();

            if (sender is MenuFlyout menu)
            {
                var ctxItem = GetTag(menu);
                LogService.Write("App", $"ContextMenu_Opening invoked for itemId={(ctxItem?.Id ?? "null")} title={(ctxItem?.Title ?? "")}");
                var item = GetTag(menu);
                if (item == null) return;

                bool isPeFile = false;
                try
                {
                    string path = (item.ExePath ?? "").Trim('\"');
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) isPeFile = true;
                    else if (item.ExePath?.Contains(" ") == true && !item.ExePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) isPeFile = true;
                }
                catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }

                foreach (var flyoutItem in menu.Items)
                {
                    if (flyoutItem is MenuFlyoutItem menuItem)
                    {
                        if (menuItem.Icon is SymbolIcon si)
                        {
                            menuItem.Text = si.Symbol switch
                            {
                                Symbol.Play => I18n.T("Menu_Run"),
                                Symbol.Repair => I18n.T("Menu_RunManager"),
                                Symbol.Folder => I18n.T("Menu_OpenFileLocation"),
                                Symbol.Edit => I18n.T("Menu_Properties"),
                                Symbol.Delete => I18n.T("Menu_Delete"),
                                _ => menuItem.Text
                            };

                            if (si.Symbol == Symbol.Folder)
                            {
                                menuItem.Visibility = isPeFile ? Visibility.Visible : Visibility.Collapsed;
                            }

                            if (si.Symbol == Symbol.Repair)
                            {
                                menuItem.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                }

                var toRemove = menu.Items.Where(i =>
                    i.Tag is CustomMenuItem ||
                    (i is MenuFlyoutSeparator sep && sep.Name == "DynamicSeparator") ||
                    (i.Tag as string == "DynamicManager")
                ).ToList();
                foreach (var r in toRemove) menu.Items.Remove(r);

                int insertIndex = 1;

                var platform = GamePlatformHelper.DetectPlatform(item.ExePath ?? "");
                var mgrPlatform = !string.IsNullOrEmpty(item.MgrPath) ? GamePlatformHelper.DetectPlatform(item.MgrPath) : null;
                bool isXbox = item.PlatformName == "Xbox";
                bool hasCustomMgr = !string.IsNullOrEmpty(item.MgrPath);

                if (hasCustomMgr || platform != null || isXbox)
                {
                    var mgrItem = new MenuFlyoutItem
                    {
                        Tag = "DynamicManager",
                        DataContext = item,
                        Icon = new SymbolIcon(Symbol.Repair)
                    };

                    if (mgrPlatform != null)
                    {
                        mgrItem.Text = string.Format(I18n.T("Menu_PlatformManager"), mgrPlatform.PlatformName);
                    }
                    else if (platform != null || isXbox)
                    {
                        string pName = isXbox ? "Xbox" : (platform?.PlatformName ?? "");
                        mgrItem.Text = string.Format(I18n.T("Menu_PlatformManager"), pName);
                    }
                    else if (hasCustomMgr)
                    {
                        mgrItem.Text = I18n.T("Menu_RunManager");
                    }

                    mgrItem.Click += MenuRunMgr_Click;
                    menu.Items.Insert(insertIndex++, mgrItem);
                }

                var customItems = item.GetCustomMenuItems();
                if (customItems.Count > 0)
                {
                    menu.Items.Insert(insertIndex++, new MenuFlyoutSeparator { Name = "DynamicSeparator" });
                    foreach (var ci in customItems)
                    {
                        var menuItem = new MenuFlyoutItem
                        {
                            Text = ci.Title,
                            Tag = ci,
                            Icon = new SymbolIcon(Symbol.Tag)
                        };
                        menuItem.Click += MenuCustom_Click;
                        menu.Items.Insert(insertIndex++, menuItem);
                    }
                }
            }
        }


        private void MenuCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CustomMenuItem ci)
                {
                    if (!string.IsNullOrEmpty(ci.Command))
                    {
                        LogService.Write("App", $"MenuCustom_Click command={ci.Command} isAdmin={ci.IsAdmin} title={ci.Title}");
                        if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                        _lastLaunchTime = DateTime.Now;

                        RunProcess(ci.Command, ci.IsAdmin);
                        LogService.Write("App", "MenuCustom_Click invoked RunProcess");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuCustom_Click failed", ex); }
        }

        private void MenuRunMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item == null) return;

                string? managerPath = item.RuntimeManagerPath;

                if (string.IsNullOrEmpty(managerPath) && item.PlatformName == "Xbox")
                {
                    managerPath = "xbox://";
                }

                if (!string.IsNullOrEmpty(managerPath))
                {
                    LogService.Write("App", $"MenuRunMgr_Click managerPath={managerPath} itemId={item.Id} platform={item.PlatformName} isMgrAdmin={item.IsMgrAdmin}");
                    if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                    _lastLaunchTime = DateTime.Now;

                    TriggerItemLoadingAnimation(item);

                    RunProcess(managerPath, item.IsMgrAdmin);
                    LogService.Write("App", "MenuRunMgr_Click invoked RunProcess for manager");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuRunMgr_Click failed", ex); }
        }

        private void TriggerItemLoadingAnimation(AppItem item)
        {
            item.IsLoading = true;
            item.LoadingOpacity = 1.0;
            _ = Task.Delay(3000).ContinueWith(async _ => 
            {
                for (int i = 0; i <= 10; i++)
                {
                    DispatcherQueue.TryEnqueue(() => item.LoadingOpacity = 1.0 - (i / 10.0));
                    await Task.Delay(50);
                }
                DispatcherQueue.TryEnqueue(() => item.IsLoading = false);
            });
        }

        private void MenuLoc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogService.Write("App", "MenuLoc_Click invoked");
                var item = GetTag(sender);
                if (item != null && !string.IsNullOrEmpty(item.ExePath))
                {
                    LogService.Write("App", $"MenuLoc_Click itemId={item.Id} exePath={item.ExePath}");
                    string? dir = Path.GetDirectoryName(item.ExePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.ExePath}\"");
                        LogService.Write("App", $"MenuLoc_Click launched explorer for itemId={item.Id}");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuLoc_Click failed", ex); }
        }

        private void MenuDel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    LogService.Write("App", $"MenuDel_Click removing itemId={item.Id} title={item.Title}");
                    _allItems.Remove(item);
                    item.Status = (int)AppItemStatus.Recycled;
                    item.DeletedAt = null;
                    _recycleItems.Add(item);
                    SaveData();
                    RefreshView();
                    LogService.Write("App", $"MenuDel_Click completed for itemId={item.Id}");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuDel_Click failed", ex); }
        }

        private void MenuProp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    LogService.Write("App", $"MenuProp_Click opening props for itemId={item.Id} title={item.Title}");
                    OpenPropertyWindow(item);
                    LogService.Write("App", $"MenuProp_Click opened props for itemId={item.Id}");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuProp_Click failed", ex); }
        }

        private async void MenuScan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (LogService.StartOperation("Scan", "Scan"))
                {
                    LogService.Write("Scan", "Scan Start");
                await ConfigService.ReconstructMissingConfigAsync();

                ScannerResultPanel.Visibility = Visibility.Collapsed;
                ScannerLoadingPanel.Visibility = Visibility.Visible;
                ScannerNewGamesList.ItemsSource = null;
                ScannerExistingGamesList.ItemsSource = null;
                ScannerInvalidGamesList.ItemsSource = null;

                var dialogTask = ScannerDialog.ShowAsync();

                var scannedGames = await Task.Run(async () =>
                {
                    var games = new List<ScannedGame>();
                    games.AddRange(SteamHelper.GetAllInstalledGames());
                    games.AddRange(EpicGamesHelper.GetAllInstalledGames());
                    games.AddRange(await StoreHelper.GetAllInstalledGamesAsync());
                    return games;
                });

                LogService.Write("Scan", $"MenuScan_Click scannedGamesCount={scannedGames?.Count ?? 0}");

                bool canValidateSteam = !string.IsNullOrEmpty(SteamHelper.DetectSteamPath());
                bool canValidateEpic = !string.IsNullOrEmpty(EpicGamesHelper.DetectEpicManifestDir());

                var existingGames = new List<ScannedGame>();
                var newGames = new List<ScannedGame>();
                var allItems = _allItems.Concat(_recycleItems).ToList();

                if (scannedGames == null)
                {
                    LogService.Write("Scan", "MenuScan_Click aborted: no scanned games");
                    return;
                }

                foreach (var game in scannedGames)
                {
                    bool exists = false;

                    if (game.PlatformBadge == "Xbox")
                    {
                        string gameId = game.ExePath.Replace(LauncherConstants.UwpAppsFolderPrefix, "");
                        exists = allItems.Any(a => !string.IsNullOrEmpty(a.ExePath) && a.ExePath.Contains(gameId, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        static string NormalizePath(string? p)
                        {
                            try { LogService.Write("UI", $"NormalizePath called p={(p ?? "")}"); } catch { }
                            if (string.IsNullOrEmpty(p)) return string.Empty;
                            try { return Path.GetFullPath(p).ToUpperInvariant(); }
                            catch { return p.ToUpperInvariant(); }
                        }

                        string gameNorm = NormalizePath(game.ExePath);
                        exists = allItems.Any(a =>
                            (!string.IsNullOrEmpty(a.ExePath) && NormalizePath(a.ExePath) == gameNorm) ||
                            string.Equals(a.Title, game.Title, StringComparison.OrdinalIgnoreCase)
                        );
                    }

                    if (exists) existingGames.Add(game);
                    else newGames.Add(game);
                }

                LogService.Write("Scan", $"MenuScan_Click classification existing={existingGames.Count} new={newGames.Count}");

                var invalidGames = new List<ScannedGame>();
                void TrackInvalidGame(AppItem i, string? badge)
                {
                    invalidGames.Add(new ScannedGame
                    {
                        Title = i.Title ?? "Unknown",
                        ExePath = i.ExePath ?? string.Empty,
                        PlatformBadge = badge ?? "",
                        ItemId = i.Id
                    });
                }

                foreach (var item in _allItems)
                {
                    bool isFileOrDirExists = false;
                    string? pureExePath = item.ExePath;

                    if (!string.IsNullOrEmpty(item.ExePath))
                    {
                        try
                        {
                            string expandedPath = Environment.ExpandEnvironmentVariables(item.ExePath);
                            pureExePath = expandedPath;

                            if (File.Exists(pureExePath) || Directory.Exists(pureExePath))
                            {
                                isFileOrDirExists = true;
                            }
                        }
                        catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }
                    }

                    if (isFileOrDirExists) continue;

                    string? platformName = item.PlatformName;
                    if (platformName == "Steam" || platformName == "Epic Games" || platformName == "Xbox")
                    {
                        if (platformName == "Steam" && !canValidateSteam)
                        {
                            continue;
                        }

                        if (platformName == "Epic Games" && !canValidateEpic)
                        {
                            continue;
                        }

                        if (platformName == "Xbox")
                        {
                            if (!StoreHelper.IsAppInstalled(item.ExePath))
                            {
                                TrackInvalidGame(item, platformName);
                            }
                            continue;
                        }
                        
                        bool found = false;
                        foreach (var game in scannedGames)
                        {
                            if (game.PlatformBadge == platformName)
                            {
                                static string NormalizePath(string? p)
                                {
                                    if (string.IsNullOrEmpty(p)) return string.Empty;
                                    try { return Path.GetFullPath(p).ToUpperInvariant(); }
                                    catch { return p.ToUpperInvariant(); }
                                }
                                string itemNorm = NormalizePath(item.ExePath);
                                string gameNorm = NormalizePath(game.ExePath);
                                if ((!string.IsNullOrEmpty(item.ExePath) && itemNorm == gameNorm) ||
                                    string.Equals(item.Title, game.Title, StringComparison.OrdinalIgnoreCase))
                                {
                                    found = true; break;
                                }
                            }
                        }

                        if (!found)
                        {
                            TrackInvalidGame(item, platformName);
                        }
                    }
                    else
                    {
                        if (IsUserLaunchTargetInvalid(item.ExePath))
                        {
                            TrackInvalidGame(item, "User");
                        }
                    }
                }

                ScannerNewGamesList.ItemsSource = new ObservableCollection<ScannedGame>(newGames);
                ScannerExistingGamesList.ItemsSource = new ObservableCollection<ScannedGame>(existingGames);
                ScannerInvalidGamesList.ItemsSource = new ObservableCollection<ScannedGame>(invalidGames);

                ScannerNewGamesHeader.Text = string.Format(I18n.T("Scanner_NewGames"), newGames.Count);
                ScannerExistingGamesHeader.Text = string.Format(I18n.T("Scanner_ExistingGames"), existingGames.Count);
                ScannerInvalidGamesHeader.Text = string.Format(I18n.T("Scanner_InvalidGames") ?? "已失效 ({0})", invalidGames.Count);
                ScannerDeleteInvalidBtn.Content = I18n.T("Scanner_DeleteInvalid") ?? "删除所选项";

                ScannerNewGamesSection.Visibility = newGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ScannerExistingGamesSection.Visibility = existingGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ScannerInvalidGamesSection.Visibility = invalidGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    LogService.Write("Scan", "Scan Complete");
                }

                ScannerLoadingPanel.Visibility = Visibility.Collapsed;
                ScannerResultPanel.Visibility = Visibility.Visible;
            }
            catch (Exception ex) { LogService.Write("Scan", "Scan failed", ex); }
        }

        private void ScannerDialogCloseBtn_Click(object sender, RoutedEventArgs e)
        {
            ScannerDialog.Hide();
        }

        private void ScannerImportSelectedBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItems = ScannerNewGamesList.SelectedItems.Cast<ScannedGame>().ToList();
                if (selectedItems.Count == 0) return;

                ImportScannedGames(selectedItems);

                var source = ScannerNewGamesList.ItemsSource as ObservableCollection<ScannedGame>;
                if (source != null)
                {
                    foreach (var item in selectedItems)
                    {
                        source.Remove(item);
                    }
                    ScannerNewGamesHeader.Text = string.Format(I18n.T("Scanner_NewGames"), source.Count);
                    if (source.Count == 0)
                    {
                        ScannerNewGamesSection.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex) { LogService.Write("Scan", "ScannerImportSelectedBtn_Click failed", ex); }
        }

        private void ScannerNewGamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ScannerNewGamesList.Items.Count > 0 && ScannerNewGamesList.SelectedItems.Count == ScannerNewGamesList.Items.Count)
                {
                    ScannerNewSelectAllBtn.Content = I18n.T("Scanner_DeselectAll") ?? "Deselect All";
                }
                else
                {
                    ScannerNewSelectAllBtn.Content = I18n.T("Scanner_SelectAll") ?? "Select All";
                }
            }
            catch (Exception ex) { LogService.Write("App", "Swallowed exception", ex); }
        }

        private void ScannerNewSelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ScannerNewGamesList.Items.Count > 0 && ScannerNewGamesList.SelectedItems.Count == ScannerNewGamesList.Items.Count)
                {
                    ScannerNewGamesList.SelectedItems.Clear();
                }
                else
                {
                    ScannerNewGamesList.SelectAll();
                }
            }
            catch (Exception ex) { LogService.Write("App", "ScannerNewSelectAllBtn_Click failed", ex); }
        }

        private void ScannerInvalidGamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (ScannerInvalidGamesList.Items.Count > 0 && ScannerInvalidGamesList.SelectedItems.Count == ScannerInvalidGamesList.Items.Count)
                {
                    ScannerInvalidSelectAllBtn.Content = I18n.T("Scanner_DeselectAll") ?? "Deselect All";
                }
                else
                {
                    ScannerInvalidSelectAllBtn.Content = I18n.T("Scanner_SelectAll") ?? "Select All";
                }
            }
            catch (Exception ex) { LogService.Write("App", "ScannerInvalidSelectAllBtn_Click failed", ex); }
        }

        private void ScannerInvalidSelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ScannerInvalidGamesList.Items.Count > 0 && ScannerInvalidGamesList.SelectedItems.Count == ScannerInvalidGamesList.Items.Count)
                {
                    ScannerInvalidGamesList.SelectedItems.Clear();
                }
                else
                {
                    ScannerInvalidGamesList.SelectAll();
                }
            }
            catch (Exception ex) { LogService.Write("App", "ScannerInvalidSelectAllBtn_Click failed", ex); }
        }

        private void ScannerDeleteInvalidBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedInvalid = ScannerInvalidGamesList.SelectedItems.Cast<ScannedGame>().ToList();
                if (selectedInvalid.Count == 0) return;

                DeleteInvalidGames(selectedInvalid);
                
                var source = ScannerInvalidGamesList.ItemsSource as ObservableCollection<ScannedGame>;
                if (source != null)
                {
                    foreach (var item in selectedInvalid)
                    {
                        source.Remove(item);
                    }
                    ScannerInvalidGamesHeader.Text = string.Format(I18n.T("Scanner_InvalidGames") ?? "已失效 ({0})", source.Count);
                    if (source.Count == 0)
                    {
                        ScannerInvalidGamesSection.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "DeleteInvalidGames failed", ex); }
        }

        private void DeleteInvalidGames(List<ScannedGame> games)
        {
            try
            {
                bool deleted = false;
                var processedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                bool canValidateSteam = !string.IsNullOrEmpty(SteamHelper.DetectSteamPath());
                bool canValidateEpic = !string.IsNullOrEmpty(EpicGamesHelper.DetectEpicManifestDir());
                var steamInstalledUrls = canValidateSteam
                    ? new HashSet<string>(SteamHelper.GetAllInstalledGames().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var epicInstalledUrls = canValidateEpic
                    ? new HashSet<string>(EpicGamesHelper.GetAllInstalledGames().Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                #pragma warning disable CS8602
                foreach (var game in games)
                {
                    if (string.IsNullOrEmpty(game.ItemId) || !processedIds.Add(game.ItemId)) continue;

                    var itemToDelete = _allItems.FirstOrDefault(a => a.Id == game.ItemId);
                    if (itemToDelete == null) continue;

                    if (!IsItemStillInvalid(itemToDelete, canValidateSteam, canValidateEpic, steamInstalledUrls, epicInstalledUrls)) continue;

                    if (itemToDelete != null)
                    {
                        _allItems.Remove(itemToDelete);
                        itemToDelete.Status = (int)AppItemStatus.Recycled;
                        itemToDelete.DeletedAt = null;
                        _recycleItems.Add(itemToDelete);
                        deleted = true;
                    }
                }
                #pragma warning restore CS8602

                if (deleted)
                {
                    SaveData();
                }
            }
            catch (Exception ex) { LogService.Write("App", "DeleteInvalidGames failed", ex); }
        }

        private bool IsItemStillInvalid(AppItem item, bool canValidateSteam, bool canValidateEpic, HashSet<string> steamInstalledUrls, HashSet<string> epicInstalledUrls)
        {
            try
            {
                var exePath = item.ExePath;
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                if (item.PlatformName == "Xbox")
                {
                    return !StoreHelper.IsAppInstalled(exePath);
                }

                if (item.PlatformName == "Steam")
                {
                    if (!canValidateSteam) return false;
                    return !steamInstalledUrls.Contains(exePath);
                }

                if (item.PlatformName == "Epic Games")
                {
                    if (!canValidateEpic) return false;
                    return !epicInstalledUrls.Contains(exePath);
                }

                return IsUserLaunchTargetInvalid(exePath);
            }
            catch (Exception ex)
            {
                LogService.Write("App", "IsItemStillInvalid failed", ex);
                return false;
            }
        }

        private bool IsUserLaunchTargetInvalid(string? rawPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPath)) return false;

                string path = Environment.ExpandEnvironmentVariables(rawPath.Trim());

                if (path.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return !StoreHelper.IsAppInstalled(path);
                }

                if (path.Contains("://", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (File.Exists(path) || Directory.Exists(path))
                {
                    return false;
                }

                var (filePath, _) = SplitPathAndArguments(path);
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    string resolved = Environment.ExpandEnvironmentVariables(filePath.Trim());
                    if (File.Exists(resolved) || Directory.Exists(resolved))
                    {
                        return false;
                    }
                }

                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                {
                    return !File.Exists(path);
                }

                if (path.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    int exeIndex = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4;
                    string exeCandidate = path.Substring(0, exeIndex).Trim('\"', ' ', '\'');
                    return !File.Exists(exeCandidate);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void RestoreRecycledItem(AppItem item)
        {
            try
            {
                var removeItem = _recycleItems.FirstOrDefault(x => x.Id == item.Id);
                if (removeItem != null)
                {
                    _recycleItems.Remove(removeItem);
                }
                var existing = _allItems.FirstOrDefault(x => x.Id == item.Id);
                if (existing != null)
                {
                    _allItems.Remove(existing);
                }
                item.Status = (int)AppItemStatus.Normal;
                item.DeletedAt = null;
                _allItems.Add(item);
                SaveData();
                RefreshView();
            }
            catch (Exception ex) { LogService.Write("App", "RestoreRecycledItem failed", ex); }
        }

        private void MarkItemForDeletion(AppItem item)
        {
            try
            {
                item.Status = (int)AppItemStatus.PendingDeletion;
                item.DeletedAt = DateTimeOffset.UtcNow;
                SaveData();
            }
            catch (Exception ex) { LogService.Write("App", "MarkItemForDeletion failed", ex); }
        }

        private void EmptyRecycleBin()
        {
            try
            {
                var recycledItems = _recycleItems.Where(i => i.Status == (int)AppItemStatus.Recycled).ToList();
                var now = DateTimeOffset.UtcNow;
                foreach (var item in recycledItems)
                {
                    item.Status = (int)AppItemStatus.PendingDeletion;
                    item.DeletedAt = now;
                }
                
                if (recycledItems.Count > 0)
                {
                    SaveData();
                }
            }
            catch (Exception ex) { LogService.Write("App", "EmptyRecycleBin failed", ex); }
        }

        private void RecycleBinFlyout_Opening(object sender, object e)
        {
            try
            {
                var recycledItems = _recycleItems.Where(x => x.Status == (int)AppItemStatus.Recycled || x.Status == (int)AppItemStatus.PendingDeletion).ToList();
                RecycleItemsControl.ItemsSource = new ObservableCollection<AppItem>(recycledItems);
                
                foreach(var item in recycledItems)
                {
                    item.OnPropertyChanged("TimeRemainingText");
                    item.OnPropertyChanged("TimeBadgeVisibility");
                    item.OnPropertyChanged("TitleTextDecorations");
                }
            }
            catch (Exception ex) { LogService.Write("App", "RecycleBinFlyout_Opening failed", ex); }
        }

        private void RecycleBinFlyout_Closed(object sender, object e)
        {
            RecycleItemsControl.ItemsSource = null;
        }

        private void AutoCleanRecycleBin()
        {
            try
            {
                var items = ConfigService.LoadItems();
                var recycleItems = ConfigService.LoadRecycleBinItems();
                bool changed = false;
                var now = DateTimeOffset.UtcNow;
                var newRecycleItems = new List<AppItem>(recycleItems);
                foreach (var item in recycleItems)
                {
                    if (item.Status == (int)AppItemStatus.PendingDeletion && item.DeletedAt.HasValue)
                    {
                        if ((now - item.DeletedAt.Value).TotalHours >= 72)
                        {
                            newRecycleItems.Remove(item);
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    ConfigService.SaveItems(items, newRecycleItems);
                }
            }
            catch (Exception ex) { LogService.Write("App", "AutoCleanRecycleBin failed", ex); }
        }

        private void BtnEmptyRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("App", "BtnEmptyRecycleBin_Click invoked");
            EmptyRecycleBin();
            LogService.Write("App", "BtnEmptyRecycleBin_Click completed EmptyRecycleBin");
            RecycleBinFlyout_Opening(RecycleBinFlyout, new object());
        }

        private void RecycleMenuFlyout_Opening(object sender, object e)
        {
            if (sender is MenuFlyout flyout && RecycleItemsControl.SelectedItem is AppItem selectedItem)
            {
                LogService.Write("App", $"RecycleMenuFlyout_Opening selectedItemId={selectedItem.Id} status={selectedItem.Status}");
                if (flyout.Items.Count >= 2)
                {
                    if (flyout.Items[0] is MenuFlyoutItem restoreItem)
                        restoreItem.Text = I18n.T("RecycleBin_Restore");
                    if (flyout.Items[1] is MenuFlyoutItem deleteItem)
                    {
                        deleteItem.Text = I18n.T("RecycleBin_Delete");
                        deleteItem.Visibility = selectedItem.Status == (int)AppItemStatus.PendingDeletion ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
            }
        }

        private void MenuRestore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is AppItem item)
            {
                LogService.Write("App", $"MenuRestore_Click restoring itemId={item.Id} title={item.Title}");
                RestoreRecycledItem(item);
                LogService.Write("App", $"MenuRestore_Click restored itemId={item.Id}");
                RecycleBinFlyout_Opening(RecycleBinFlyout, new object());
            }
        }

        private void MenuDeletePerm_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is AppItem item)
            {
                LogService.Write("App", $"MenuDeletePerm_Click marking for deletion itemId={item.Id} title={item.Title}");
                MarkItemForDeletion(item);
                LogService.Write("App", $"MenuDeletePerm_Click marked itemId={item.Id}");
                RecycleBinFlyout_Opening(RecycleBinFlyout, new object());
            }
        }

        private void RecycleItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is Grid grid && grid.DataContext is AppItem item)
            {
                RecycleItemsControl.SelectedItem = item;
            }
        }

        private void ImportScannedGames(List<ScannedGame> games)
        {
            #pragma warning disable CS8602, CS8604
            try
            {
                using (LogService.StartOperation("Scan", "ImportScannedGames"))
                {
                        if (games == null || games.Count == 0)
                        {
                            LogService.Write("Scan", "ImportScannedGames aborted: no input");
                            return;
                        }

                        LogService.Write("Scan", $"ImportScannedGames Start inputCount={games?.Count ?? 0}");
                    var localGames = games!;
                    int sortOrder = 0;
                    if (_allItems != null && _allItems.Count > 0)
                    {
                        sortOrder = _allItems.Max(x => x.SortOrder) + 1;
                    }

                var newAppItems = new List<AppItem>();
                foreach (var game in games)
                {
                    var gTitle = game?.Title;
                    var gExe = game?.ExePath;
                    var gPlatform = game?.PlatformBadge;
                    var newItem = new AppItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = gTitle,
                        ExePath = gExe,
                        Platform = gPlatform,
                        SortOrder = sortOrder++
                    };

                    newAppItems.Add(newItem);
                    _allItems!.Add(newItem);

                    if (!string.IsNullOrEmpty(newItem.ExePath) &&
                        newItem.ExePath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string? icon = await IconHelper.GetIconPathAsync(newItem.ExePath, newItem.Id);
                                if (!string.IsNullOrEmpty(icon) && File.Exists(icon))
                                {
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        newItem.IconPath = icon;
                                        RefreshView();
                                    });
                                }
                            }
                            catch (Exception ex) { LogService.Write("App", "ImportScannedGames icon task failed", ex); }
                        });
                    }
                }

                    ConfigService.SaveItems(_allItems!.ToList(), _recycleItems!.ToList());
                    LogService.Write("Scan", $"ImportScannedGames completed newAdded={newAppItems.Count} totalItems={_allItems!.Count}");

                    _ = ConfigService.RefreshGlobalAsync();
                }
            }
            #pragma warning restore CS8602, CS8604
            catch (Exception ex) { LogService.Write("App", "ImportScannedGames failed", ex); }
        }


        private void LoadUI()
        {
            try
            {
                PropTitle.Text = _currentEditingItem!.Title ?? "";
                PropExePath.Text = _currentEditingItem.ExePath ?? "";
                PropIsAdmin.IsChecked = _currentEditingItem.IsAdmin;
                PropMgrPath.Text = _currentEditingItem.MgrPath ?? "";
                PropIsMgrAdmin.IsChecked = _currentEditingItem.IsMgrAdmin;
                PropDisplayNameLabel.Text = I18n.T("Property_DisplayName");

                string? exePathSnapshot = _currentEditingItem.ExePath;
                _ = Task.Run(async () =>
                {
                    var platform = await GamePlatformHelper.DetectPlatformAsync(exePathSnapshot ?? "");
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (platform != null)
                        {
                            PropPlatformBadge.Text = platform.PlatformName;
                            PropPlatformBadgeContainer.Visibility = Visibility.Visible;
                        }
                        else if (!string.IsNullOrEmpty(exePathSnapshot))
                        {
                            PropPlatformBadge.Text = "User";
                            PropPlatformBadgeContainer.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            PropPlatformBadgeContainer.Visibility = Visibility.Collapsed;
                        }
                    });
                });

                PropUseAlternativeLaunch.IsChecked = _currentEditingItem.UseAlternativeLaunch;
                PropAlternativeLaunchCommand.Text = _currentEditingItem.AlternativeLaunchCommand ?? "";
                PropIsAltAdmin.IsChecked = _currentEditingItem.IsAltAdmin;
                PropRunAlongside.IsChecked = _currentEditingItem.RunAlongside;
                PropAlongsideCommand.Text = _currentEditingItem.AlongsideCommand ?? "";
                PropIsAlongsideAdmin.IsChecked = _currentEditingItem.IsAlongsideAdmin;

                var customItems = _currentEditingItem.GetCustomMenuItems();
                for (int i = 0; i < 10; i++)
                {
                    if (i < customItems.Count)
                    {
                        _customTitles[i].Text = customItems[i].Title ?? "";
                        _customCommands[i].Text = customItems[i].Command ?? "";
                        _customAdmins[i].IsChecked = customItems[i].IsAdmin;
                    }
                    else
                    {
                        _customTitles[i].Text = "";
                        _customCommands[i].Text = "";
                        _customAdmins[i].IsChecked = false;
                    }
                }
                UpdateCustomVisibility();

                UpdatePropIconView(_currentEditingItem.IconPath);
            }
            catch (Exception ex) { LogService.Write("App", "ImportScannedGames failed", ex); }
        }

        private void SaveToItem()
        {
            try
            {
                _currentEditingItem!.Title = PropTitle.Text?.Trim() ?? "";



                string newExePath = PropExePath.Text?.Trim() ?? "";
                if (_currentEditingItem.ExePath != newExePath)
                {
                    _currentEditingItem.ExePath = newExePath;
                }

                _currentEditingItem.IsAdmin = PropIsAdmin.IsChecked ?? false;
                _currentEditingItem.IsAltAdmin = PropIsAltAdmin.IsChecked ?? false;
                _currentEditingItem.IsAlongsideAdmin = PropIsAlongsideAdmin.IsChecked ?? false;
                _currentEditingItem.IsMgrAdmin = PropIsMgrAdmin.IsChecked ?? false;
                _currentEditingItem.MgrPath = PropMgrPath.Text?.Trim() ?? "";


                _currentEditingItem.UseAlternativeLaunch = PropUseAlternativeLaunch.IsChecked ?? false;
                _currentEditingItem.AlternativeLaunchCommand = PropAlternativeLaunchCommand.Text?.Trim() ?? "";
                _currentEditingItem.RunAlongside = PropRunAlongside.IsChecked ?? false;
                _currentEditingItem.AlongsideCommand = PropAlongsideCommand.Text?.Trim() ?? "";

                var customItems = new List<CustomMenuItem>();
                for (int i = 0; i < 10; i++)
                {
                    string cmd = _customCommands[i].Text?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        customItems.Add(new CustomMenuItem
                        {
                            Title = _customTitles[i].Text?.Trim() ?? "",
                            Command = cmd,
                            IsAdmin = _customAdmins[i].IsChecked ?? false
                        });
                    }
                }
                _currentEditingItem.SetCustomMenuItems(customItems);
                string? detectedPlatform = PropPlatformBadge.Text?.Trim();
                if (!string.IsNullOrEmpty(detectedPlatform))
                    _currentEditingItem.Platform = detectedPlatform;





                if (string.IsNullOrEmpty(_currentEditingItem.ExePath))
                {
                }
            }
            catch (Exception ex) { LogService.Write("App", "LoadUI failed", ex); }
        }




        private void UpdatePropIconView(string? iconPath)
        {
            try
            {
                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                {
                    DispatcherQueue.TryEnqueue(() => { PropIcon.Source = null; });
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        long cacheKey = new FileInfo(iconPath).LastWriteTime.Ticks;
                        var uri = new Uri($"file:///{iconPath.Replace("\\", "/")}?t={cacheKey}");
                        bitmap.UriSource = uri;
                        PropIcon.Source = bitmap;
                    }
                    catch
                    {
                        PropIcon.Source = null;
                    }
                });
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => { PropIcon.Source = null; });
            }
        }

        private void RefreshIconDisplay(string iconPath)
        {
            UpdatePropIconView(iconPath);
        }

        private void OpenPropertyWindow(AppItem item)
        {
            try
            {
                _currentEditingItem = item;
                _isNewItemMode = false;


                LoadUI();


                PropBtnDelete.Visibility = Visibility.Visible;


                ShowPropertyPanel();
            }
            catch (Exception ex) { LogService.Write("App", "LoadUI failed", ex); }
        }

        private void ShowPropertyPanel()
        {
            PropertyPanel.Visibility = Visibility.Visible;
            PopulateShortcutMenus();

            var transform = new TranslateTransform { Y = -20 };
            PropertyPanel.RenderTransform = transform;
            PropertyPanel.Opacity = 0;

            var storyboard = new Storyboard();
            
            var moveAnimation = new DoubleAnimation
            {
                From = -20,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(moveAnimation, transform);
            Storyboard.SetTargetProperty(moveAnimation, "Y");
            storyboard.Children.Add(moveAnimation);

            var fadeAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(200))
            };
            Storyboard.SetTarget(fadeAnimation, PropertyPanel);
            Storyboard.SetTargetProperty(fadeAnimation, "Opacity");
            storyboard.Children.Add(fadeAnimation);

            storyboard.Begin();
        }

        private async void PopulateShortcutMenus()
        {
            using (LogService.StartOperation("App", "PopulateShortcutMenus"))
            {
            try
            {
                MenuExeStartMenu.Items.Clear();
                MenuExeDesktop.Items.Clear();
                MenuAltStartMenu.Items.Clear();
                MenuAltDesktop.Items.Clear();
                MenuAlongStartMenu.Items.Clear();
                MenuAlongDesktop.Items.Clear();
                MenuMgrStartMenu.Items.Clear();
                MenuMgrDesktop.Items.Clear();

                List<ShortcutScanner.FileItem> startMenuItems = _preloadedStartMenuTask != null
                    ? await _preloadedStartMenuTask
                    : await Task.Run(() => ShortcutScanner.GetStartMenuItems());
                _preloadedStartMenuTask = null;

                List<ShortcutScanner.FileItem> desktopItems = _preloadedDesktopTask != null
                    ? await _preloadedDesktopTask
                    : await Task.Run(() => ShortcutScanner.GetDesktopItems());
                _preloadedDesktopTask = null;

                LogService.Write("App", $"PopulateShortcutMenus fetched startMenuItems={startMenuItems?.Count ?? 0} desktopItems={desktopItems?.Count ?? 0}");

                MenuExeStartMenu.Items.Clear();
                MenuExeDesktop.Items.Clear();
                MenuAltStartMenu.Items.Clear();
                MenuAltDesktop.Items.Clear();
                MenuAlongStartMenu.Items.Clear();
                MenuAlongDesktop.Items.Clear();
                MenuMgrStartMenu.Items.Clear();
                MenuMgrDesktop.Items.Clear();

                PopulateMenuItems(MenuExeStartMenu, startMenuItems, PropExePath);
                PopulateMenuItems(MenuExeDesktop, desktopItems, PropExePath);
                PopulateMenuItems(MenuAltStartMenu, startMenuItems, PropAlternativeLaunchCommand);
                PopulateMenuItems(MenuAltDesktop, desktopItems, PropAlternativeLaunchCommand);
                PopulateMenuItems(MenuAlongStartMenu, startMenuItems, PropAlongsideCommand);
                PopulateMenuItems(MenuAlongDesktop, desktopItems, PropAlongsideCommand);
                PopulateMenuItems(MenuMgrStartMenu, startMenuItems, PropMgrPath);
                PopulateMenuItems(MenuMgrDesktop, desktopItems, PropMgrPath);

                LogService.Write("App", "PopulateShortcutMenus populated standard menus and custom browse flyouts");

                for (int i = 0; i < 10; i++)
                {
                    int index = i;
                    var flyout = new MenuFlyout();
                    var startMenuSub = new MenuFlyoutSubItem { Text = I18n.T("Source_StartMenu"), Icon = new FontIcon { Glyph = "\uE700" } };
                    var desktopSub = new MenuFlyoutSubItem { Text = I18n.T("Source_Desktop"), Icon = new FontIcon { Glyph = "\uE8FC" } };
                    var browseItem = new MenuFlyoutItem { Text = I18n.T("Property_BrowseFile"), Icon = new FontIcon { Glyph = "\uE8E5" } };

                    browseItem.Click += (s, e) => BtnBrowseCustom_Click(index);

                    PopulateMenuItems(startMenuSub, startMenuItems, _customCommands[i]);
                    PopulateMenuItems(desktopSub, desktopItems, _customCommands[i]);

                    LogService.Write("App", $"PopulateShortcutMenus custom browse index={index} startCount={startMenuItems?.Count ?? 0} desktopCount={desktopItems?.Count ?? 0}");

                    flyout.Items.Add(startMenuSub);
                    flyout.Items.Add(desktopSub);
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    flyout.Items.Add(browseItem);

                    _customBrowses[i].Flyout = flyout;
                }
            }
            catch (Exception ex) { LogService.Write("App", "PopulateShortcutMenus failed", ex); }
            }
        }

        private void BtnBrowseCustom_Click(int index)
        {
            BrowseFile(_customCommands[index], Win32FileDialog.BuildFilter(Win32FileDialog.FilterAll));
        }

        private void PopulateMenuItems(MenuFlyoutSubItem parent, List<ShortcutScanner.FileItem>? items, TextBox? targetTextBox)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (!item.IsFolder)
                {
                    var menuItem = new MenuFlyoutItem { Text = item.Name, Tag = item.FullPath };
                    LogService.Write("App", $"PopulateMenuItems adding item name={item.Name} path={item.FullPath} targetTextBox={(targetTextBox?.Name ?? "")}");
                    menuItem.Click += (s, e) => OnShortcutMenuItemClick(item.FullPath, targetTextBox, item.Name);
                    parent.Items.Add(menuItem);
                }
                    else if (item.IsFolder && item.Children.Count > 0)
                {
                    LogService.Write("App", $"PopulateMenuItems entering folder name={item.Name} childCount={item.Children.Count}");
                    PopulateMenuItems(parent, item.Children, targetTextBox);
                }
            }
        }

        private async void OnShortcutMenuItemClick(string filePath, TextBox? targetTextBox, string? displayName = null)
        {
            using (LogService.StartOperation("App", "OnShortcutMenuItemClick"))
            {
            try
            {
                LogService.Write("App", $"OnShortcutMenuItemClick Start filePath={filePath} displayName={displayName}");
                if (string.IsNullOrEmpty(filePath))
                {
                    LogService.Write("App", "OnShortcutMenuItemClick aborted: empty filePath");
                    return;
                }
                bool isStoreApp = filePath.StartsWith("shell:AppsFolder\\");


                string actualPath = filePath;
                ShortcutInfo? shortcutInfo = null;
                bool isUrlProtocol = false;
                bool extractFromLnk = false;

                if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    shortcutInfo = ShortcutResolver.GetShortcutInfo(filePath);
                    if (shortcutInfo != null)
                    {
                        if (!string.IsNullOrEmpty(shortcutInfo.AUMID))
                        {
                            actualPath = $"shell:AppsFolder\\{shortcutInfo.AUMID}";
                        }
                        else if (shortcutInfo.IsUrl && !string.IsNullOrEmpty(shortcutInfo.ActualUrl))
                        {
                            actualPath = shortcutInfo.ActualUrl;
                            isUrlProtocol = true;
                            extractFromLnk = true;
                        }
                        else if (!string.IsNullOrEmpty(shortcutInfo.TargetPath))
                        {
                            actualPath = shortcutInfo.TargetPath;
                        }
                    }
                }
                else if (filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                {
                    var urlInfo = ShortcutResolver.GetUrlFileInfo(filePath);
                    if (urlInfo != null && !string.IsNullOrEmpty(urlInfo.ActualUrl))
                    {
                        actualPath = urlInfo.ActualUrl;
                        isUrlProtocol = true;
                        extractFromLnk = true;

                        shortcutInfo = urlInfo;
                    }
                }

                LogService.Write("App", $"OnShortcutMenuItemClick resolved actualPath={actualPath} isStoreApp={isStoreApp} isUrlProtocol={isUrlProtocol} extractFromLnk={extractFromLnk}");
                if (shortcutInfo != null)
                {
                    LogService.Write("App", $"OnShortcutMenuItemClick shortcutInfo AUMID={shortcutInfo.AUMID} TargetPath={shortcutInfo.TargetPath} IconPath={shortcutInfo.IconPath} IsUrl={shortcutInfo.IsUrl}");
                }

                if (targetTextBox != null) targetTextBox.Text = actualPath;


                if (targetTextBox == PropExePath)
                {
                    if (string.IsNullOrEmpty(PropTitle.Text) || _isNewItemMode)
                    {
                        PropTitle.Text = displayName ?? Path.GetFileNameWithoutExtension(filePath);
                    }

                    _currentEditingItem!.ExePath = actualPath;


                    PropIcon.Source = null;
                    string? iconPath = null;


                    if (actualPath.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                    {
                        string? steamExePath = SteamHelper.GetExecutableFromSteamUrl(actualPath);
                        if (!string.IsNullOrEmpty(steamExePath) && File.Exists(steamExePath))
                        {
                            LogService.Write("App", $"OnShortcutMenuItemClick extracting icon from steamExePath={steamExePath}");
                            iconPath = await IconHelper.GetIconPathAsync(steamExePath, _currentEditingItem.Id, forceExtract: true);
                            LogService.Write("App", $"OnShortcutMenuItemClick steam iconPath={iconPath}");
                        }
                        else
                        {
                        }
                    }
                    else
                    {

                        string iconSource = filePath;
                        bool shouldExtractFromLnk = extractFromLnk;

                        if (actualPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                        {
                            iconSource = actualPath;
                            shouldExtractFromLnk = false;
                        }

                        else if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath))
                        {
                            iconSource = shortcutInfo.IconPath;
                            shouldExtractFromLnk = false;
                        }

                        else if (!isUrlProtocol && !string.IsNullOrEmpty(actualPath) && File.Exists(actualPath))
                        {
                            iconSource = actualPath;
                            shouldExtractFromLnk = false;
                        }

                        else if (isUrlProtocol && (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                        {
                            iconSource = filePath;
                            shouldExtractFromLnk = true;
                        }

                        LogService.Write("App", $"OnShortcutMenuItemClick calling GetIconPathAsync iconSource={iconSource} shouldExtractFromLnk={shouldExtractFromLnk}");
                        iconPath = await IconHelper.GetIconPathAsync(iconSource, _currentEditingItem.Id, forceExtract: true);
                        LogService.Write("App", $"OnShortcutMenuItemClick GetIconPathAsync returned iconPath={iconPath}");
                    }

                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        _currentEditingItem.IconPath = null;
                        _currentEditingItem.IconPath = iconPath;
                        RefreshIconDisplay(iconPath);
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "OnShortcutMenuItemClick failed", ex); }
            }
        }

        private void HidePropertyPanel()
        {
            var transform = PropertyPanel.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform { Y = 0 };
                PropertyPanel.RenderTransform = transform;
            }

            var storyboard = new Storyboard();

            var moveAnimation = new DoubleAnimation
            {
                From = 0,
                To = -20,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(moveAnimation, transform);
            Storyboard.SetTargetProperty(moveAnimation, "Y");
            storyboard.Children.Add(moveAnimation);

            var fadeAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150))
            };
            Storyboard.SetTarget(fadeAnimation, PropertyPanel);
            Storyboard.SetTargetProperty(fadeAnimation, "Opacity");
            storyboard.Children.Add(fadeAnimation);

            storyboard.Completed += (s, e) =>
            {
                PropertyPanel.Visibility = Visibility.Collapsed;
                PropertyPanel.RenderTransform = null;
                PropertyPanel.Opacity = 1;
                _currentEditingItem = null;
                _isNewItemMode = false;
            };

            storyboard.Begin();
        }

        private void BtnCloseProperty_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                HidePropertyPanel();
            }
            catch (Exception ex) { LogService.Write("App", "BtnCloseProperty_Click failed", ex); }
        }

        private async void BtnSaveProperty_Click(object sender, RoutedEventArgs e)
        {
            using (LogService.StartOperation("App", "BtnSaveProperty_Click"))
            {
            try
            {
                LogService.Write("App", $"BtnSaveProperty_Click Start currentEditingItemId={_currentEditingItem?.Id} title={_currentEditingItem?.Title}");
                if (_currentEditingItem == null)
                {
                    LogService.Write("App", "BtnSaveProperty_Click aborted: no current editing item");
                    return;
                }


                if (string.IsNullOrWhiteSpace(PropExePath.Text))
                {
                    LogService.Write("App", "BtnSaveProperty_Click validation failed: empty PropExePath");
                    PropExePath.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                    _ = Task.Delay(2000).ContinueWith(_ =>
                        DispatcherQueue.TryEnqueue(() => PropExePath.ClearValue(TextBox.BorderBrushProperty)),
                        TaskScheduler.Default);
                    PropExePath.Focus(FocusState.Programmatic);
                    return;
                }


                if (_isNewItemMode)
                {
                    var existing = _allItems.Concat(_recycleItems).FirstOrDefault(x =>
                        !string.IsNullOrEmpty(x.ExePath) &&
                        x.ExePath.Equals(PropExePath.Text, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        LogService.Write("App", $"BtnSaveProperty_Click validation failed: duplicate ExePath existingId={existing.Id}");
                        PropExePath.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                        _ = Task.Delay(2000).ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => PropExePath.ClearValue(TextBox.BorderBrushProperty)),
                            TaskScheduler.Default);
                        PropExePath.Focus(FocusState.Programmatic);
                        return;
                    }
                }


                SaveToItem();

                LogService.Write("App", $"BtnSaveProperty_Click SaveToItem applied for id={_currentEditingItem.Id} title={_currentEditingItem.Title} exePath={_currentEditingItem.ExePath}");


                if (_isNewItemMode)
                {
                    _allItems.Add(_currentEditingItem);
                }
                else
                {
                }

                SaveData();
                LogService.Write("App", $"BtnSaveProperty_Click SaveData completed itemsCount={_allItems.Count}");
                HidePropertyPanel();
            }
            catch (Exception ex) { LogService.Write("App", "BtnSaveProperty_Click failed", ex); }
            }
        }

        private void BtnDeleteProperty_Click(object sender, RoutedEventArgs e)
        {
            using (LogService.StartOperation("App", "BtnDeleteProperty_Click"))
            {
            try
            {
                LogService.Write("App", $"BtnDeleteProperty_Click Start currentEditingItemId={_currentEditingItem?.Id}");
                if (_currentEditingItem == null) return;

                if (!_isNewItemMode)
                {
                    _allItems.Remove(_currentEditingItem);
                    _currentEditingItem.Status = (int)AppItemStatus.Recycled;
                    _currentEditingItem.DeletedAt = null;
                    _recycleItems.Add(_currentEditingItem);
                    LogService.Write("App", $"BtnDeleteProperty_Click recycled itemId={_currentEditingItem.Id}");
                }
                else
                {
                    _allItems.Remove(_currentEditingItem);
                    LogService.Write("App", $"BtnDeleteProperty_Click removed new temporary item");
                }
                
                SaveData();
                RefreshView();
                HidePropertyPanel();
            }
            catch (Exception ex) { LogService.Write("App", "BtnDeleteProperty_Click failed", ex); }
            }
        }

        private async void BtnChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            using (LogService.StartOperation("App", "BtnChangeIcon_Click"))
            {
            try
            {

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string filter = Win32FileDialog.BuildFilter(Win32FileDialog.FilterExecutablesAndImages, Win32FileDialog.FilterAll);
                string? filePath = Win32FileDialog.ShowOpenFileDialog(hwnd, I18n.T("FileDialog_SelectIconFile"), filter);

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {

                    LogService.Write("App", $"BtnChangeIcon_Click selectedFile={filePath}");


                    PropIcon.Source = null;


                    string? newPath = null;
                    try
                    {
                        if (_currentEditingItem == null)
                        {
                            LogService.Write("App", "BtnChangeIcon_Click aborted: no current editing item");
                            return;
                        }
                        newPath = await IconHelper.GetIconPathAsync(filePath, _currentEditingItem.Id, forceExtract: true);
                        LogService.Write("App", $"BtnChangeIcon_Click GetIconPathAsync returned newPath={newPath}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("App", "BtnChangeIcon_Click GetIconPathAsync failed", ex);
                    }

                    if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                    {
                        if (_currentEditingItem != null)
                        {
                            _currentEditingItem.IconPath = null;
                            _currentEditingItem.IconPath = newPath;
                            UpdatePropIconView(newPath);
                            LogService.Write("App", $"BtnChangeIcon_Click updated item icon id={_currentEditingItem.Id} newPath={newPath}");
                        }
                        else
                        {
                            LogService.Write("App", "BtnChangeIcon_Click: current editing item became null before update");
                        }
                    }
                    else
                    {
                        LogService.Write("App", $"BtnChangeIcon_Click no valid icon produced for selectedFile={filePath} newPath={newPath}");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "UpdatePropIconView failed", ex); }
            }
        }




        private async void BrowseFile(Microsoft.UI.Xaml.Controls.TextBox target, string? filter = null)
        {
            using (LogService.StartOperation("App", "BrowseFile"))
            {
            try
            {
                if (target == null)
                {
                    return;
                }


                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (string.IsNullOrEmpty(filter))
                {
                    filter = Win32FileDialog.BuildFilter(Win32FileDialog.FilterExecutables, Win32FileDialog.FilterAll);
                }
                LogService.Write("App", $"BrowseFile invoking dialog filter={filter}");
                string? filePath = Win32FileDialog.ShowOpenFileDialog(hwnd, I18n.T("FileDialog_SelectFile"), filter);
                LogService.Write("App", $"BrowseFile dialog returned filePath={filePath}");

                if (!string.IsNullOrEmpty(filePath))
                {

                    string actualPath = filePath;
                    bool isUrlProtocol = false;
                    ShortcutInfo? shortcutInfo = null;

                    if (filePath.ToLower().EndsWith(".lnk"))
                    {
                        shortcutInfo = ShortcutResolver.GetShortcutInfo(filePath);
                        if (shortcutInfo != null)
                        {
                            if (!string.IsNullOrEmpty(shortcutInfo.AUMID))
                            {
                                actualPath = $"shell:AppsFolder\\{shortcutInfo.AUMID}";
                            }
                            else if (shortcutInfo.IsUrl)
                            {
                                actualPath = shortcutInfo.ActualUrl ?? shortcutInfo.TargetPath ?? filePath;
                                isUrlProtocol = true;
                            }
                            else if (!string.IsNullOrEmpty(shortcutInfo.TargetPath))
                            {
                                actualPath = shortcutInfo.TargetPath;
                            }
                        }
                    }

                    else if (filePath.ToLower().EndsWith(".url"))
                    {
                        shortcutInfo = ShortcutResolver.GetUrlFileInfo(filePath);
                        if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.ActualUrl))
                        {
                            actualPath = shortcutInfo.ActualUrl;
                            isUrlProtocol = true;
                        }
                        else
                        {
                        }
                    }

                    LogService.Write("App", $"BrowseFile resolved actualPath={actualPath} isUrlProtocol={isUrlProtocol}");
                    if (shortcutInfo != null)
                    {
                        LogService.Write("App", $"BrowseFile shortcutInfo AUMID={shortcutInfo.AUMID} TargetPath={shortcutInfo.TargetPath} IconPath={shortcutInfo.IconPath} IsUrl={shortcutInfo.IsUrl}");
                    }
                    target.Text = actualPath;


                    if (target == PropExePath)
                    {
                        if (string.IsNullOrEmpty(PropTitle.Text) || _isNewItemMode)
                        {
                            bool isNonExeFile = filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                                filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
                            string fileName = Path.GetFileNameWithoutExtension(isNonExeFile ? filePath : actualPath);
                            PropTitle.Text = fileName;
                        }

                        _currentEditingItem!.ExePath = actualPath;


                        PropIcon.Source = null;
                        string? iconPath = null;


                        if (actualPath.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                        {
                            string? steamExePath = SteamHelper.GetExecutableFromSteamUrl(actualPath);
                            if (!string.IsNullOrEmpty(steamExePath) && File.Exists(steamExePath))
                            {
                                LogService.Write("App", $"BrowseFile extracting steam icon from {steamExePath}");
                                iconPath = await IconHelper.GetIconPathAsync(steamExePath, _currentEditingItem.Id, forceExtract: true);
                                LogService.Write("App", $"BrowseFile steam iconPath={iconPath}");
                            }
                            else
                            {
                            }
                        }
                        else
                        {

                            string iconSource = filePath;

                            if (actualPath.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
                            {
                                iconSource = actualPath;
                            }

                            else if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath))
                            {
                                iconSource = shortcutInfo.IconPath;
                            }

                            else if (!isUrlProtocol && !string.IsNullOrEmpty(actualPath) && File.Exists(actualPath))
                            {
                                iconSource = actualPath;
                            }

                            else if (isUrlProtocol && (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                            {
                                iconSource = filePath;
                            }
                            LogService.Write("App", $"BrowseFile calling GetIconPathAsync iconSource={iconSource}");
                            iconPath = await IconHelper.GetIconPathAsync(iconSource, _currentEditingItem.Id, forceExtract: true);
                            LogService.Write("App", $"BrowseFile GetIconPathAsync returned iconPath={iconPath}");
                        }

                        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        {
                            _currentEditingItem.IconPath = null;
                            _currentEditingItem.IconPath = iconPath;
                            RefreshIconDisplay(iconPath);
                        }
                        else
                        {
                        }
                    }
                }
                else
                {
                }
            }
            catch (Exception ex) { LogService.Write("App", "SearchFlyout_Opened failed", ex); }
            }
        }

        private void BtnBrowseExe_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropExePath); } catch (Exception ex) { LogService.Write("App", "BtnBrowseExe_Click inner BrowseFile failed", ex); }
        }

        private void BtnBrowseAlt_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropAlternativeLaunchCommand); } catch (Exception ex) { LogService.Write("App", "BtnBrowseAlt_Click inner BrowseFile failed", ex); }
        }

        private void BtnBrowseAlongside_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropAlongsideCommand); } catch (Exception ex) { LogService.Write("App", "BtnBrowseAlongside_Click inner BrowseFile failed", ex); }
        }

        private void BtnBrowseMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BrowseFile(PropMgrPath);
            }
            catch (Exception ex) { LogService.Write("App", "BtnBrowseMgr_Click failed", ex); }
        }


        private void SearchFlyout_Opened(object sender, object e)
        {
            try
            {
                SearchBoxFlyout.Focus(FocusState.Programmatic);
            }
            catch (Exception ex) { LogService.Write("App", "SearchFlyout_Closing failed", ex); }
        }

        private void AnnouncementFlyout_Opened(object sender, object e)
        {
            try
            {
                RefreshAnnouncementList();
                LogService.Write("Announcement", "Announcement flyout opened");
            }
            catch (Exception ex) { LogService.Write("Announcement", "AnnouncementFlyout_Opened failed", ex); }
        }

        private void AnnouncementsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                if (e.ClickedItem is not AnnouncementListItem item) return;

                foreach (var row in _announcementItems)
                {
                    if (!ReferenceEquals(row, item) && row.IsExpanded)
                    {
                        row.IsExpanded = false;
                    }
                }

                item.IsExpanded = !item.IsExpanded;

                if (!item.IsRead)
                {
                    ServerConfigManager.MarkAsRead(item.Id, notify: false);
                    item.IsRead = true;
                }

                int unreadCount = _announcementItems.Count(x => !x.IsRead);
                UpdateAnnouncementButtonIndicator(unreadCount);
                LogService.Write("Announcement", $"Announcement clicked id={item.Id}");
            }
            catch (Exception ex) { LogService.Write("Announcement", "AnnouncementsListView_ItemClick failed", ex); }
        }

        private void OnAnnouncementsUpdated()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshAnnouncementList();
                });
            }
            catch (Exception ex) { LogService.Write("Announcement", "OnAnnouncementsUpdated failed", ex); }
        }

        private void RefreshAnnouncementList()
        {
            try
            {
                var activeAnnouncements = ServerConfigManager.GetActiveAnnouncements();
                _announcementItems = new ObservableCollection<AnnouncementListItem>(activeAnnouncements.Select(AnnouncementListItem.FromAnnouncement));

                if (AnnouncementsListView != null)
                {
                    AnnouncementsListView.ItemsSource = _announcementItems;
                    AnnouncementsListView.Visibility = _announcementItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (AnnouncementsEmptyText != null)
                {
                    AnnouncementsEmptyText.Visibility = _announcementItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                }

                int unreadCount = _announcementItems.Count(x => !x.IsRead);
                UpdateAnnouncementButtonIndicator(unreadCount);
                ToolTipService.SetToolTip(AnnouncementButton, I18n.T("Menu_Announcements"));

                LogService.Write("Announcement", $"Announcement list refreshed total={_announcementItems.Count} unread={unreadCount}");
            }
            catch (Exception ex) { LogService.Write("Announcement", "RefreshAnnouncementList failed", ex); }
        }
        private void BodyRichText_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
            if (sender is not Microsoft.UI.Xaml.Controls.RichTextBlock rich) return;
                rich.Blocks.Clear();
                if (rich.DataContext is not AnnouncementListItem item) return;

                string text = item.Body ?? string.Empty;
                var regex = new System.Text.RegularExpressions.Regex(@"(https?://[\w\-\./?%&=#]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
                int lastIndex = 0;
                var paragraph = new Microsoft.UI.Xaml.Documents.Paragraph();

                foreach (System.Text.RegularExpressions.Match m in regex.Matches(text))
                {
                    if (m.Index > lastIndex)
                    {
                        paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text.Substring(lastIndex, m.Index - lastIndex) });
                    }

                    string url = m.Value;
                    var link = new Microsoft.UI.Xaml.Documents.Hyperlink();
                    link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = url });
                    string capturedUrl = url;
                    link.Click += async (s, ev) =>
                    {
                        try { await Windows.System.Launcher.LaunchUriAsync(new System.Uri(capturedUrl)); } catch { }
                    };
                    paragraph.Inlines.Add(link);
                    lastIndex = m.Index + m.Length;
                }

                if (lastIndex < text.Length)
                    paragraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = text.Substring(lastIndex) });

                rich.Blocks.Add(paragraph);
            }
            catch (Exception ex) { LogService.Write("Announcement", "BodyRichText_Loaded failed", ex); }
        }

        private void UpdateAnnouncementButtonIndicator(int unreadCount)
        {
            if (AnnouncementButtonIcon == null) return;

            if (unreadCount > 0)
            {
                AnnouncementButtonIcon.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
            }
            else
            {
                AnnouncementButtonIcon.ClearValue(FontIcon.ForegroundProperty);
            }
        }

        private void SearchFlyout_Closing(Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase sender, Microsoft.UI.Xaml.Controls.Primitives.FlyoutBaseClosingEventArgs args)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(SearchBoxFlyout.Text))
                {
                    args.Cancel = true;
                }
            }
            catch (Exception ex) { LogService.Write("App", "SearchBox_TextChanged failed", ex); }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (sender == null) return;

                string query = (sender.Text ?? "").ToLower().Trim();

                IEnumerable<AppItem> filtered = string.IsNullOrEmpty(query)
                    ? _allItems
                    : _allItems.Where(item =>
                        (!string.IsNullOrEmpty(item.Title) && item.Title.ToLower().Contains(query)) ||
                        (!string.IsNullOrEmpty(item.ExePath) && item.ExePath.ToLower().Contains(query)) ||
                        (!string.IsNullOrEmpty(item.TitlePinyin) && item.TitlePinyin.Contains(query)) ||
                        (!string.IsNullOrEmpty(item.TitlePinyinInitial) && item.TitlePinyinInitial.Contains(query)) ||
                        (!string.IsNullOrEmpty(item.TitleEnglishInitial) && item.TitleEnglishInitial.Contains(query)));

                _viewItems = new ObservableCollection<AppItem>(filtered);
                AppGrid.ItemsSource = _viewItems;

                IsFiltered = !string.IsNullOrEmpty(query);
                UpdateEmptyState();
            }
            catch (Exception ex) { LogService.Write("App", "EditOrderFlyout_Opening failed", ex); }
        }

        private void EditOrderFlyout_Opening(object sender, object e)
        {
            try
            {
                _tempOrderCollection = new ObservableCollection<AppItem>(_allItems);
                OrderItemsControl.ItemsSource = _tempOrderCollection;
                _orderItemsControl = OrderItemsControl as ListView;
            }
            catch (Exception ex) { LogService.Write("App", "OrderList_ContainerContentChanging failed", ex); }
        }

        private void OrderList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            try
            {
                if (args.InRecycleQueue) return;
                if (args.ItemContainer is ListViewItem lvi)
                {
                    var sortButtons = FindChildByName(lvi, "SortButtons") as StackPanel;
                    if (sortButtons != null && sortButtons.Children.Count >= 2)
                    {
                        if (sortButtons.Children[0] is Button moveUpBtn)
                            ToolTipService.SetToolTip(moveUpBtn, I18n.T("Sort_MoveUp"));
                        if (sortButtons.Children[1] is Button moveDownBtn)
                            ToolTipService.SetToolTip(moveDownBtn, I18n.T("Sort_MoveDown"));
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "BtnMoveUp_Click failed", ex); }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.Tag as AppItem;
                MoveItem(item, -1);
            }
            catch (Exception ex) { LogService.Write("App", "BtnMoveDown_Click failed", ex); }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.Tag as AppItem;
                MoveItem(item, 1);
            }
            catch (Exception ex) { LogService.Write("App", "EditOrderFlyout_Closed failed", ex); }
        }

        private void EditOrderFlyout_Closed(object sender, object e)
        {
            try
            {
                if (_tempOrderCollection != null)
                {
                    var newOrder = _tempOrderCollection.ToList();
                    ConfigService.SaveItems(newOrder, _recycleItems.ToList());
                    RefreshView();
                    _tempOrderCollection = null;
                }
            }
            catch (Exception ex) { LogService.Write("App", "EditOrderFlyout_Closed failed", ex); }
        }

        private void MoveItem(AppItem? item, int offset)
        {
            if (item == null || _tempOrderCollection == null) return;

            int index = _tempOrderCollection.IndexOf(item);
            if (index == -1) return;

            int newIndex = index + offset;

            if (newIndex >= 0 && newIndex < _tempOrderCollection.Count)
            {
                _tempOrderCollection.Move(index, newIndex);

                if (_orderItemsControl != null)
                {
                    _orderItemsControl.SelectedItem = item;
                    _orderItemsControl.ScrollIntoView(item);
                }
            }
        }

        private void OrderList_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (sender is ListView listView && listView.SelectedItem is AppItem item)
            {
                if (e.Key == Windows.System.VirtualKey.W || e.Key == Windows.System.VirtualKey.Up)
                {
                    MoveItem(item, -1);
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.S || e.Key == Windows.System.VirtualKey.Down)
                {
                    MoveItem(item, 1);
                    e.Handled = true;
                }
            }
        }

        private void OrderItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var panel = FindChildByName(grid, "SortButtons") as StackPanel;
                if (panel != null) panel.Opacity = 1;
            }
        }

        private void OrderItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Grid grid)
            {
                var panel = FindChildByName(grid, "SortButtons") as StackPanel;
                if (panel != null) panel.Opacity = 0;
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new AppItem
            {
                Id = Guid.NewGuid().ToString("N")[..16],
                Title = string.Empty,
            };

            _currentEditingItem = newItem;
            _isNewItemMode = true;

            LoadUI();

            PropBtnDelete.Visibility = Visibility.Collapsed;

            ShowPropertyPanel();
        }


        private void AuthorLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/EricZhang233/EricGameLauncher",
                UseShellExecute = true
            });
        }

        private void MenuIconSize_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuIconSize_Click invoked");
            SizeFlyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuIconSize_Click showed SizeFlyout");
        }

        private void MenuSort_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuSort_Click invoked");
            EditOrderFlyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuSort_Click showed EditOrderFlyout");
        }

        private void MenuRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("App", "MenuRecycleBin_Click invoked");
            RecycleBinFlyout.ShowAt(BtnMore);
            LogService.Write("App", "MenuRecycleBin_Click showed RecycleBinFlyout");
        }


        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuSettings_Click invoked");
            UpdateStorageModeUI();


            if (ToggleCloseAfterLaunch != null)
            {
                ToggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
            }
            if (ComboUpdateChannel != null)
            {
                ComboUpdateChannel.SelectedIndex = ConfigService.UpdateChannel == "latest" ? 1 : 0;
            }
            if (SizeSlider != null)
            {
                SizeSlider.Value = ConfigService.IconSize;
            }

            SettingsFlyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuSettings_Click showed SettingsFlyout");
        }

        private void MenuInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (LogService.StartOperation("App", "Install"))
                {
                    LogService.Write("App", "Install Start");
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                string appName = "EricGameLauncher";
                string description = "Eric Game Launcher";

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcutPath = Path.Combine(desktopPath, $"{appName}.lnk");

                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string startMenuPath = Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs");
                string startMenuShortcutPath = Path.Combine(startMenuPath, $"{appName}.lnk");

                if (File.Exists(desktopShortcutPath)) File.Delete(desktopShortcutPath);
                if (File.Exists(startMenuShortcutPath)) File.Delete(startMenuShortcutPath);

                ShortcutResolver.CreateShortcut(exePath, desktopShortcutPath, description);

                if (!Directory.Exists(startMenuPath)) Directory.CreateDirectory(startMenuPath);
                ShortcutResolver.CreateShortcut(exePath, startMenuShortcutPath, description);
                    LogService.Write("App", "Install Complete");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuInstall_Click failed", ex); }
        }

        private void MenuUninstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using (LogService.StartOperation("App", "Uninstall"))
                {
                    LogService.Write("App", "Uninstall Start");
                string appName = "EricGameLauncher";

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcutPath = Path.Combine(desktopPath, $"{appName}.lnk");

                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string startMenuShortcutPath = Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs", $"{appName}.lnk");

                if (File.Exists(desktopShortcutPath)) File.Delete(desktopShortcutPath);
                if (File.Exists(startMenuShortcutPath)) File.Delete(startMenuShortcutPath);
                    LogService.Write("App", "Uninstall Complete");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuUninstall_Click failed", ex); }
        }


        private void ToggleCloseAfterLaunch_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                if (ConfigService.CloseAfterLaunch != toggle.IsOn)
                {
                    ConfigService.CloseAfterLaunch = toggle.IsOn;
                    ConfigService.SaveConfig();
                }
            }
        }

        private void ComboLaunchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboLaunchMode.SelectedItem is ComboBoxItem item && item.Tag is string val)
            {
                ConfigService.LaunchMode = val; AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";
                Task.Run(() => ConfigService.SaveConfig());
            }
        }

        private void ComboUpdateChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                string newChannel = combo.SelectedIndex == 1 ? "latest" : "stable";
                if (ConfigService.UpdateChannel != newChannel)
                {
                    ConfigService.UpdateChannel = newChannel;
                    ConfigService.SaveConfig();

                    _pendingUpdate = null;
                    HasUpdate = false;
                    _ = CheckForUpdatesQuietlyAsync(skipDelay: true);
                }
            }
        }

        private void BtnSizeDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (SizeSlider != null && SizeSlider.Value > SizeSlider.Minimum)
            {
                SizeSlider.Value -= 1;
            }
        }

        private void BtnSizeIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (SizeSlider != null && SizeSlider.Value < SizeSlider.Maximum)
            {
                SizeSlider.Value += 1;
            }
        }

        private void SizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                _sizeSlider = slider;
                IconSize = slider.Value;
                ConfigService.IconSize = slider.Value;
                LogService.Write("UI", $"SizeSlider_ValueChanged newSize={slider.Value}");
                ConfigService.SaveConfig();

                UpdateGridItemSizes(slider.Value);
            }
        }

        private void UpdateGridItemSizes(double size)
        {

            if (!_isRootLoaded || AppGrid == null || AppGrid.Items == null)
            {
                LogService.Write("UI", "UpdateGridItemSizes aborted: root not ready");
                return;
            }
            LogService.Write("UI", $"UpdateGridItemSizes start size={size} itemCount={AppGrid.Items.Count}");
            if (AppGrid.Items.Count == 0) return;

            foreach (var item in AppGrid.Items)
            {
                var container = AppGrid.ContainerFromItem(item);
                if (container is GridViewItem gvi)
                {
                    ApplySizeToContainer(gvi, size);
                }
            }
            LogService.Write("UI", "UpdateGridItemSizes applied to visible containers");
        }

        private void AppGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            if (args.ItemContainer is GridViewItem gvi)
                ApplySizeToContainer(gvi, IconSize);
        }

        private void ApplySizeToContainer(GridViewItem container, double size)
        {
            double cornerRadiusBg = size * 0.2;
            double cornerRadiusIcon = (size - 12) * 0.2;

            var panel = FindChildByName(container, "ItemPanel") as StackPanel;
            var iconGrid = FindChildByName(container, "IconGrid") as Grid;
            var bgBorder = FindChildByName(container, "IconBgBorder") as Border;
            var imgBorder = FindChildByName(container, "IconImgBorder") as Border;
            var titleText = FindChildByName(container, "TitleText") as TextBlock;

            if (panel != null) panel.Width = size;
            if (iconGrid != null) { iconGrid.Width = size; iconGrid.Height = size; }
            if (bgBorder != null) bgBorder.CornerRadius = new CornerRadius(cornerRadiusBg);
            if (imgBorder != null) imgBorder.CornerRadius = new CornerRadius(cornerRadiusIcon);
            if (titleText != null) titleText.Width = size;
            LogService.Write("UI", $"ApplySizeToContainer applied size={size} to containerName={(container?.Name ?? "")}");
        }

        private async void BtnSwitchStorageMode_Click(object sender, RoutedEventArgs e)
        {
            if (DebugPaths.IsDebug())
            {
                try { LogService.Write("Config", "BtnSwitchStorageMode_Click ignored in Debug mode"); } catch { }
                return;
            }
            using (LogService.StartOperation("Config", "BtnSwitchStorageMode_Click"))
            {
                try
                    {
                        bool switchToSystemMode = !ConfigService.IsSystemMode;
                        if (ToggleCloseAfterLaunch != null && ConfigService.CloseAfterLaunch != ToggleCloseAfterLaunch.IsOn)
                        {
                            ConfigService.CloseAfterLaunch = ToggleCloseAfterLaunch.IsOn;
                            LogService.Write("Config", $"ToggleCloseAfterLaunch changed to={ToggleCloseAfterLaunch.IsOn}");
                            ConfigService.SaveConfig();
                        }
                }
                catch (Exception ex) { LogService.Write("Config", "BtnSwitchStorageMode_Click failed", ex); }
            }
        }




        private void UpdateStorageModeUI()
        {
            string baseModeText;
            if (ConfigService.IsSystemMode)
            {
                baseModeText = I18n.T("Settings_SystemMode");
                ToolTipService.SetToolTip(BtnSwitchStorageMode, I18n.T("Settings_SwitchToPortable"));
            }
            else
            {
                baseModeText = I18n.T("Settings_PortableMode");
                ToolTipService.SetToolTip(BtnSwitchStorageMode, I18n.T("Settings_SwitchToSystem"));
            }
            var displayText = baseModeText ?? "";
            if (DebugPaths.IsDebug())
            {
                displayText = string.IsNullOrEmpty(displayText) ? "DebugMode" : displayText + " DebugMode";
                try { LogService.Write("Config", "UpdateStorageModeUI: Debug mode active"); } catch { }
                if (BtnSwitchStorageMode != null) BtnSwitchStorageMode.Visibility = Visibility.Collapsed;
                ToolTipService.SetToolTip(BtnSwitchStorageMode, "Debug mode: storage switching disabled");
            }
            else
            {
                if (BtnSwitchStorageMode != null) BtnSwitchStorageMode.Visibility = Visibility.Visible;
            }

            StorageModeText.Text = displayText;
            ToolTipService.SetToolTip(StorageModeText, ConfigService.CurrentDataPath);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = ConfigService.CurrentDataPath;
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder.Replace("\"", "\\\"")}\"",
                UseShellExecute = true
            });
        }

        private void BtnOpenCacheFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = ConfigService.SystemCachePath;
            if (!System.IO.Directory.Exists(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder.Replace("\"", "\\\"")}\"",
                UseShellExecute = true
            });
        }




        private T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                child = VisualTreeHelper.GetParent(child);
                if (child is T parent)
                {
                    return parent;
                }
            }
            return null;
        }




        private DependencyObject? FindChildByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement element && element.Name == name)
                    return child;

                var result = FindChildByName(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }


        private void ApplyLocalization()
        {
            try
            {
                AnnouncementsTitleText.Text = I18n.T("Announcements_Title");
                AnnouncementsEmptyText.Text = I18n.T("Announcements_None");
                ToolTipService.SetToolTip(SearchButton, I18n.T("TitleBar_Search"));
                SearchBoxFlyout.PlaceholderText = I18n.T("TitleBar_SearchPlaceholder");
                ToolTipService.SetToolTip(BtnMore, I18n.T("TitleBar_More"));
                MenuIconSizeItem.Text = I18n.T("Menu_IconSize");
                MenuAddItem.Text = I18n.T("Menu_Add");
                if (MenuScanItem != null) MenuScanItem.Text = I18n.T("Menu_Scan");
                if (ScannerDialog != null)
                {
                    if (ScannerDialogTitle != null) ScannerDialogTitle.Text = I18n.T("Scanner_Title");
                    ToolTipService.SetToolTip(ScannerDialogCloseBtn, I18n.T("Property_Close") ?? "Close");
                    if (ScannerImportSelectedBtn != null) ScannerImportSelectedBtn.Content = I18n.T("Scanner_ImportSelected");
                    if (ScannerNewSelectAllBtn != null) ScannerNewSelectAllBtn.Content = ScannerNewGamesList?.Items.Count > 0 && ScannerNewGamesList.SelectedItems.Count == ScannerNewGamesList.Items.Count ? I18n.T("Scanner_DeselectAll") : I18n.T("Scanner_SelectAll");
                    if (ScannerInvalidSelectAllBtn != null) ScannerInvalidSelectAllBtn.Content = ScannerInvalidGamesList?.Items.Count > 0 && ScannerInvalidGamesList.SelectedItems.Count == ScannerInvalidGamesList.Items.Count ? I18n.T("Scanner_DeselectAll") : I18n.T("Scanner_SelectAll");
                    if (ScannerLoadingText != null) ScannerLoadingText.Text = I18n.T("Scanner_Loading");
                    if (ScannerDescriptionText != null) ScannerDescriptionText.Text = I18n.T("Scanner_Description");
                }
                MenuSortItem.Text = I18n.T("Menu_Sort");
                MenuRecycleBinItem.Text = I18n.T("Menu_RecycleBin");
                if(RecycleTitle != null) RecycleTitle.Text = I18n.T("RecycleBin_Title");
                if(RecycleDescription != null) RecycleDescription.Text = I18n.T("RecycleBin_Desc");
                if(BtnEmptyRecycleBin != null) BtnEmptyRecycleBin.Content = I18n.T("RecycleBin_Empty");
                MenuSettingsItem.Text = I18n.T("Menu_Settings");
                MenuCheckUpdateItem.Text = I18n.T("Menu_CheckUpdate");
                MenuPrivacyItem.Text = I18n.T("Privacy_MenuTitle");
                MenuSystemIntegrationItem.Text = I18n.T("Menu_SystemIntegration");
                MenuInstallItem.Text = I18n.T("Menu_Install");
                MenuUninstallItem.Text = I18n.T("Menu_Uninstall");
                SizeFlyoutTitle.Text = I18n.T("Menu_IconSize");
                SortTitle.Text = I18n.T("Sort_Title");
                SortDescription.Text = I18n.T("Sort_Description");
                SettingsTitle.Text = I18n.T("Settings_Title");
                SettingsGeneralLabel.Text = I18n.T("Settings_General");
                SettingsCloseAfterLaunchLabel.Text = I18n.T("Settings_CloseAfterLaunch");
                SettingsLaunchModeLabel.Text = I18n.T("Settings_LaunchMode");
                ComboLaunchMode.SelectionChanged -= ComboLaunchMode_SelectionChanged;
                ComboLaunchMode.Items.Clear();
                ComboLaunchMode.Items.Add(new ComboBoxItem { Content = I18n.T("Settings_LaunchMode_Single"), Tag = "single" });
                ComboLaunchMode.Items.Add(new ComboBoxItem { Content = I18n.T("Settings_LaunchMode_Double"), Tag = "double" });
                ComboLaunchMode.SelectedIndex = ConfigService.LaunchMode == "double" ? 1 : 0; AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";
                ComboLaunchMode.SelectionChanged += ComboLaunchMode_SelectionChanged;
                SettingsUpdateChannelLabel.Text = I18n.T("Settings_UpdateChannel");
                ComboUpdateChannel.SelectionChanged -= ComboUpdateChannel_SelectionChanged;
                ComboUpdateChannel.Items.Clear();
                ComboUpdateChannel.Items.Add(I18n.T("Settings_UpdateChannel_Stable"));
                ComboUpdateChannel.Items.Add(I18n.T("Settings_UpdateChannel_Latest"));
                ComboUpdateChannel.SelectedIndex = ConfigService.UpdateChannel == "latest" ? 1 : 0;
                ComboUpdateChannel.SelectionChanged += ComboUpdateChannel_SelectionChanged;
                if (SettingsUpdateChannelDesc != null)
                    SettingsUpdateChannelDesc.Text = I18n.T("Settings_UpdateChannel_Desc");
                SettingsDataLocationLabel.Text = I18n.T("Settings_DataLocation");
                UpdateStorageModeUI();
                SettingsMigrateNote.Text = I18n.T("Settings_MigrateNote");
                var languages = I18n.GetAvailableLanguages();
                LanguageComboBox.SelectionChanged -= LanguageComboBox_SelectionChanged;
                LanguageComboBox.Items.Clear();
                int selectedIndex = 0;
                for (int i = 0; i < languages.Count; i++)
                {
                    LanguageComboBox.Items.Add(I18n.GetDisplayName(languages[i]));
                }
                LanguageComboBox.Tag = languages;
                // try to select the current language, fallback to first
                int idx = languages.IndexOf(I18n.CurrentLanguage);
                if (idx >= 0) selectedIndex = idx;
                LanguageComboBox.SelectedIndex = selectedIndex;
                LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

                try
                {
                    if (BtnOpenConfigFolder != null)
                        ToolTipService.SetToolTip(BtnOpenConfigFolder, I18n.T("Settings_OpenConfigFolder"));
                    if (BtnOpenCacheFolder != null)
                        ToolTipService.SetToolTip(BtnOpenCacheFolder, I18n.T("Settings_OpenCacheFolder"));
                }
                catch (Exception ex) { LogService.Write("App", "ApplyLocalization failed", ex); }

                PropTitleLabel.Text = I18n.T("Property_Title");
                try
                {
                    var closeBtn = PropTitleLabel.Parent is Grid g ? g.Children.OfType<Button>().FirstOrDefault() : null;
                    if (closeBtn != null)
                        ToolTipService.SetToolTip(closeBtn, I18n.T("Property_Close"));
                }
                catch (Exception ex) { LogService.Write("App", "ApplyLocalization failed", ex); }

                PropDisplayNameLabel.Text = I18n.T("Property_DisplayName");
                PropMainExePathLabel.Text = I18n.T("Property_MainExePath");
                MenuExeStartMenu.Text = I18n.T("Source_StartMenu");
                MenuExeDesktop.Text = I18n.T("Source_Desktop");
                MenuExeBrowse.Text = I18n.T("Property_BrowseFile");

                MenuAltStartMenu.Text = I18n.T("Source_StartMenu");
                MenuAltDesktop.Text = I18n.T("Source_Desktop");
                MenuAltBrowse.Text = I18n.T("Property_BrowseFile");

                MenuAlongStartMenu.Text = I18n.T("Source_StartMenu");
                MenuAlongDesktop.Text = I18n.T("Source_Desktop");
                MenuAlongBrowse.Text = I18n.T("Property_BrowseFile");

                MenuMgrStartMenu.Text = I18n.T("Source_StartMenu");
                MenuMgrDesktop.Text = I18n.T("Source_Desktop");
                MenuMgrBrowse.Text = I18n.T("Property_BrowseFile");
                PropSubstituteExeLabel.Text = I18n.T("Property_SubstituteExe");
                PropRunAtLaunchLabel.Text = I18n.T("Property_RunAtLaunch");
                PropManagerPathLabel.Text = I18n.T("Property_ManagerPath");
                PropOptionalLabel.Text = I18n.T("Property_Optional");

                string adminText = I18n.T("Property_Admin");
                PropAdminLabel1.Text = adminText;
                PropAdminLabel2.Text = adminText;
                PropAdminLabel3.Text = adminText;
                PropAdminLabel4.Text = adminText;
                try
                {
                    var iconGrid = PropIcon?.Parent as Border;
                    var changeIconBtn = iconGrid?.Parent is Grid ig ? ig.Children.OfType<Button>().FirstOrDefault(b => b != null) : null;
                    if (changeIconBtn != null)
                        ToolTipService.SetToolTip(changeIconBtn, I18n.T("Property_ChangeIcon"));
                }
                catch (Exception ex) { LogService.Write("App", "ApplyLocalization failed", ex); }

                try
                {
                    var exeDropDown = PropExePath?.Parent is Grid eg ? eg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (exeDropDown != null)
                        ToolTipService.SetToolTip(exeDropDown, I18n.T("Property_SelectFile"));

                    var mgrDropDown = PropMgrPath?.Parent is Grid mg ? mg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (mgrDropDown != null)
                        ToolTipService.SetToolTip(mgrDropDown, I18n.T("Property_SelectFile"));
                }
                catch (Exception ex) { LogService.Write("App", "ApplyLocalization failed", ex); }

                PropDeleteText.Text = I18n.T("Menu_Delete");
                ToolTipService.SetToolTip(PropBtnDelete, I18n.T("Property_DeleteItem"));
                PropSaveText.Text = I18n.T("Property_Save");
                try
                {
                    var saveBtnParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(PropSaveText);
                    var saveBtn = saveBtnParent != null ? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(saveBtnParent) as Button : null;
                    if (saveBtn != null)
                        ToolTipService.SetToolTip(saveBtn, I18n.T("Property_Save"));
                }
                catch (Exception ex) { LogService.Write("App", "ApplyLocalization failed", ex); }

                EmptyStateText.Text = I18n.T("Empty_Description");

                PropCustomMenuLabel.Text = I18n.T("Property_CustomMenu");
                string titlePlaceholder = I18n.T("Property_CustomTitlePlaceholder");
                string cmdPlaceholder = I18n.T("Property_CustomCommandPlaceholder");
                string browseTooltip = I18n.T("Property_BrowseFile");
                string selectTooltip = I18n.T("Property_SelectFile");
                string adminTooltip = I18n.T("Property_Admin");

                string customItemLabel = I18n.T("Property_CustomItem");
                for (int i = 0; i < 10; i++)
                {
                    _customSlotLabels[i].Text = $"{customItemLabel} {i + 1}";
                    _customTitles[i].PlaceholderText = titlePlaceholder;
                    _customCommands[i].PlaceholderText = cmdPlaceholder;
                    ToolTipService.SetToolTip(_customBrowses[i], selectTooltip);
                    ToolTipService.SetToolTip(_customAdmins[i], adminTooltip);
                    _customAdminLabels[i].Text = adminTooltip;
                }

                if (MigrationTitle != null) MigrationTitle.Text = I18n.T("Migration_OverlayTitle");
                if (MigrationSubTitle != null) MigrationSubTitle.Text = I18n.T("Migration_OverlaySubTitle");
                RefreshAnnouncementList();
            }
            catch (Exception ex)
            {
                LogService.Write("App", "ApplyLocalization failed", ex);
            }
        }


        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.Tag is List<string> languages &&
                LanguageComboBox.SelectedIndex >= 0 &&
                LanguageComboBox.SelectedIndex < languages.Count)
            {
                var selectedLang = languages[LanguageComboBox.SelectedIndex];
                if (selectedLang != I18n.CurrentLanguage)
                {
                    ConfigService.Language = selectedLang;
                    ConfigService.SaveConfig();
                    I18n.Load(selectedLang);
                }
            }
        }
        private void UpdateCustomVisibility()
        {
            int visibleCount = 0;
            string customItemLabel = I18n.T("Property_CustomItem");
            for (int i = 0; i < 10; i++)
            {
                bool isVisible = false;
                if (i == 0)
                {
                    isVisible = true;
                }
                else
                {
                    isVisible = !string.IsNullOrEmpty(_customCommands[i - 1].Text?.Trim());
                }

                _customSections[i].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
                if (isVisible)
                {
                    visibleCount++;
                    _customSlotLabels[i].Text = $"{customItemLabel} {visibleCount}";
                }
            }
        }

        private async Task StartUpdateFlowAsync(UpdateService.ReleaseInfo release, bool isForced = false)
        {
            using (LogService.StartOperation("Update", "StartUpdateFlowAsync"))
            {
                try { await ShowReleaseDialogAsync(release, hasUpdate: true, isForced); }
                catch (Exception ex) { LogService.Write("Update", "StartUpdateFlowAsync failed", ex); }
            }
        }

        private async Task ShowReleaseDialogAsync(UpdateService.ReleaseInfo release, bool hasUpdate, bool isForced = false)
        {
            using (LogService.StartOperation("Update", "ShowReleaseDialogAsync"))
            {
            string downloadUrl = release.assets.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.browser_download_url ?? "";
            if (hasUpdate && string.IsNullOrEmpty(downloadUrl)) return;

            var (contentGrid, dlgW, dlgH) = await BuildReleaseContentAsync(release, prependTitle: true);
            var dialog = new ContentDialog
            {
                Title = hasUpdate
                    ? (object)I18n.T("Update_DialogTitle")
                    : I18n.T("Update_NoUpdateContent"),
                Content = contentGrid,
                PrimaryButtonText = hasUpdate ? I18n.T("Update_DialogConfirm") : (string.IsNullOrEmpty(downloadUrl) ? "" : I18n.T("Update_Repair")),
                CloseButtonText = isForced ? I18n.T("Update_Exit") : (hasUpdate ? I18n.T("Update_DialogCancel") : I18n.T("Update_OK")),
                DefaultButton = hasUpdate ? ContentDialogButton.Primary : ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };

                if (isForced)
            {
                dialog.Closing += (s, e) =>
                {
                    if (e.Result != ContentDialogResult.Primary)
                    {
                        e.Cancel = true;
                        LogService.Write("App", "Exit requested (forced update dialog)");
                        try { Application.Current.Exit(); } catch (Exception ex) { LogService.Write("App", "Exit failed (forced update dialog)", ex); }
                    }
                };
            }

            dialog.Resources["ContentDialogMaxWidth"] = dlgW;
            dialog.Resources["ContentDialogMaxHeight"] = dlgH;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrEmpty(downloadUrl))
            {
                UpdateService.StartUpdater(downloadUrl);
                LogService.Write("App", "Exit requested (update start)");
                try { Application.Current.Exit(); } catch (Exception ex) { LogService.Write("App", "Exit failed (update start)", ex); }
            }
            }
        }

        private async Task<(Grid contentGrid, double dialogW, double dialogH)> BuildReleaseContentAsync(UpdateService.ReleaseInfo release, bool prependTitle)
        {
            using (LogService.StartOperation("Update", "BuildReleaseContentAsync"))
            {
            if (!System.IO.Directory.Exists(ConfigService.SystemCachePath))
                System.IO.Directory.CreateDirectory(ConfigService.SystemCachePath);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(ConfigService.SystemCachePath, "WebView2"));
            double dialogW = Math.Max(560, this.Bounds.Width * 0.80);
            double dialogH = Math.Max(420, this.Bounds.Height * 0.78);
            double innerH = Math.Max(280, dialogH - 150) - 48;

            var webView = new Microsoft.UI.Xaml.Controls.WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = dialogW - 48,
                Height = innerH
            };

            object dialogContent;
            try
            {
                await webView.EnsureCoreWebView2Async();
                var actualTheme = (this.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default;
                if (actualTheme == ElementTheme.Default)
                    actualTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

                string bodyHtml = (!string.IsNullOrEmpty(release.body_html) ? release.body_html : release.body) ?? "";
                if (prependTitle) bodyHtml = $"<h2>{release.name}</h2>{bodyHtml}";

                string htmlContent = $@"
                    <!DOCTYPE html>
                    <html data-theme='{(actualTheme == ElementTheme.Dark ? "dark" : "light")}'>
                    <head>
                        <meta charset='utf-8'>
                        <style>
                            {WebViewStyles.MarkdownCss}
                            @media (prefers-color-scheme: light), (prefers-color-scheme: dark) {{
                                html[data-theme='dark'] .markdown-body {{ background-color: #0d1117; color: #e6edf3; }}
                                html[data-theme='light'] .markdown-body {{ background-color: #ffffff; color: #1F2328; }}
                            }}
                            body {{
                                box-sizing: border-box; overflow-x: hidden; margin: 0; padding: 25px;
                                background-color: transparent !important;
                            }}
                            @media (max-width: 767px) {{ body {{ padding: 15px; width: 95%; }} }}
                        </style>
                    </head>
                    <body class='markdown-body'>{bodyHtml}</body>
                    </html>";

                webView.NavigateToString(htmlContent);
                dialogContent = webView;
            }
            catch
            {
                string fallbackText = prependTitle ? $"# {release.name}\n\n{release.body}" : $"## {release.name}\n\n{release.body}";
                dialogContent = new ScrollViewer
                {
                    Content = new TextBlock { Text = fallbackText, TextWrapping = TextWrapping.Wrap },
                    Height = innerH
                };
            }

            string channelName = ConfigService.UpdateChannel == "latest"
                ? I18n.T("Settings_UpdateChannel_Latest")
                : I18n.T("Settings_UpdateChannel_Stable");
            var channelNote = new TextBlock
            {
                Text = string.Format(I18n.T("Update_ChannelNote"), channelName),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, -20)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)dialogContent, 0);
            Grid.SetRow(channelNote, 1);
            grid.Children.Add((UIElement)dialogContent);
            grid.Children.Add(channelNote);

            return (grid, dialogW, dialogH);
        }
    }
        private async void VersionText_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            LogService.Write("App", "VersionText_PointerPressed invoked");
            if (HasUpdate && _pendingUpdate != null)
            {
                bool isForced = false;
                var match = System.Text.RegularExpressions.Regex.Match(_pendingUpdate.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                if (match.Success)
                {
                    Version latestVersion = UpdateService.NormalizeVersion(match.Value);
                    isForced = UpdateService.CheckForceUpdateAsync(latestVersion);
                }
                else
                {
                    isForced = UpdateService.CheckForceUpdateAsync();
                }
                await StartUpdateFlowAsync(_pendingUpdate, isForced);
                LogService.Write("App", "VersionText_PointerPressed started update flow");
            }
        }
    }
}


