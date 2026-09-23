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
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WinRT.Interop;

namespace EricGameLauncher
{
    public sealed partial class MainView : UserControl, INotifyPropertyChanged
    {
        private ObservableCollection<AppItem> _allItems = new();
        private ObservableCollection<AppItem> _recycleItems = new();
        private ObservableCollection<AppItem> _viewItems = new();
        private RecycleBinFlyoutControl? _recycleBinControl;
        private SettingsFlyoutControl? _settingsControl;
        private SettingsFlyoutControl SettingsControl => _settingsControl ??= CreateSettingsControl();
        private ScannerDialogControl? _scannerControl;

        private SettingsFlyoutControl CreateSettingsControl()
        {
            var control = new SettingsFlyoutControl();
            control.LaunchModeChanged += SettingsControl_LaunchModeChanged;
            control.UpdateChannelChanged += SettingsControl_UpdateChannelChanged;
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


                    if (SearchControl != null)
                    {
                        if (value)
                        {

                            SearchControl.Button.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                        }
                        else
                        {

                            SearchControl.Button.ClearValue(Button.BackgroundProperty);
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
        private DateTime _lastLaunchTime = DateTime.MinValue;
        private bool _isRootLoaded = false;
        private bool _pendingInitialRefresh = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        private MainWindow? _window;
        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private WndProc? _wndProcDelegate;
        private FrameworkElement? _closeAfterLaunchInputRoot;
        private DispatcherTimer? _closeAfterLaunchTimer;
        private bool _closeAfterLaunchPending = false;

        public MainView()
        {
            this.InitializeComponent();
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
            _ = InitializeAfterActivationAsync();
        }

        private void SetupWindowShell()
        {
            var sw = Stopwatch.StartNew();
            LogService.Write("Startup", $"Ctor Start [Version: {AppVersion.DisplayVersion}]");
            try { StartupArgs.Parse(); DebugPaths.ApplyIfDebug(); } catch { }

            if (_window == null) return;

            TitleBarText.Text = _window.Title;

            _window.SetTitleBar(TitleBarGrid);

            _window.Closed += HostWindow_Closed;

            _oldWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = new WndProc(WindowProcess)));
            LogService.Write("Startup", $"Ctor End duration={sw.ElapsedMilliseconds}ms");
        }

        private void WireControls()
        {
            LogService.Write("Startup", "Main view initialize start");
            SearchControl.QueryChanged += SearchBox_TextChanged;
            IconSizeControl.IconSizeChanged += IconSizeControl_SizeChanged;
            OrderControl.OrderCommitted += OrderControl_OrderCommitted;
            PropertyControl.SaveRequested += PropertyPanel_SaveRequested;
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
            try
            {
                SetTitleBarIcon();
                _window?.SetSplashIcon();
                LogService.Write("Startup", "Startup overlay prepared");
                StartupArgs.LogEnvironment();
                ServerConfigManager.LoadReadIds();

                Text.Load(ConfigService.Language);
                Text.LanguageChanged += () =>
                {
                    DispatcherQueue.TryEnqueue(() => ApplyLocalization());
                };

                ServerConfigManager.AnnouncementsUpdated += OnAnnouncementsUpdated;

                ConfigService.IconSize = ConfigService.IconSize;
                ConfigService.DataChanged += () =>
                {
                    DispatcherQueue.TryEnqueue(() => RefreshView());
                };

                LoadSettings();
                ApplyLocalization();
                RefreshAnnouncementList();

                _ = LoadDataAsync();
                _ = InitializeNetworkTasksAsync();
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
                .ToList();

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

                    var status = await UpdateService.CheckUpdateStatusAsync(ConfigService.UpdateChannel);
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
                var status = await UpdateService.CheckUpdateStatusAsync(ConfigService.UpdateChannel);
                if (status.Release != null)
                {
                    if (status.HasUpdate)
                    {
                        var release = status.Release;
                        _pendingUpdate = release;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            HasUpdate = true;
                        });

                        if (status.IsForced)
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
                        await ShowReleaseDialogAsync(status.Release, hasUpdate: false);
                    }
                }
                else
                {
                    ContentDialog noUpdateDialog = new ContentDialog
                    {
                        Title = Text.T("Update_NoUpdateTitle"),
                        Content = Text.T("Update_NoUpdateContent"),
                        CloseButtonText = Text.T("Update_OK"),
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = this.XamlRoot
                    };
                    await noUpdateDialog.ShowAsync();
                }
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


        private void LoadSettings()
        {
            using (LogService.StartOperation("App", "LoadSettings"))
            {
            try
            {
                LogService.Write("App", $"LoadSettings Start CloseAfterLaunch={ConfigService.CloseAfterLaunch} LaunchMode={ConfigService.LaunchMode} IconSize={ConfigService.IconSize}");

                AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";

                IconSizeControl.SetValue(ConfigService.IconSize);
                IconSize = IconSizeControl.Value;
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

                    List<AppItem> items;
                    List<AppItem> recycleItems;
                    items = ConfigService.LoadItems();
                    recycleItems = ConfigService.LoadRecycleBinItems();
                    ApplyItemData(items, recycleItems);
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
                _allItems = new ObservableCollection<AppItem>(normalItems);
                _recycleItems = new ObservableCollection<AppItem>(normalizedRecycle);
                _viewItems = new ObservableCollection<AppItem>(_allItems);
                AppGrid.ItemsSource = _viewItems;
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
                    MigrationOverlayControl.Visibility = Visibility.Visible;
                    await Task.Delay(200);
                    await MigrationService.RunMigrationAndRestart();
                    return;
                }

                LogService.Write("Startup", $"LoadData BeforeRefreshGlobal {sw.ElapsedMilliseconds}ms");
                await ConfigService.RefreshGlobalAsync();
                LogService.Write("Startup", $"LoadData AfterRefreshGlobal {sw.ElapsedMilliseconds}ms");

                var loadedItems = ConfigService.LoadItems();
                var loadedRecycleItems = ConfigService.LoadRecycleBinItems();
                ApplyItemData(loadedItems, loadedRecycleItems);
                AppGrid.ItemsSource = _viewItems;
                LogService.Write("Startup", $"LoadData PopulatedCollections all={_allItems.Count} recycle={_recycleItems.Count}");

                _preloadedStartMenuTask = Task.Run(() => ShortcutScanner.GetStartMenuItems());
                _preloadedDesktopTask = Task.Run(() => ShortcutScanner.GetDesktopItems());
                    LogService.Write("Startup", $"LoadData AfterPreloadTasks {sw.ElapsedMilliseconds}ms");
                }
            }
            catch (Exception ex) { LogService.Write("App", "LoadData failed", ex); }
        }

