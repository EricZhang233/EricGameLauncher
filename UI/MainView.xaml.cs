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
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace EricGameLauncher
{
    public sealed partial class MainView : UserControl, INotifyPropertyChanged
    {
        private readonly ObservableCollection<AppItem> _allItems = new();
        private readonly ObservableCollection<AppItem> _recycleItems = new();
        private readonly ObservableCollection<AppItem> _viewItems = new();
        private readonly CancellationTokenSource _lifetime = new();
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _externalRefreshTimer;
        private string _externalStamp = "";
        private string _lastGridConfigStamp = "";
        private RecycleBinFlyoutControl? _recycleBinControl;
        private SettingsFlyoutControl? _settingsControl;
        private SettingsFlyoutControl SettingsControl => _settingsControl ??= CreateSettingsControl();
        private ScannerDialogControl? _scannerControl;
        private CancellationTokenSource? _scanSession;
        private readonly Dictionary<AppItem, CancellationTokenSource> _loadingAnimations = new();
        private DateTime _nextExternalRefresh;

        private SettingsFlyoutControl CreateSettingsControl()
        {
            var control = new SettingsFlyoutControl();
            control.OwnerWindowHandle = _hWnd;
            control.LaunchModeChanged += SettingsControl_LaunchModeChanged;
            control.UpdateChannelChanged += SettingsControl_UpdateChannelChanged;
            control.OperationFailed += ShowOperationError;
            control.ConfigurationImported += () => { LoadSettings(); ApplyLocalization(); UpdateMenuExitVisibility(); };
            LogService.Write("UI", "Settings control created on demand");
            return control;
        }

        private Task<List<ShortcutScanner.FileItem>>? _preloadedStartMenuTask;
        private Task<List<ShortcutScanner.FileItem>>? _preloadedDesktopTask;
        private readonly List<BitmapImage> _startupPreloadedIcons = new();

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
        private bool _manualCheckInProgress;
        private bool _isUserInteracting = false;
        private bool _isRefreshPending = false;
        private bool _isDraggingItems;
        private readonly Windows.UI.ViewManagement.UISettings _animationSettings = new();
        private int _refreshQueued;
        private string[]? _dragOrder;
        private DateTime _lastLaunchTime = DateTime.MinValue;
        private bool _isRootLoaded = false;
        private bool _pendingInitialRefresh = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        private MainWindow? _window;
        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private WndProc? _wndProcDelegate;
        private FrameworkElement? _closeAfterLaunchInputRoot;
        private XamlRoot? _titleBarXamlRoot;
        public IList<UIElement> TitleBarActions => TitleBarActionsPanel.Children;
        private DispatcherTimer? _closeAfterLaunchTimer;
        private bool _closeAfterLaunchPending = false;
        private Action? _languageChangedHandler;
        private Action? _dataChangedHandler;
        private Action<bool>? _quickStartChangedHandler;

        private bool WarmupMode => _window?.WarmupMode ?? false;

        public MainView()
        {
            this.InitializeComponent();
            MoreMenuFlyout.Opening += (_, _) => UpdateShortcutMenuState();
            UpdateShortcutMenuState();
            AppGrid.ActivationRequested += LaunchItem;
            ApplyGridItemTransitions();
            LogService.Write("UI", "Main view constructed");
        }

        public void Start(MainWindow window)
        {
            _window = window;
            _hWnd = WindowNative.GetWindowHandle(window);
            PropertyControl.OwnerWindowHandle = _hWnd;
            LogService.Write("UI", "Main view attached to window");

            SetupWindowShell();
            WireControls();
            _externalRefreshTimer = DispatcherQueue.CreateTimer();
            _externalRefreshTimer.Interval = TimeSpan.FromSeconds(2);
            _externalRefreshTimer.Tick += (_, _) =>
            {
                if (_lifetime.IsCancellationRequested || _isDraggingItems || DateTime.UtcNow < _nextExternalRefresh) return;
                try
                {
                    string stamp = GetConfigStamp();
                    if (stamp == _externalStamp) return;
                    ConfigService.SaveSettings();
                    ConfigService.Reload();
                    if (ConfigService.LoadError != null) throw new IOException(ConfigService.LoadError);
                    if (!LoadSettings()) throw new InvalidOperationException(Text.T("Refresh_Failed"));
                    _settingsControl?.Sync();
                    if (!ServerConfigManager.LoadReadIds()) throw new IOException(Text.T("Refresh_Failed"));
                    RefreshAnnouncementList();
                    RefreshView();
                    if (_lastGridConfigStamp == GetConfigStamp()) _externalStamp = _lastGridConfigStamp;
                }
                catch (Exception ex) { _nextExternalRefresh = DateTime.UtcNow.AddSeconds(10); ShowOperationError(ex); }
            };
            if (!WarmupMode) _externalRefreshTimer.Start();
            _ = InitializeAfterActivationAsync();
        }

        private void SetupWindowShell()
        {
            var sw = Stopwatch.StartNew();
            LogService.Write("Startup", $"Ctor Start [Version: {AppVersion.DisplayVersion}]");
            try { StartupArgs.Parse(); DebugPaths.ApplyIfDebug(); } catch { }

            if (_window == null) return;

            TitleBarText.Text = _window.Title;

            _window.SetTitleBar(TitleBarDragRegion);
            TitleBarGrid.Loaded += TitleBarGrid_Loaded;
            TitleBarGrid.SizeChanged += TitleBarGrid_SizeChanged;
            LogService.Write("UI", $"Title bar drag region covers title and middle space; automatic action group count={TitleBarActions.Count}");

            _window.Closed += HostWindow_Closed;

            _oldWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = new WndProc(WindowProcess)));
            LogService.Write("Startup", $"Ctor End duration={sw.ElapsedMilliseconds}ms");
        }

        private void TitleBarGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (_titleBarXamlRoot != null) _titleBarXamlRoot.Changed -= TitleBarXamlRoot_Changed;
            _titleBarXamlRoot = TitleBarGrid.XamlRoot;
            if (_titleBarXamlRoot != null) _titleBarXamlRoot.Changed += TitleBarXamlRoot_Changed;
            UpdateTitleBarInsets();
        }

        private void TitleBarGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarInsets();

        private void TitleBarXamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarInsets();

        private void UpdateTitleBarInsets()
        {
            if (_window == null || TitleBarGrid.XamlRoot == null || _lifetime.IsCancellationRequested) return;
            try
            {
                double scale = TitleBarGrid.XamlRoot.RasterizationScale;
                if (scale <= 0) return;
                double right = _window.AppWindow.TitleBar.RightInset / scale + 12;
                if (Math.Abs(TitleBarActionsPanel.Margin.Right - right) < 0.1) return;
                TitleBarActionsPanel.Margin = new Thickness(0, 0, right, 0);
                LogService.Write("UI", $"Title bar caption inset updated right={right:F1} scale={scale:F2}");
            }
            catch (Exception ex) { LogService.Write("UI", "Title bar caption inset update failed", ex); }
        }

        private void WireControls()
        {
            LogService.Write("Startup", "Main view initialize start");
            SearchControl.QueryChanged += SearchBox_TextChanged;
            IconSizeControl.IconSizeChanged += IconSizeControl_SizeChanged;
            PropertyControl.SaveRequested += PropertyPanel_SaveRequested;
            AnnouncementControl.OperationFailed += ShowOperationError;
            PropertyControl.DeleteRequested += PropertyPanel_DeleteRequested;
            PropertyControl.MenusRequested += PropertyPanel_MenusRequested;
            PropertyControl.ApplyLocalization();

            VersionText.Text = AppVersion.DisplayVersion;

            this.Loaded += (s, e) =>
            {
                try
                {
                    _ = RevealUIWhenReadyAsync();
                }
                catch (Exception ex) { LogService.Write("UI", "Root element loaded handler failed", ex); }
            };
            LogService.Write("Startup", "Main view initialize complete");
        }

        private async Task RevealUIWhenReadyAsync()
        {
            var sw = Stopwatch.StartNew();
            var maxWaitTime = 1000;
            try
            {
                while (sw.ElapsedMilliseconds < maxWaitTime)
                {
                    if (AppGrid != null && AppGrid.ActualWidth > 0)
                    {
                        AppGrid.Visibility = Visibility.Visible;
                        AppGrid.Focus(FocusState.Programmatic);
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
                    AppGrid.Focus(FocusState.Programmatic);
                    LogService.Write("Startup", $"UI revealed after timeout at {sw.ElapsedMilliseconds}ms");
                }
                _isRootLoaded = true;

                if (_pendingInitialRefresh)
                {
                    _pendingInitialRefresh = false;
                    RefreshView();
                }
            }
            finally
            {
                _window?.MarkUiReady();
            }
        }

        private async Task InitializeAfterActivationAsync()
        {
            await Task.Yield();
            if (_lifetime.IsCancellationRequested) return;
            try
            {
                SetTitleBarIcon();
                _window?.SetSplashIcon();
                LogService.Write("Startup", "Startup overlay prepared");
                StartupArgs.LogEnvironment();
                ServerConfigManager.LoadReadIds();

                Text.Load(ConfigService.Language);
                _languageChangedHandler = () => DispatcherQueue.TryEnqueue(() => { if (!_lifetime.IsCancellationRequested) ApplyLocalization(); });
                Text.LanguageChanged += _languageChangedHandler;

                ServerConfigManager.AnnouncementsUpdated += OnAnnouncementsUpdated;

                ConfigService.IconSize = ConfigService.IconSize;
                _dataChangedHandler = QueueRefreshView;
                ConfigService.DataChanged += _dataChangedHandler;

                _quickStartChangedHandler = enabled => DispatcherQueue.TryEnqueue(() => { if (!_lifetime.IsCancellationRequested) UpdateMenuExitVisibility(); });
                QuickStartService.EnabledChanged += _quickStartChangedHandler;

                LoadSettings();
                ApplyLocalization();
                RefreshAnnouncementList();

                _ = LoadDataAsync();
                if (!WarmupMode) _ = InitializeNetworkTasksAsync();
                _ = InitializeMenuItemsAsync();
                LogService.Write("Startup", "Post-activation initialization scheduled");
            }
            catch (Exception ex)
            {
                LogService.Write("Startup", "Post-activation initialization failed", ex);
                _window?.MarkDataReady();
                _window?.MarkUiReady();
            }
        }

        private async Task InitializeMenuItemsAsync()
        {
            await Task.Delay(200);
            if (_lifetime.IsCancellationRequested) return;
            
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
                    if (_lifetime.IsCancellationRequested) return;
                    LogService.Write("Startup", $"LoadDataAsync AfterLoadData duration={sw.ElapsedMilliseconds}ms items={_allItems.Count}");
                    AutoCleanRecycleBin();
                    LogService.Write("Startup", $"LoadDataAsync AfterAutoClean totalDuration={sw.ElapsedMilliseconds}ms recycleItems={_recycleItems.Count}");
                    await PreloadStartupIconsAsync();
                }
            }
            finally
            {
                _window?.MarkDataReady();
                LogService.StartupExit();
            }
        }

        private async Task PreloadStartupIconsAsync()
        {
            var paths = _allItems
                .Select(item => item.IconPath)
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24).ToList();

            var converter = new ImagePathConverter();
            var preloadTasks = paths.Select(path => PreloadStartupIconAsync(converter, path)).ToList();
            var preloadTask = Task.WhenAll(preloadTasks);
            var completed = await Task.WhenAny(preloadTask, Task.Delay(TimeSpan.FromSeconds(5)));
            if (completed != preloadTask)
                LogService.Write("Startup", $"PreloadStartupIconsAsync timed out requested={paths.Count}");
            else
                LogService.Write("Startup", $"PreloadStartupIconsAsync completed requested={paths.Count}");

            LogService.Write("Startup", "Startup icon signal ready");
        }

        private async Task PreloadStartupIconAsync(ImagePathConverter converter, string path)
        {
            try
            {
                if (converter.Convert(path, typeof(ImageSource), "", "") is not BitmapImage bitmap)
                    return;

                _startupPreloadedIcons.Add(bitmap);
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                bitmap.ImageOpened += (sender, args) => completion.TrySetResult(true);
                bitmap.ImageFailed += (sender, args) => completion.TrySetResult(false);
                await Task.WhenAny(completion.Task, Task.Delay(800));
            }
            catch (Exception ex)
            {
                LogService.Write("Startup", $"PreloadStartupIconAsync failed path={path}", ex);
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
                    try { await ServerConfigManager.FetchConfigAsync(_lifetime.Token); }
                    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
                    catch (Exception ex) { LogService.Write("Network", "Announcements unavailable; update check continues", ex); ShowOperationError(ex); }
                    _lifetime.Token.ThrowIfCancellationRequested();
                    LogService.Write("Startup", $"InitializeNetwork AfterFetch {sw.ElapsedMilliseconds}ms");
                    _ = CheckForUpdatesInBackgroundAsync();
                    LogService.Write("Startup", $"InitializeNetwork BackgroundCheckScheduled {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { LogService.Write("Network", "Announcements refresh failed; cached snapshot retained", ex); }
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
                    await Task.Delay(5000, _lifetime.Token);
                    LogService.Write("Update", "BackgroundCheck AfterDelay - now checking for updates");
                    await CheckForUpdatesQuietlyAsync(skipDelay: true);
                }
            }
            catch (Exception ex)
            {
                if (!_lifetime.IsCancellationRequested) LogService.Write("Update", "BackgroundCheck Failed", ex);
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

                    var status = await UpdateService.CheckUpdateStatusAsync(ConfigService.UpdateChannel).WaitAsync(_lifetime.Token);
                    if (_lifetime.IsCancellationRequested) return;
                    if (status.HasUpdate && status.Release != null)
                    {
                        var release = status.Release;
                        bool isForced = status.IsForced;

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
                if (!_lifetime.IsCancellationRequested) LogService.Write("Update", "QuietCheck Failed", ex);
            }
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_manualCheckInProgress) return;
            _manualCheckInProgress = true;
            MenuCheckUpdateItem.IsEnabled = false;
            try { await ManualCheckAsync(); }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowOperationError(ex); }
            finally
            {
                _manualCheckInProgress = false;
                MenuCheckUpdateItem.IsEnabled = true;
            }
        }

        private async Task ManualCheckAsync()
        {
            UpdateService.InvalidateCache();
            _pendingUpdate = null;
            HasUpdate = false;
            using (LogService.StartOperation("Update", "ManualCheck"))
            {
                LogService.Write("Update", "ManualCheck Start");
                await ShowReleaseDialogAsync(null, hasUpdate: false, checkForUpdates: true);
                LogService.Write("Update", "ManualCheck End");
            }
        }

        private async void MenuPrivacyItem_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuPrivacyItem_Click Start");
            var scrollViewer = new Microsoft.UI.Xaml.Controls.ScrollViewer
            {
                VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                VerticalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled,
                MaxHeight = 400,
                Content = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = Text.T("Privacy_DialogContent"),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 0, 16, 0)
                }
            };
            ContentDialog privacyDialog = new ContentDialog
            {
                Title = Text.T("Privacy_DialogTitle"),
                Content = scrollViewer,
                CloseButtonText = Text.T("Privacy_DialogClose"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot,
                FullSizeDesired = false
            };
            await privacyDialog.ShowAsync();
            LogService.Write("UI", "MenuPrivacyItem_Click End");
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
                    var bitmap = AppIconService.GetBitmapImage();
                    if (bitmap != null)
                    {
                        img.Source = bitmap;
                        LogService.Write("App", "MenuAuthorIconInternal loaded icon");
                    }
                    else
                    {
                        LogService.Write("App", "MenuAuthorIconInternal no icon available");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuAuthorIconInternal_Loaded failed", ex); }
            }
        }


        private bool LoadSettings()
        {
            using (LogService.StartOperation("App", "LoadSettings"))
            {
            try
            {
                LogService.Write("App", $"LoadSettings Start CloseAfterLaunch={ConfigService.CloseAfterLaunch} LaunchMode={ConfigService.LaunchMode} IconSize={ConfigService.IconSize}");

                UpdateGridInteraction();

                IconSizeControl.SetValue(ConfigService.IconSize);
                IconSize = IconSizeControl.Value;
                LogService.Write("App", "LoadSettings applied UI values");
                return true;
            }
            catch (Exception ex) { LogService.Write("App", "LoadSettings failed", ex); return false; }
            }
        }

        private static string GetConfigStamp() =>
            $"{File.GetLastWriteTimeUtc(ConfigService.SettingsFilePath).Ticks}:{File.GetLastWriteTimeUtc(ConfigService.ItemsFilePath).Ticks}:{File.GetLastWriteTimeUtc(StorageMigration.ModeFile).Ticks}:{File.GetLastWriteTimeUtc(ServerConfigManager.ReadIdsPath).Ticks}";

        private void QueueRefreshView()
        {
            if (_lifetime.IsCancellationRequested || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (!_lifetime.IsCancellationRequested) RefreshView();
            })) Interlocked.Exchange(ref _refreshQueued, 0);
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

                    if (_isUserInteracting || _isDraggingItems)
                    {
                        _isRefreshPending = true;
                        LogService.Write("App", "RefreshView postponed due to user interaction");
                        return;
                    }
                    _isRefreshPending = false;

                    List<AppItem> items;
                    List<AppItem> recycleItems;
                    string stamp = GetConfigStamp();
                    items = ConfigService.LoadItems();
                    recycleItems = ConfigService.LoadRecycleBinItems();
                    if (ConfigService.LoadError != null) throw new IOException(ConfigService.LoadError);
                    ApplyItemData(items, recycleItems);
                    _lastGridConfigStamp = stamp;
                    if (ConfigService.LoadError != null) ShowOperationError(new IOException(ConfigService.LoadError));
                }
                catch (Exception ex) { LogService.Write("App", "RefreshView failed", ex); }
            }
        }

        private void ApplyItemData(List<AppItem> items, List<AppItem> recycleItems)
        {
            var sw = Stopwatch.StartNew();
            var (normalItems, normalizedRecycle, _) = ItemService.NormalizeState(items, recycleItems);

            bool hasChanges = ItemService.HasChanged(_allItems.ToList(), normalItems, _recycleItems.ToList(), normalizedRecycle);

            if (hasChanges)
            {
                LogService.Write("App", $"ApplyItemData DataChanged - rebuilding collections");
                SyncCollection(_allItems, normalItems, true);
                SyncCollection(_recycleItems, normalizedRecycle, true, RecycleKey);
                RefreshGridItems();
            }
            else
            {
                LogService.Write("App", $"ApplyItemData NoChanges - skipping rebuild");
            }

            LogService.Write("App", $"ApplyItemData applied counts all={_allItems.Count} recycle={_recycleItems.Count} view={_viewItems.Count} duration={sw.ElapsedMilliseconds}ms");
            UpdateEmptyState();

            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                LogService.Write("App", "ApplyItemData UpdateGridItemSizes queued");
                UpdateGridItemSizes(IconSize);
            });
        }

        private void UpdateEmptyState()
        {
            using (LogService.StartOperation("App", "UpdateEmptyState"))
            {
            try
            {
                EmptyStateControl.Visibility = Visibility.Collapsed;
                LogService.Write("App", "UpdateEmptyState applied: hidden");
            }
            catch (Exception ex) { LogService.Write("App", "UpdateEmptyState failed", ex); }
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void ShowOperationError(Exception error)
        {
            LogService.Write("UI", "Operation failed", error);
            Notifications.Notify(error.Message, NotificationLevel.Error);
        }

        private static string ItemDisplayName(AppItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Title)) return item.Title!;
            return string.IsNullOrEmpty(item.ExePath) ? "" : Path.GetFileNameWithoutExtension(item.ExePath);
        }

        private void ShowSuccessNotification(string message)
        {
            Notifications.Notify(message, NotificationLevel.Success);
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
                    if (WarmupMode)
                    {
                        LogService.Write("Startup", "LoadData migration deferred, warmup window");
                        return;
                    }
                    MigrationOverlayControl.Visibility = Visibility.Visible;
                    await Task.Delay(200);
                    if (_lifetime.IsCancellationRequested) return;
                    await MigrationService.RunMigrationAsync();
                    if (_lifetime.IsCancellationRequested) return;
                    MigrationOverlayControl.Visibility = Visibility.Collapsed;
                }

                LogService.Write("Startup", $"LoadData BeforeRefreshGlobal {sw.ElapsedMilliseconds}ms");
                await ConfigService.RefreshGlobalAsync(_lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                LogService.Write("Startup", $"LoadData AfterRefreshGlobal {sw.ElapsedMilliseconds}ms");

                var loadedItems = ConfigService.LoadItems();
                var loadedRecycleItems = ConfigService.LoadRecycleBinItems();
                ApplyItemData(loadedItems, loadedRecycleItems);
                AppGrid.ItemsSource = _viewItems;
                LogService.Write("Startup", $"LoadData PopulatedCollections all={_allItems.Count} recycle={_recycleItems.Count}");

                    LogService.Write("Startup", $"LoadData AfterPreloadTasks {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) { MigrationOverlayControl.Visibility = Visibility.Collapsed; ShowOperationError(ex); } }
        }

        private void SyncFromCore()
        {
            SyncCollection(_allItems, ConfigService.LoadItems(), true);
            SyncCollection(_recycleItems, ConfigService.LoadRecycleBinItems(), true, RecycleKey);
            RefreshGridItems();
        }

        private static string RecycleKey(AppItem item) => item.RecycleId ?? item.Id;

        private static void SyncCollection(ObservableCollection<AppItem> target, IEnumerable<AppItem> source, bool update, Func<AppItem, string>? keyOf = null)
        {
            keyOf ??= item => item.Id;
            var desired = source.ToList();
            var ids = desired.Select(keyOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
            for (int i = target.Count - 1; i >= 0; i--) if (!ids.Contains(keyOf(target[i]))) target.RemoveAt(i);
            for (int i = 0; i < desired.Count; i++)
            {
                int existing = -1;
                for (int j = i; j < target.Count; j++) if (keyOf(target[j]) == keyOf(desired[i])) { existing = j; break; }
                if (existing < 0) target.Insert(i, desired[i]);
                else
                {
                    if (existing != i) target.Move(existing, i);
                    if (update && !ReferenceEquals(target[i], desired[i])) ItemService.CopyContent(target[i], desired[i]);
                }
            }
        }

        private void RefreshGridItems()
        {
            ApplyGridItemTransitions();
            var query = SearchControl?.Text?.Trim() ?? "";
            IEnumerable<AppItem> items = string.IsNullOrEmpty(query)
                ? _allItems
                : ItemService.Search(_allItems, query.ToLowerInvariant());

            SyncCollection(_viewItems, items, false);
            if (!ReferenceEquals(AppGrid.ItemsSource, _viewItems)) AppGrid.ItemsSource = _viewItems;
            AppGrid.RecalculateAdaptiveWidth();
            IsFiltered = !string.IsNullOrEmpty(query);
            UpdateEmptyState();
            LogService.Write("App", $"RefreshGridItems applied count={_viewItems.Count} filtered={IsFiltered}");
        }


        private DispatcherTimer? _tooltipTimer;
        private AppItem? _hoveredItem;
        private FrameworkElement? _hoveredElement;

        private void ItemPanel_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDraggingItems) return;
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
                if (_isDraggingItems) return;
                if (_hoveredItem != null && _hoveredElement != null && CustomIconToolTip != null && CustomIconToolTipText != null)
                {
                    LogService.Write("App", $"TooltipTimer_Tick showing tooltip for itemId={_hoveredItem.Id} title={_hoveredItem.Title}");
                    CustomIconToolTipText.Text = _hoveredItem.Title;

                    if (this.XamlRoot != null)
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

                                var transform = _hoveredElement.TransformToVisual(this);
                                
                                var targetCenter = transform.TransformPoint(new Windows.Foundation.Point(
                                    _hoveredElement.ActualWidth / 2,
                                    _hoveredElement.ActualHeight - (titleText.ActualHeight / 2)
                                ));

                                double targetX = targetCenter.X - (popupWidth / 2);
                                double targetY = targetCenter.Y - (popupHeight / 2);

                                if (this is FrameworkElement contentFE)
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
                if (_isDraggingItems || ConfigService.LaunchMode == "double")
                {
                    LogService.Write("App", "AppGrid_ItemClick ignored due to dragging or double launch mode");
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

        private void AppGrid_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            e.Handled = HandleActivationKey(e.OriginalSource as DependencyObject, e.Key, e.KeyStatus.WasKeyDown) || e.Handled;
        }

        internal bool HandleActivationKey(DependencyObject? source, Windows.System.VirtualKey key, bool repeated)
        {
            if (key is not (Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)) return false;
            if (_lifetime.IsCancellationRequested || _isDraggingItems || repeated) return false;
            while (source != null && source != AppGrid && source is not GridViewItem)
            {
                if (source is TextBox or PasswordBox or AutoSuggestBox or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return false;
                source = VisualTreeHelper.GetParent(source);
            }
            if (source is GridViewItem { Content: AppItem item }) { LaunchItem(item); return true; }
            return false;
        }

        private void HideCustomToolTip()
        {
            if (_hoveredItem == null && _hoveredElement == null && (CustomIconToolTip == null || !CustomIconToolTip.IsOpen))
                return;
            _hoveredItem = null;
            _hoveredElement = null;
            _tooltipTimer?.Stop();
            if (CustomIconToolTip != null) CustomIconToolTip.IsOpen = false;
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
                if (_isDraggingItems || ConfigService.LaunchMode != "double")
                {
                    LogService.Write("App", "AppGrid_DoubleTapped ignored due to dragging or single launch mode");
                    return;
                }
                var source = e.OriginalSource as DependencyObject;
                while (source != null && source != AppGrid && source is not GridViewItem)
                    source = VisualTreeHelper.GetParent(source);
                if (source is GridViewItem { Content: AppItem item })
                {
                    e.Handled = true;
                    LogService.Write("App", $"AppGrid_DoubleTapped launching itemId={item.Id} title={item.Title}");
                    LaunchItem(item);
                }
            }
            catch (Exception ex) { LogService.Write("App", "AppGrid_DoubleTapped failed", ex); }
            }
        }




        private async void LaunchItem(AppItem item)
        {
            if (_lifetime.IsCancellationRequested || _isDraggingItems) return;
            if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
            _lastLaunchTime = DateTime.Now;

            try
            {
                HideCustomToolTip();
                var result = await LaunchService.LaunchAsync(item, item.UseAlternativeLaunch);
                if (_lifetime.IsCancellationRequested) return;
                result.EnsureSuccess();
                TriggerItemLoadingAnimation(item);
                if (ConfigService.CloseAfterLaunch)
                    StartCloseAfterLaunchTimer();
            }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowOperationError(ex); }
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
                                Symbol.Play => Text.T("Menu_Run"),
                                Symbol.Repair => Text.T("Menu_RunManager"),
                                Symbol.Folder => Text.T("Menu_OpenFileLocation"),
                                Symbol.Edit => Text.T("Menu_Properties"),
                                Symbol.Delete => Text.T("Menu_Delete"),
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

                if (item.HasManagerOrDefault)
                {
                    var mgrItem = new MenuFlyoutItem
                    {
                        Tag = "DynamicManager",
                        DataContext = item,
                        Icon = new SymbolIcon(Symbol.Repair)
                    };

                    if (mgrPlatform != null)
                    {
                        mgrItem.Text = string.Format(Text.T("Menu_PlatformManager"), mgrPlatform.PlatformName);
                    }
                    else if (platform != null || isXbox)
                    {
                        string pName = isXbox ? "Xbox" : (platform?.PlatformName ?? "");
                        mgrItem.Text = string.Format(Text.T("Menu_PlatformManager"), pName);
                    }
                    else if (hasCustomMgr)
                    {
                        mgrItem.Text = Text.T("Menu_RunManager");
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


        private async void MenuCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CustomMenuItem ci)
                {
                    if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                    _lastLaunchTime = DateTime.Now;
                    HideCustomToolTip();
                    (await LaunchService.LaunchCustomCommandAsync(ci)).EnsureSuccess();
                }
            }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowOperationError(ex); }
        }

        private async void MenuRunMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item == null) return;

                if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                _lastLaunchTime = DateTime.Now;

                HideCustomToolTip();
                var result = await LaunchService.LaunchManagerAsync(item);
                if (_lifetime.IsCancellationRequested) return;
                result.EnsureSuccess();
                TriggerItemLoadingAnimation(item);
            }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowOperationError(ex); }
        }

        private async void TriggerItemLoadingAnimation(AppItem item)
        {
            if (_loadingAnimations.Remove(item, out var previous)) previous.Cancel();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _loadingAnimations[item] = request;
            item.IsLoading = true;
            item.LoadingOpacity = 1.0;
            try
            {
                await Task.Delay(3000, request.Token);
                if (_animationSettings.AnimationsEnabled)
                {
                    for (int i = 0; i <= 10; i++)
                    {
                        request.Token.ThrowIfCancellationRequested();
                        item.LoadingOpacity = 1.0 - i / 10.0;
                        await Task.Delay(50, request.Token);
                    }
                }
                item.IsLoading = false;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogService.Write("UI", "Loading indicator failed", ex); }
            finally { if (_loadingAnimations.TryGetValue(item, out var current) && ReferenceEquals(current, request)) _loadingAnimations.Remove(item); }
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
                    string? target = ProcessRunner.FileTarget(item.ExePath);
                    string? dir = target == null ? null : Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start("explorer.exe", $"/select,\"{target}\"");
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
                    ItemService.RemoveItem(item.Id, null);
                    SyncFromCore();
                    RefreshView();
                    ShowSuccessNotification(string.Format(Text.T("Notify_Deleted"), ItemDisplayName(item)));
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
                    PropertyControl.BeginEdit(item, false);
                    LogService.Write("App", $"MenuProp_Click opened props for itemId={item.Id}");
                }
            }
            catch (Exception ex) { LogService.Write("App", "MenuProp_Click failed", ex); }
        }

        private async void MenuScan_Click(object sender, RoutedEventArgs e)
        {
            _scanSession?.Cancel();
            using var session = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _scanSession = session;
            var scannerControl = CreateScannerControl();
            Task? dialogTask = null;
            Task<PlatformScanResult>? scanTask = null;
            scannerControl.Closed += (_, _) => session.Cancel();
            try
            {
                using (LogService.StartOperation("Scan", "Scan"))
                {
                    LogService.Write("Scan", "Scan Start");
                await ConfigService.ReconstructMissingConfigAsync();

                scannerControl.ShowLoading();

                dialogTask = scannerControl.ShowAsync().AsTask();

                scanTask = PlatformScan.RunAsync(token: session.Token);
                if (await Task.WhenAny(scanTask, dialogTask) == dialogTask)
                {
                    session.Cancel();
                    try { await scanTask; } catch (OperationCanceledException) { }
                    await dialogTask;
                    return;
                }
                var scan = await scanTask;
                scan.RequireComplete();
                var scannedGames = scan.Games;
                if (session.IsCancellationRequested || !ReferenceEquals(_scanSession, session)) return;

                LogService.Write("Scan", $"MenuScan_Click scannedGamesCount={scannedGames?.Count ?? 0}");

                bool canValidateSteam = ScanService.CanValidateSteam;
                bool canValidateEpic = ScanService.CanValidateEpic;

                var allItems = _allItems.ToList();
                var (newGames, existingGames) = ScanService.Classify(scannedGames ?? new List<ScannedGame>(), allItems);

                LogService.Write("Scan", $"MenuScan_Click classification existing={existingGames.Count} new={newGames.Count}");

                var invalidGames = ScanService.FindInvalidGames(_allItems.ToList(), scan);

                scannerControl.ShowResults(newGames, existingGames, invalidGames);
                    LogService.Write("Scan", "Scan Complete");
                    await dialogTask;
                }
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            catch (Exception ex) { LogService.Write("Scan", "Scan failed", ex); if (!_lifetime.IsCancellationRequested && ReferenceEquals(_scanSession, session) && (!session.IsCancellationRequested || dialogTask?.IsFaulted == true)) { scannerControl.ShowFailure(ex); ShowOperationError(ex); } }
            finally
            {
                session.Cancel();
                if (scanTask != null)
                {
                    try { await scanTask; }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { LogService.Write("Scan", "Cancelled scan completed with an error", ex); }
                }
                if (dialogTask != null && !dialogTask.IsCompleted)
                {
                    if (session.IsCancellationRequested) scannerControl.Hide();
                    try { await dialogTask; } catch (Exception ex) { LogService.Write("Scan", "Dialog close failed", ex); }
                }
                if (ReferenceEquals(_scanSession, session)) _scanSession = null;
            }
        }

        private ScannerDialogControl CreateScannerControl()
        {
            _scannerControl = new ScannerDialogControl();
            _scannerControl.ImportRequested += ScannerControl_ImportRequested;
            _scannerControl.DeleteInvalidRequested += ScannerControl_DeleteInvalidRequested;
            _scannerControl.OperationFailed += ShowOperationError;
            LogService.Write("UI", "Scanner control created on demand");
            _scannerControl.XamlRoot = this.XamlRoot;
            _scannerControl.ApplyLocalization();
            return _scannerControl;
        }

        private ScanActionResult ScannerControl_ImportRequested(IReadOnlyList<ScannedGame> games)
        {
            var result = ItemService.ImportGamesDetailed(games.ToList());
            try { SyncFromCore(); } catch (Exception ex) { ShowOperationError(ex); }
            _ = ConfigService.RefreshGlobalAsync();
            if (result.Changed > 0) ShowSuccessNotification(string.Format(Text.T("Notify_Imported"), result.Changed));
            return result;
        }

        private ScanActionResult ScannerControl_DeleteInvalidRequested(IReadOnlyList<ScannedGame> games)
        {
            var result = ScanService.DeleteInvalidGamesDetailed(games.ToList());
            try { SyncFromCore(); } catch (Exception ex) { ShowOperationError(ex); }
            if (result.Changed > 0) ShowSuccessNotification(string.Format(Text.T("Notify_InvalidRemoved"), result.Changed));
            return result;
        }

        private void AutoCleanRecycleBin()
        {
            try
            {
                ItemService.AutoCleanExpired();
                SyncFromCore();
            }
            catch (Exception ex) { LogService.Write("App", "AutoCleanRecycleBin failed", ex); }
        }






        private async void PropertyPanel_SaveRequested()
        {
            using (LogService.StartOperation("App", "PropertyPanel_SaveRequested"))
            {
                try
                {
                    var item = PropertyControl.EditingItem;
                    bool isNew = PropertyControl.IsNewItemMode;
                    LogService.Write("App", $"PropertyPanel_SaveRequested Start currentEditingItemId={item?.Id} title={item?.Title}");
                    if (item == null)
                    {
                        LogService.Write("App", "PropertyPanel_SaveRequested aborted: no current editing item");
                        return;
                    }

                    string exePath = PropertyControl.ExePathText;
                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        LogService.Write("App", "PropertyPanel_SaveRequested validation failed: empty ExePath");
                        PropertyControl.ShowExePathError();
                        return;
                    }

                    if (isNew && ItemService.CheckDuplicate(exePath))
                    {
                        LogService.Write("App", $"PropertyPanel_SaveRequested validation failed: duplicate ExePath path={exePath}");
                        PropertyControl.ShowExePathError();
                        return;
                    }

                    PropertyControl.ApplyToItem();

                    bool current = await PropertyControl.SaveDraftAsync(_lifetime.Token);
                    if (_lifetime.IsCancellationRequested) return;

                    SyncFromCore();
                    RefreshView();
                    if (!current) return;
                    PropertyControl.ClosePanel();
                    ShowSuccessNotification(string.Format(Text.T(isNew ? "Notify_Added" : "Notify_Updated"), ItemDisplayName(item)));
                    LogService.Write("App", $"PropertyPanel_SaveRequested completed id={item.Id} isNew={isNew}");
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception ex) { if (!_lifetime.IsCancellationRequested) ShowOperationError(ex); }
            }
        }

        private void PropertyPanel_DeleteRequested()
        {
            using (LogService.StartOperation("App", "PropertyPanel_DeleteRequested"))
            {
                try
                {
                    var item = PropertyControl.EditingItem;
                    bool isNew = PropertyControl.IsNewItemMode;
                    LogService.Write("App", $"PropertyPanel_DeleteRequested Start currentEditingItemId={item?.Id}");
                    if (item == null) return;

                    if (!isNew)
                        ItemService.RemoveItem(item.Id, null);

                    SyncFromCore();
                    RefreshView();
                    PropertyControl.ClosePanel();
                    if (!isNew) ShowSuccessNotification(string.Format(Text.T("Notify_Deleted"), ItemDisplayName(item)));
                    LogService.Write("App", $"PropertyPanel_DeleteRequested completed id={item.Id} isNew={isNew}");
                }
                catch (Exception ex) { LogService.Write("App", "PropertyPanel_DeleteRequested failed", ex); }
            }
        }

        private async void PropertyPanel_MenusRequested()
        {
            using (LogService.StartOperation("App", "PopulateShortcutMenus"))
            {
                try
                {
                    List<ShortcutScanner.FileItem> startMenuItems = _preloadedStartMenuTask != null
                        ? await _preloadedStartMenuTask
                        : await Task.Run(() => ShortcutScanner.GetStartMenuItems());
                    _preloadedStartMenuTask = null;

                    List<ShortcutScanner.FileItem> desktopItems = _preloadedDesktopTask != null
                        ? await _preloadedDesktopTask
                        : await Task.Run(() => ShortcutScanner.GetDesktopItems());
                    _preloadedDesktopTask = null;

                    LogService.Write("App", $"PopulateShortcutMenus fetched startMenuItems={startMenuItems?.Count ?? 0} desktopItems={desktopItems?.Count ?? 0}");

                    PropertyControl.PopulateShortcutMenus(startMenuItems, desktopItems);
                }
                catch (Exception ex) { LogService.Write("App", "PopulateShortcutMenus failed", ex); }
            }
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
                AnnouncementControl.Refresh();
            }
            catch (Exception ex) { LogService.Write("Announcement", "RefreshAnnouncementList failed", ex); }
        }
        private void SearchBox_TextChanged(TextBox sender, TextChangedEventArgs args)
        {
            try
            {
                if (sender == null) return;

                ApplyGridItemTransitions();
                string query = (sender.Text ?? "").ToLower().Trim();

                IEnumerable<AppItem> filtered = string.IsNullOrEmpty(query)
                    ? _allItems
                    : ItemService.Search(_allItems, query);

                SyncCollection(_viewItems, filtered, false);
                if (!ReferenceEquals(AppGrid.ItemsSource, _viewItems)) AppGrid.ItemsSource = _viewItems;

                IsFiltered = !string.IsNullOrEmpty(query);
                UpdateEmptyState();
            }
            catch (Exception ex) { LogService.Write("App", "SearchBox_TextChanged failed", ex); }
        }

        private void ApplyGridItemTransitions()
        {
            var transitions = new TransitionCollection();
            if (_animationSettings.AnimationsEnabled)
            {
                transitions.Add(new ReorderThemeTransition());
                transitions.Add(new RepositionThemeTransition { IsStaggeringEnabled = false });
            }
            AppGrid.ItemContainerTransitions = transitions;
        }

        private void UpdateGridInteraction()
        {
            ApplyGridItemTransitions();
            AppGrid.CanDragItems = true;
            AppGrid.CanReorderItems = true;
            AppGrid.ReorderMode = ListViewReorderMode.Enabled;
            AppGrid.AllowDrop = true;
            AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";
            AppGrid.SelectionMode = ListViewSelectionMode.None;
        }

        private void AppGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            using var operation = LogService.StartOperation("Sort", "DragPickup");
            if (_isDraggingItems)
            {
                e.Cancel = true;
                return;
            }

            HideCustomToolTip();
            ApplyGridItemTransitions();
            _dragOrder = _viewItems.Select(item => item.Id).ToArray();
            _isDraggingItems = true;
            e.Data.RequestedOperation = DataPackageOperation.Move;
            LogService.Write("Sort", $"Grid drag started count={e.Items.Count}");
        }

        private void AppGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            using var operation = LogService.StartOperation("Sort", "DragDrop");
            try
            {
                var order = _viewItems.Select(item => item.Id).ToArray();
                if (_dragOrder != null && !_dragOrder.SequenceEqual(order))
                {
                    ItemService.SaveVisibleOrder(order);
                    _isRefreshPending = true;
                    LogService.Write("Sort", $"Grid reorder committed count={order.Length}");
                }
                else
                {
                    LogService.Write("Sort", "Grid drag completed without an order change");
                }
            }
            catch (Exception ex)
            {
                _isRefreshPending = true;
                LogService.Write("Sort", "Grid reorder failed", ex);
            }
            finally
            {
                _dragOrder = null;
                _isDraggingItems = false;
                if (_isRefreshPending)
                    QueueRefreshView();
            }
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new AppItem
            {
                Id = Guid.NewGuid().ToString("N")[..16],
                Title = string.Empty,
            };

            PropertyControl.BeginEdit(newItem, true);
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
            IconSizeControl.ApplyLocalization();
            IconSizeControl.Flyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuIconSize_Click showed IconSizeFlyoutControl");
        }

        private void MenuRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("App", "MenuRecycleBin_Click invoked");
            _recycleBinControl ??= CreateRecycleBinControl();
            _recycleBinControl.SetItems(_recycleItems);
            _recycleBinControl.ApplyLocalization();
            _recycleBinControl.Flyout.ShowAt(BtnMore);
            LogService.Write("App", "MenuRecycleBin_Click showed RecycleBinFlyoutControl");
        }

        private RecycleBinFlyoutControl CreateRecycleBinControl()
        {
            var control = new RecycleBinFlyoutControl();
            control.ItemsChanged += () =>
            {
                SyncFromCore();
                control.SetItems(_recycleItems);
            };
            control.Failed += message => ShowOperationError(new InvalidOperationException(message));
            control.NotifyRequested += (level, message) => Notifications.Notify(message, level);
            return control;
        }


        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuSettings_Click invoked");
            SettingsControl.ApplyLocalization();
            SettingsControl.Sync();
            IconSizeControl.SetValue(ConfigService.IconSize);
            SettingsControl.Flyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuSettings_Click showed SettingsFlyoutControl");
        }

        private void MenuInstall_Click(object sender, RoutedEventArgs e)
        {
            try { AppInstallService.Install(); }
            catch (Exception ex) { LogService.Write("App", "MenuInstall_Click failed", ex); }
        }

        private void UpdateShortcutMenuState()
        {
            bool enabled = !AppInstallService.IsInstalled;
            MenuSystemIntegrationItem.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            MenuInstallItem.IsEnabled = enabled;
            MenuUninstallItem.IsEnabled = enabled;
        }

        private void MenuUninstall_Click(object sender, RoutedEventArgs e)
        {
            try { AppInstallService.Uninstall(); }
            catch (Exception ex) { LogService.Write("App", "MenuUninstall_Click failed", ex); }
        }


        private void SettingsControl_LaunchModeChanged(string mode)
        {
            UpdateGridInteraction();
            LogService.Write("UI", $"SettingsControl launch mode applied to grid mode={mode}");
        }

        private void SettingsControl_UpdateChannelChanged()
        {
            _pendingUpdate = null;
            HasUpdate = false;
            _ = CheckForUpdatesQuietlyAsync(skipDelay: true);
        }

        private void IconSizeControl_SizeChanged(double value)
        {
            IconSize = value;
            ConfigService.IconSize = value;
            LogService.Write("UI", $"IconSizeControl changed newSize={value}");
            UpdateGridItemSizes(value);
            AppGrid.RecalculateAdaptiveWidth();
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
            ConverterSummary.Flush();
        }

        private void AppGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            if (args.ItemContainer is GridViewItem gvi)
            {
                gvi.AllowDrop = true;
                ApplySizeToContainer(gvi, IconSize);
            }
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
                AnnouncementControl?.ApplyLocalization();
                SearchControl?.ApplyLocalization();
                ToolTipService.SetToolTip(BtnMore, Text.T("TitleBar_More"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BtnMore, Text.T("TitleBar_More"));
                MenuIconSizeItem.Text = Text.T("Menu_IconSize");
                MenuAddItem.Text = Text.T("Menu_Add");
                if (MenuScanItem != null) MenuScanItem.Text = Text.T("Menu_Scan");
                _scannerControl?.ApplyLocalization();
                MenuRecycleBinItem.Text = Text.T("Menu_RecycleBin");
                _recycleBinControl?.ApplyLocalization();
                MenuSettingsItem.Text = Text.T("Menu_Settings");
                MenuCheckUpdateItem.Text = Text.T("Menu_CheckUpdate");
                MenuPrivacyItem.Text = Text.T("Privacy_MenuTitle");
                MenuExitItem.Text = Text.T("Menu_Exit");
                UpdateMenuExitVisibility();
                MenuSystemIntegrationItem.Text = Text.T("Menu_SystemIntegration");
                MenuInstallItem.Text = Text.T("Menu_Install");
                MenuUninstallItem.Text = Text.T("Menu_Uninstall");
                IconSizeControl?.ApplyLocalization();
                _settingsControl?.ApplyLocalization();
                UpdateGridInteraction();

                PropertyControl.ApplyLocalization();

                EmptyStateControl?.ApplyLocalization();

                MigrationOverlayControl?.ApplyLocalization();
                HistoryOverlayControl?.ApplyLocalization();
                RefreshAnnouncementList();
                Text.FlushSummary();
            }
            catch (Exception ex)
            {
                LogService.Write("App", "ApplyLocalization failed", ex);
            }
        }



        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuExit_Click");
            if (Application.Current is App app)
            {
                app.RequestFullExit();
                return;
            }

            LogService.Write("UI", "MenuExit_Click could not resolve App instance", null, null, LogService.LogLevel.Error);
            try { Application.Current?.Exit(); } catch (Exception ex) { LogService.Write("UI", "MenuExit_Click fallback exit failed", ex); }
        }

        private void UpdateMenuExitVisibility()
        {
            var visibility = ConfigService.QuickStart ? Visibility.Visible : Visibility.Collapsed;
            if (MenuExitItem.Visibility == visibility && MenuExitSeparator.Visibility == visibility) return;
            MenuExitItem.Visibility = visibility;
            MenuExitSeparator.Visibility = visibility;
            LogService.Write("UI", $"Menu exit visibility={visibility} quickStart={ConfigService.QuickStart}");
        }

        private async Task StartUpdateFlowAsync(UpdateService.ReleaseInfo release, bool isForced = false)
        {
            using (LogService.StartOperation("Update", "StartUpdateFlowAsync"))
            {
                try { await ShowReleaseDialogAsync(release, hasUpdate: true, isForced); }
                catch (Exception ex) { LogService.Write("Update", "StartUpdateFlowAsync failed", ex); }
            }
        }

        private async Task ShowReleaseDialogAsync(UpdateService.ReleaseInfo? release, bool hasUpdate, bool isForced = false, bool checkForUpdates = false)
        {
            bool reopenForced;
            do
            {
            reopenForced = false;
            using (LogService.StartOperation("Update", "ShowReleaseDialogAsync"))
            {
            double viewWidth = this.ActualWidth > 0 ? this.ActualWidth : 950;
            double viewHeight = this.ActualHeight > 0 ? this.ActualHeight : 650;
            double dialogWidth = Math.Max(560, viewWidth * 0.80);
            double dialogHeight = Math.Max(420, viewHeight * 0.78);
            using var loadingLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            var loadingRing = new ProgressRing { IsActive = true, Width = 28, Height = 28 };
            var loadingContent = new StackPanel { Width = dialogWidth - 48, Spacing = 16, Padding = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
            loadingContent.Children.Add(loadingRing);
            loadingContent.Children.Add(new TextBlock { Text = Text.T("Update_Checking"), TextAlignment = TextAlignment.Center });
            var dialog = new ContentDialog
            {
                Title = Text.T("Update_NoUpdateTitle"),
                Content = loadingContent,
                CloseButtonText = isForced ? Text.T("Update_Exit") : Text.T("Close"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };
            dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
            dialog.Resources["ContentDialogMaxHeight"] = dialogHeight;
            bool closed = false;
            bool showHistory = false;
            bool isUpdating = false;
            bool updateStarted = false;
            bool exitRequested = false;
            WebView2? releaseWebView = null;
            void CloseReleaseWebView()
            {
                var view = releaseWebView;
                releaseWebView = null;
                if (view != null)
                {
                    try { view.Close(); }
                    catch (Exception ex) { LogService.Write("Update", "Release WebView close failed", ex); }
                }
            }
            dialog.Closing += (_, e) =>
            {
                if (_lifetime.IsCancellationRequested || updateStarted || showHistory) return;
                if (isUpdating) { e.Cancel = true; return; }
                if (isForced)
                {
                    exitRequested = true;
                    LogService.Write("App", "Exit requested (forced update dialog)");
                }
            };
            dialog.Closed += (_, _) => { closed = true; loadingLifetime.Cancel(); };
            using var closeOnUnload = _lifetime.Token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
            var showTask = dialog.ShowAsync().AsTask();
            try
            {
            LogService.Write("Update", $"Update dialog opened with loading content width={dialogWidth} height={dialogHeight}");
            if (checkForUpdates)
            {
                var status = await UpdateService.CheckUpdateStatusAsync(ConfigService.UpdateChannel).WaitAsync(loadingLifetime.Token);
                release = status.Release;
                hasUpdate = status.HasUpdate;
                isForced = status.IsForced;
                dialog.CloseButtonText = isForced ? Text.T("Update_Exit") : Text.T("Close");
                _pendingUpdate = hasUpdate ? release : null;
                HasUpdate = hasUpdate;
            }
            loadingLifetime.Token.ThrowIfCancellationRequested();
            if (release == null)
            {
                loadingRing.IsActive = false;
                dialog.Content = Text.T("Update_NoUpdateContent");
                dialog.SecondaryButtonText = Text.T("Update_History");
                dialog.CloseButtonText = Text.T("Update_OK");
                if (await showTask == ContentDialogResult.Secondary) await ShowUpdateHistoryAsync();
                return;
            }
            var asset = UpdateService.SelectUpdateAsset(release);
            string downloadUrl = asset?.browser_download_url ?? "";
            if (hasUpdate && string.IsNullOrEmpty(downloadUrl)) throw new InvalidOperationException(Text.T("Update_PackageUnavailable"));
            var contentGrid = await BuildReleaseContentAsync(release, true, dialogWidth, dialogHeight, loadingLifetime.Token, view => releaseWebView = view);
            loadingLifetime.Token.ThrowIfCancellationRequested();
            loadingRing.IsActive = false;
            var updateFailed = false;
            var updateAttempt = 0;
            dialog.Title = hasUpdate
                    ? (object)Text.T("Update_DialogTitle")
                    : Text.T("Update_NoUpdateContent");
            dialog.Content = contentGrid;
            dialog.PrimaryButtonText = hasUpdate ? Text.T("Update_DialogConfirm") : (string.IsNullOrEmpty(downloadUrl) ? "" : Text.T("Update_Repair"));
            dialog.SecondaryButtonText = Text.T("Update_History");
            dialog.CloseButtonText = isForced ? Text.T("Update_Exit") : (hasUpdate ? Text.T("Update_DialogCancel") : Text.T("Update_OK"));
            dialog.DefaultButton = hasUpdate ? ContentDialogButton.Primary : ContentDialogButton.Close;
            LogService.Write("Update", "Update dialog loading content replaced with release details");

            var tcs = new TaskCompletionSource<bool>();

            dialog.SecondaryButtonClick += (s, e) =>
            {
                e.Cancel = true;
                showHistory = true;
                LogService.Write("Update", "History requested from release dialog");
                dialog.Hide();
            };

            dialog.PrimaryButtonClick += async (s, e) =>
            {
                e.Cancel = true;
                isUpdating = true;
                updateFailed = false;
                int attempt = ++updateAttempt;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = false;
                dialog.CloseButtonText = "";
                dialog.PrimaryButtonText = string.Format(Text.T("Update_DownloadProgress"), 0);

                try
                {
                    await UpdateService.StartUpdaterAndWaitAsync(asset!, msg =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_lifetime.IsCancellationRequested || !isUpdating || updateFailed || attempt != updateAttempt) return;
                            if (msg.StartsWith("DOWNLOAD "))
                            {
                                var pct = msg.Split(' ')[1];
                                dialog.PrimaryButtonText = string.Format(Text.T("Update_DownloadProgress"), pct);
                            }
                        });
                    }, _lifetime.Token);
                }
                catch (Exception ex)
                {
                    if (_lifetime.IsCancellationRequested) return;
                    updateFailed = true;
                    dialog.PrimaryButtonText = ex is UpdateVerificationException ? ex.Message : Text.T("Update_Failed");
                    dialog.IsPrimaryButtonEnabled = true;
                    dialog.IsSecondaryButtonEnabled = true;
                    dialog.CloseButtonText = isForced ? Text.T("Update_Exit") : Text.T("Close");
                    LogService.Write("Update", $"Update failed; dialog remains open, forced={isForced}", ex);
                    return;
                }
                finally
                {
                    isUpdating = false;
                }

                updateStarted = true;
                tcs.TrySetResult(true);
                dialog.Hide();
            };

            await showTask;
            if (!tcs.Task.IsCompleted && showHistory)
            {
                CloseReleaseWebView();
                await ShowUpdateHistoryAsync(waitForClose: isForced);
                reopenForced = isForced && !_lifetime.IsCancellationRequested;
            }
            }
            catch (OperationCanceledException) when (loadingLifetime.IsCancellationRequested)
            {
                if (!closed) dialog.Hide();
                await showTask;
                LogService.Write("Update", "Update dialog loading cancelled");
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "Update dialog loading failed", ex);
                if (!closed && !_lifetime.IsCancellationRequested)
                {
                    loadingRing.IsActive = false;
                    dialog.Title = Text.T("Update_CheckFailed");
                    dialog.Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 };
                    dialog.PrimaryButtonText = "";
                    dialog.SecondaryButtonText = "";
                    CloseReleaseWebView();
                    dialog.CloseButtonText = isForced ? Text.T("Update_Exit") : Text.T("Close");
                    await showTask;
                }
            }
            finally
            {
                loadingRing.IsActive = false;
                CloseReleaseWebView();
            }
            if ((exitRequested || updateStarted) && !_lifetime.IsCancellationRequested)
            {
                bool exited = await ((App)Application.Current).RequestFullExitAsync(updateStarted);
                reopenForced = !exited && isForced && !_lifetime.IsCancellationRequested;
            }
            }
            }
            while (reopenForced && !_lifetime.IsCancellationRequested);
        }

        private async Task<Grid> BuildReleaseContentAsync(UpdateService.ReleaseInfo release, bool prependTitle, double dialogW, double dialogH, CancellationToken token, Action<WebView2?> ownWebView)
        {
            using (LogService.StartOperation("Update", "BuildReleaseContentAsync"))
            {
            double innerH = Math.Max(280, dialogH - 150) - 48;

            string bodyHtml = string.IsNullOrEmpty(release.body_html) ? MarkdownHtml.ToHtml(release.body ?? "") : release.body_html;
            if (prependTitle) bodyHtml = $"<h2>{System.Net.WebUtility.HtmlEncode(release.name)}</h2>{bodyHtml}";

            var result = await HtmlViewBuilder.CreateAsync(this, bodyHtml, release.body ?? string.Empty, dialogW - 48, innerH, "", token);
            ownWebView(result.WebView);
            token.ThrowIfCancellationRequested();
            FrameworkElement dialogContent = result.View;

            string channelName = ConfigService.UpdateChannel == "latest"
                ? Text.T("Settings_UpdateChannel_Latest")
                : Text.T("Settings_UpdateChannel_Stable");
            var channelNote = new TextBlock
            {
                Text = string.Format(Text.T("Update_ChannelNote"), channelName),
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

            return grid;
        }
        }

        private async Task ShowUpdateHistoryAsync(bool waitForClose = false)
        {
            using (LogService.StartOperation("Update", "ShowUpdateHistoryAsync"))
            {
                LogService.Write("Update", "History overlay requested");
                var hidden = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnHidden() => hidden.TrySetResult(true);
                if (waitForClose) HistoryOverlayControl.Hidden += OnHidden;
                try
                {
                    await HistoryOverlayControl.ShowAsync();
                    if (waitForClose) await hidden.Task.WaitAsync(_lifetime.Token);
                }
                finally
                {
                    if (waitForClose) HistoryOverlayControl.Hidden -= OnHidden;
                }
            }
        }

        private async void VersionText_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            LogService.Write("App", "VersionText_PointerPressed invoked");
            if (HasUpdate && _pendingUpdate != null)
            {
                bool isForced = UpdateService.IsForcedUpdate(_pendingUpdate);
                await StartUpdateFlowAsync(_pendingUpdate, isForced);
                LogService.Write("App", "VersionText_PointerPressed started update flow");
            }
        }

        public void ActivateAndFocus()
        {
            try
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    WindowActivator.Activate(_hWnd);
                });
            }
            catch (Exception ex) { LogService.Write("App", "MainWindow ActivateAndFocus failed", ex); }
        }

        private void SetTitleBarIcon()
        {
            try
            {
                IntPtr hCustomIcon = AppIconService.GetCustomIconHandle();
                if (hCustomIcon != IntPtr.Zero)
                {
                    try
                    {
                        var customBitmap = AppIconService.GetBitmapImageFromHicon(hCustomIcon);
                        if (customBitmap != null) TitleBarIcon.Source = customBitmap;
                    }
                    finally { DestroyIcon(hCustomIcon); }
                    return;
                }

                var bitmap = AppIconService.GetBitmapImage();
                if (bitmap != null) TitleBarIcon.Source = bitmap;
                LogService.Write("App", "Title bar icon applied");
            }
            catch (Exception ex) { LogService.Write("App", "SetTitleBarIcon failed", ex); }
        }

        #region Win32 Message Interception

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const int GWLP_WNDPROC = -4;
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
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
                _window?.SaveWindowState(null);

                if (_isRefreshPending)
                {
                    RefreshView();
                }
            }

            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }

        #endregion

        private void HostWindow_Closed(object sender, WindowEventArgs args)
        {
            using (LogService.StartOperation("App", "Shutdown"))
            {
                LogService.Write("App", "MainWindow_Closed Start");
                _lifetime.Cancel();
                PropertyControl.EndSession();
                TitleBarGrid.Loaded -= TitleBarGrid_Loaded;
                TitleBarGrid.SizeChanged -= TitleBarGrid_SizeChanged;
                if (_titleBarXamlRoot != null) _titleBarXamlRoot.Changed -= TitleBarXamlRoot_Changed;
                _titleBarXamlRoot = null;
                _externalRefreshTimer?.Stop();
                HistoryOverlayControl.Hide();
                ServerConfigManager.AnnouncementsUpdated -= OnAnnouncementsUpdated;
                if (_languageChangedHandler != null)
                {
                    Text.LanguageChanged -= _languageChangedHandler;
                    _languageChangedHandler = null;
                }
                if (_dataChangedHandler != null)
                {
                    ConfigService.DataChanged -= _dataChangedHandler;
                    _dataChangedHandler = null;
                }
                if (_quickStartChangedHandler != null)
                {
                    QuickStartService.EnabledChanged -= _quickStartChangedHandler;
                    _quickStartChangedHandler = null;
                }
                CancelCloseAfterLaunch();
                if (_tooltipTimer != null)
                {
                    _tooltipTimer.Stop();
                    _tooltipTimer.Tick -= TooltipTimer_Tick;
                    _tooltipTimer = null;
                }
                if (_oldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(_hWnd, GWLP_WNDPROC, _oldWndProc);
                    _oldWndProc = IntPtr.Zero;
                }
                _wndProcDelegate = null;
                LogService.Write("App", "MainWindow_Closed End");
            }
        }

        private void StartCloseAfterLaunchTimer()
        {
            if (_closeAfterLaunchPending) return;
            _closeAfterLaunchPending = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _closeAfterLaunchInputRoot ??= this;
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

        private async void CloseAfterLaunchTimer_Tick(object? sender, object e)
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
                        if (QuickStartService.IsActive && _window != null)
                        {
                            LogService.Write("QuickStart", "Close after launch closes main window, host keeps running");
                            if (await _window.ConfirmSaveBeforeCloseAsync()) _window.Close();
                        }
                        else
                        {
                            LogService.Write("App", "Exit requested (CloseAfterLaunch)");
                            await ((App)Application.Current).RequestFullExitAsync();
                        }
                    }
                    else
                    {
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
    }
}