        private void SyncFromCore()
        {
            _allItems = new ObservableCollection<AppItem>(ConfigService.LoadItems());
            _recycleItems = new ObservableCollection<AppItem>(ConfigService.LoadRecycleBinItems());
            RefreshGridItems();
        }

        private void RefreshGridItems()
        {
            var query = SearchControl?.Text?.Trim() ?? "";
            IEnumerable<AppItem> items = string.IsNullOrEmpty(query)
                ? _allItems
                : ItemService.Search(_allItems, query.ToLowerInvariant());

            _viewItems = new ObservableCollection<AppItem>(items);
            AppGrid.ItemsSource = _viewItems;
            IsFiltered = !string.IsNullOrEmpty(query);
            UpdateEmptyState();
            LogService.Write("App", $"RefreshGridItems applied count={_viewItems.Count} filtered={IsFiltered}");
        }


        private DispatcherTimer? _tooltipTimer;
        private AppItem? _hoveredItem;
        private FrameworkElement? _hoveredElement;

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
                TriggerItemLoadingAnimation(item);
                LaunchService.Launch(item, item.UseAlternativeLaunch);
                if (ConfigService.CloseAfterLaunch)
                    StartCloseAfterLaunchTimer();
            }
            catch (Exception ex) { LogService.Write("App", "LaunchItem failed", ex); }
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


        private void MenuCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuFlyoutItem menuItem && menuItem.Tag is CustomMenuItem ci)
                {
                    if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                    _lastLaunchTime = DateTime.Now;
                    LaunchService.LaunchCustomCommand(ci);
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

                if ((DateTime.Now - _lastLaunchTime).TotalMilliseconds < 500) return;
                _lastLaunchTime = DateTime.Now;

                TriggerItemLoadingAnimation(item);
                LaunchService.LaunchManager(item);
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
                    ItemService.RemoveItem(item.Id, null);
                    SyncFromCore();
                    RefreshView();
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
            try
            {
                using (LogService.StartOperation("Scan", "Scan"))
                {
                    LogService.Write("Scan", "Scan Start");
                await ConfigService.ReconstructMissingConfigAsync();

                var scannerControl = CreateScannerControl();
                scannerControl.ShowLoading();

                var dialogTask = scannerControl.ShowAsync();

                var scannedGames = await ScanService.ScanAllAsync();

                LogService.Write("Scan", $"MenuScan_Click scannedGamesCount={scannedGames?.Count ?? 0}");

                bool canValidateSteam = ScanService.CanValidateSteam;
                bool canValidateEpic = ScanService.CanValidateEpic;

                var allItems = _allItems.Concat(_recycleItems).ToList();
                var (newGames, existingGames) = ScanService.Classify(scannedGames ?? new List<ScannedGame>(), allItems);

                LogService.Write("Scan", $"MenuScan_Click classification existing={existingGames.Count} new={newGames.Count}");

                var invalidGames = ScanService.FindInvalidGames(
                    _allItems.ToList(), scannedGames ?? new List<ScannedGame>(),
                    canValidateSteam, canValidateEpic);

                scannerControl.ShowResults(newGames, existingGames, invalidGames);
                    LogService.Write("Scan", "Scan Complete");
                }
            }
            catch (Exception ex) { LogService.Write("Scan", "Scan failed", ex); }
        }

        private ScannerDialogControl CreateScannerControl()
        {
            if (_scannerControl == null)
            {
                _scannerControl = new ScannerDialogControl();
                _scannerControl.ImportRequested += ScannerControl_ImportRequested;
                _scannerControl.DeleteInvalidRequested += ScannerControl_DeleteInvalidRequested;
                LogService.Write("UI", "Scanner control created on demand");
            }
            _scannerControl.XamlRoot = this.XamlRoot;
            _scannerControl.ApplyLocalization();
            return _scannerControl;
        }

        private void ScannerControl_ImportRequested(IReadOnlyList<ScannedGame> games)
        {
            ImportScannedGames(games.ToList());
        }

        private void ScannerControl_DeleteInvalidRequested(IReadOnlyList<ScannedGame> games)
        {
            DeleteInvalidGames(games.ToList());
        }

        private void DeleteInvalidGames(List<ScannedGame> games)
        {
            try
            {
                ScanService.DeleteInvalidGames(games);
                SyncFromCore();
            }
            catch (Exception ex) { LogService.Write("App", "DeleteInvalidGames failed", ex); }
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

        private void ImportScannedGames(List<ScannedGame> games)
        {
            try
            {
                if (games == null || games.Count == 0) return;
                var addedCount = ItemService.ImportGames(games);
                SyncFromCore();
                _ = ConfigService.RefreshGlobalAsync();
                LogService.Write("Scan", $"ImportScannedGames completed imported={addedCount}");
            }
            catch (Exception ex) { LogService.Write("App", "ImportScannedGames failed", ex); }
        }





        private void PropertyPanel_SaveRequested()
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

                    if (isNew)
                        ItemService.AddItem(item);
                    else
                        ItemService.EditItem(item, _ => { });

                    SyncFromCore();
                    RefreshView();
                    PropertyControl.ClosePanel();
                    LogService.Write("App", $"PropertyPanel_SaveRequested completed id={item.Id} isNew={isNew}");
                }
                catch (Exception ex) { LogService.Write("App", "PropertyPanel_SaveRequested failed", ex); }
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
        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (sender == null) return;

                string query = (sender.Text ?? "").ToLower().Trim();

                IEnumerable<AppItem> filtered = string.IsNullOrEmpty(query)
                    ? _allItems
                    : ItemService.Search(_allItems, query);

                _viewItems = new ObservableCollection<AppItem>(filtered);
                AppGrid.ItemsSource = _viewItems;

                IsFiltered = !string.IsNullOrEmpty(query);
                UpdateEmptyState();
            }
            catch (Exception ex) { LogService.Write("App", "EditOrderFlyout_Opening failed", ex); }
        }

        private void OrderControl_OrderCommitted(IReadOnlyList<AppItem> newOrder)
        {
            try
            {
                _allItems.Clear();
                foreach (var item in newOrder)
                    _allItems.Add(item);
                RefreshGridItems();
                LogService.Write("App", $"OrderControl applied order count={newOrder.Count}");
            }
            catch (Exception ex) { LogService.Write("App", "OrderControl_OrderCommitted failed", ex); }
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

        private void MenuSort_Click(object sender, RoutedEventArgs e)
        {
            LogService.Write("UI", "MenuSort_Click invoked");
            OrderControl.Open(_allItems);
            OrderControl.ApplyLocalization();
            OrderControl.Flyout.ShowAt(BtnMore);
            LogService.Write("UI", "MenuSort_Click showed OrderFlyoutControl");
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

        private void MenuUninstall_Click(object sender, RoutedEventArgs e)
        {
            try { AppInstallService.Uninstall(); }
            catch (Exception ex) { LogService.Write("App", "MenuUninstall_Click failed", ex); }
        }


        private void SettingsControl_LaunchModeChanged(string mode)
        {
            AppGrid.IsItemClickEnabled = mode != "double";
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
                MenuIconSizeItem.Text = Text.T("Menu_IconSize");
                MenuAddItem.Text = Text.T("Menu_Add");
                if (MenuScanItem != null) MenuScanItem.Text = Text.T("Menu_Scan");
                _scannerControl?.ApplyLocalization();
                MenuSortItem.Text = Text.T("Menu_Sort");
                MenuRecycleBinItem.Text = Text.T("Menu_RecycleBin");
                _recycleBinControl?.ApplyLocalization();
                MenuSettingsItem.Text = Text.T("Menu_Settings");
                MenuCheckUpdateItem.Text = Text.T("Menu_CheckUpdate");
                MenuPrivacyItem.Text = Text.T("Privacy_MenuTitle");
                MenuSystemIntegrationItem.Text = Text.T("Menu_SystemIntegration");
                MenuInstallItem.Text = Text.T("Menu_Install");
                MenuUninstallItem.Text = Text.T("Menu_Uninstall");
                IconSizeControl?.ApplyLocalization();
                OrderControl?.ApplyLocalization();
                SettingsControl?.ApplyLocalization();
                AppGrid.IsItemClickEnabled = ConfigService.LaunchMode != "double";

                PropertyControl.ApplyLocalization();

                EmptyStateControl?.ApplyLocalization();

                MigrationOverlayControl?.ApplyLocalization();
                RefreshAnnouncementList();
                Text.FlushSummary();
            }
            catch (Exception ex)
            {
                LogService.Write("App", "ApplyLocalization failed", ex);
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
            string downloadUrl = release.assets?.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.browser_download_url ?? "";
            if (hasUpdate && string.IsNullOrEmpty(downloadUrl)) return;

            var (contentGrid, dlgW, dlgH) = await BuildReleaseContentAsync(release, prependTitle: true);
            var dialog = new ContentDialog
            {
                Title = hasUpdate
                    ? (object)Text.T("Update_DialogTitle")
                    : Text.T("Update_NoUpdateContent"),
                Content = contentGrid,
                PrimaryButtonText = hasUpdate ? Text.T("Update_DialogConfirm") : (string.IsNullOrEmpty(downloadUrl) ? "" : Text.T("Update_Repair")),
                CloseButtonText = isForced ? Text.T("Update_Exit") : (hasUpdate ? Text.T("Update_DialogCancel") : Text.T("Update_OK")),
                DefaultButton = hasUpdate ? ContentDialogButton.Primary : ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

                if (isForced)
            {
                dialog.Closing += (s, e) =>
                {
                    if (e.Result != ContentDialogResult.Primary)
                    {
                        e.Cancel = true;
                        LogService.Write("App", "Exit requested (forced update dialog)");
                        _window?.AllowRealExit();
                        try { Application.Current.Exit(); } catch (Exception ex) { LogService.Write("App", "Exit failed (forced update dialog)", ex); }
                    }
                };
            }

            dialog.Resources["ContentDialogMaxWidth"] = dlgW;
            dialog.Resources["ContentDialogMaxHeight"] = dlgH;

            var tcs = new TaskCompletionSource<bool>();
            var isUpdating = false;

            dialog.PrimaryButtonClick += async (s, e) =>
            {
                e.Cancel = true;
                isUpdating = true;
                dialog.IsPrimaryButtonEnabled = false;
                dialog.CloseButtonText = "";
                dialog.PrimaryButtonText = string.Format(Text.T("Update_DownloadProgress"), 0);

                try
                {
                    await UpdateService.StartUpdaterAndWaitAsync(downloadUrl, msg =>
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (msg.StartsWith("DOWNLOAD "))
                            {
                                var pct = msg.Split(' ')[1];
                                dialog.PrimaryButtonText = string.Format(Text.T("Update_DownloadProgress"), pct);
                            }
                        });
                    });
                }
                finally
                {
                    isUpdating = false;
                }

                tcs.SetResult(true);
                dialog.Hide();
            };

            dialog.Closing += (s, e) =>
            {
                if (isUpdating) e.Cancel = true;
            };

            var result = await dialog.ShowAsync();
            if (tcs.Task.IsCompleted)
            {
                LogService.Write("App", "Exit requested (update start)");
                _window?.AllowRealExit();
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
            double viewWidth = this.ActualWidth > 0 ? this.ActualWidth : 950;
            double viewHeight = this.ActualHeight > 0 ? this.ActualHeight : 650;
            double dialogW = Math.Max(560, viewWidth * 0.80);
            double dialogH = Math.Max(420, viewHeight * 0.78);
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
                var actualTheme = this.ActualTheme;
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
                dialogContent = new ScrollViewer
                {
                    Content = new TextBlock { Text = release.body ?? string.Empty, TextWrapping = TextWrapping.Wrap },
                    Height = innerH
                };
            }

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

            return (grid, dialogW, dialogH);
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
                    var customBitmap = AppIconService.GetBitmapImageFromHicon(hCustomIcon);
                    if (customBitmap != null) TitleBarIcon.Source = customBitmap;
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
                ConfigService.SaveAll();
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
                        if (QuickStartService.IsActive && _window != null)
                        {
                            LogService.Write("QuickStart", "Close after launch switched to background mode");
                            _window.EnterBackgroundMode();
                        }
                        else
                        {
                            LogService.Write("App", "Exit requested (CloseAfterLaunch)");
                            ConfigService.SaveAll();
                            try { Application.Current.Exit(); } catch (Exception ex) { LogService.Write("App", "Exit failed (CloseAfterLaunch)", ex); }
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


