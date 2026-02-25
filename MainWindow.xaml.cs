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
        private ObservableCollection<AppItem> _viewItems = new();
        private AppItem? _currentEditingItem = null;
        private bool _isNewItemMode = false;


        private ToggleSwitch? _toggleCloseAfterLaunch;
        private Slider? _sizeSlider;
        private ListView? _orderItemsControl;
        private StackPanel[] _customSections;
        private TextBox[] _customTitles;
        private TextBox[] _customCommands;
        private CheckBox[] _customAdmins;
        private DropDownButton[] _customBrowses;
        private TextBlock[] _customAdminLabels;
        private TextBlock[] _customSlotLabels;

        // P1: Preloaded shortcut sources. Background-populated during startup so that
        // opening the property panel does not block the UI thread with Shell COM calls.
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
                    _iconSize = value;
                    OnPropertyChanged(nameof(IconSize));
                }
            }
        }

        private bool _isFiltered;
        public bool IsFiltered
        {
            get => _isFiltered;
            set
            {
                if (_isFiltered != value)
                {
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

        private Brush _updateIndicatorColor = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        public Brush UpdateIndicatorColor
        {
            get => _updateIndicatorColor;
            set { if (_updateIndicatorColor != value) { _updateIndicatorColor = value; OnPropertyChanged(nameof(UpdateIndicatorColor)); } }
        }

        private bool _hasUpdate;
        public bool HasUpdate
        {
            get => _hasUpdate;
            set { if (_hasUpdate != value) { _hasUpdate = value; OnPropertyChanged(nameof(HasUpdate)); } }
        }

        private UpdateService.ReleaseInfo? _pendingUpdate;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            this.InitializeComponent();

            // Initialize custom menu section arrays
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
                _customBrowses[index].Click += (s, e) => { /* Flyout handled in PopulateShortcutMenus */ };
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
                    else
                    {
                        PropPlatformBadgeContainer.Visibility = Visibility.Collapsed;
                    }
                }
                catch { }
            };


            VersionText.Text = AppVersion.DisplayVersion;



            if (this.Content is FrameworkElement rootElement)
            {
                rootElement.Loaded += (s, e) =>
                {
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
                    catch (Exception) { }
                };
            }

            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            this.ExtendsContentIntoTitleBar = true;


            this.Title = "EricGameLauncher";


            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("EricGameLauncher.ico.ico");
                if (stream != null)
                {
                    string tempIconPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EricGameLauncher_TempIcon.ico");
                    using var fileStream = new System.IO.FileStream(tempIconPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
                    stream.CopyTo(fileStream);
                    fileStream.Close();

                    this.AppWindow.SetIcon(tempIconPath);

                    var bitmap = new BitmapImage(new Uri(tempIconPath));
                    TitleBarIcon.Source = bitmap;
                }
            }
            catch (Exception) { }


            var titleBar = this.AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            var inputNonClientPointerSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
            inputNonClientPointerSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption, new Windows.Graphics.RectInt32[] {
                new Windows.Graphics.RectInt32(0, 0, (int)this.Bounds.Width, 48)
            });

            ConfigService.Initialize();

            // Initialize localization
            I18n.Load(ConfigService.Language);
            I18n.LanguageChanged += () =>
            {
                DispatcherQueue.TryEnqueue(() => ApplyLocalization());
            };

            IconSize = ConfigService.IconSize;


            RestoreWindowState();


            this.Closed += MainWindow_Closed;


            this.AppWindow.Changed += AppWindow_Changed;


            LoadSettings();
            ApplyLocalization();

            // Intercept Win32 messages to detect user-initiated resize/move
            _hWnd = WindowNative.GetWindowHandle(this);
            _oldWndProc = SetWindowLongPtr(_hWnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate = new WndProc(WindowProcess)));

            _ = LoadData();

            // 启动在线更新静默检查
            _ = CheckForUpdatesQuietlyAsync();
        }

        private async Task CheckForUpdatesQuietlyAsync()
        {
            try
            {
                // 等待 3 秒，确保系统资源优先分配给主界面图标加载
                await Task.Delay(3000);

                var release = await UpdateService.CheckForUpdateAsync();
                if (release != null)
                {
                    _pendingUpdate = release;

                    // 使用低优先级更新 UI，避免干扰用户交互和渲染
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        HasUpdate = true;
                        UpdateIndicatorColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0)); // 红色提醒
                    });
                }
            }
            catch { }
        }

        private async void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            var release = await UpdateService.CheckForUpdateAsync();
            if (release != null)
            {
                _pendingUpdate = release;

                DispatcherQueue.TryEnqueue(() =>
                {
                    HasUpdate = true;
                    UpdateIndicatorColor = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0));
                });

                // 直接进入更新确认流程
                await StartUpdateFlowAsync(release);
            }
            else
            {
                // 已经是最新版本，弹出提示
                ContentDialog noUpdateDialog = new ContentDialog
                {
                    Title = I18n.T("Update_NoUpdateTitle"),
                    Content = I18n.T("Update_NoUpdateContent"),
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };
                await noUpdateDialog.ShowAsync();
            }
        }

        #region Win32 Message Interception
        private IntPtr _hWnd;
        private IntPtr _oldWndProc;
        private WndProc? _wndProcDelegate;

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;
        private const uint WM_EXITSIZEMOVE = 0x0232;

        private IntPtr WindowProcess(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_EXITSIZEMOVE)
            {
                // User finished resizing or moving the window
                SaveWindowState(null);
            }
            return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
        }
        #endregion

        private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            // We no longer save window state here to avoid system-triggered drift.
            // Saving is now handled via WM_EXITSIZEMOVE in WindowProcess.

            if (args.DidSizeChange && this.AppWindow != null)
            {
                // Update hit test region if size changed
                var inputNonClientPointerSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
                inputNonClientPointerSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Caption, new Windows.Graphics.RectInt32[] {
                    new Windows.Graphics.RectInt32(0, 0, this.AppWindow.Size.Width, 48)
                });
            }
        }

        private void RestoreWindowState()
        {
            try
            {
                var (x, y, width, height) = ConfigService.GetWindowBounds();

                // Use Resize instead of ResizeClient to avoid drifting when title bar is extended
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

                    // Clamp saved position into the current work area so window does not drift off-screen
                    int targetX = Math.Clamp(x, workLeft, Math.Max(workRight - 100, workLeft));
                    int targetY = Math.Clamp(y, workTop, Math.Max(workBottom - 100, workTop));

                    this.AppWindow.Move(new Windows.Graphics.PointInt32(targetX, targetY));
                }
            }
            catch (Exception)
            {
                this.AppWindow.Resize(new Windows.Graphics.SizeInt32(950, 650));
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // M3: restore the original WndProc to prevent use-after-free when the
            // WinUI 3 window is destroyed while our hook is still installed.
            if (_oldWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_hWnd, GWLP_WNDPROC, _oldWndProc);
                _oldWndProc = IntPtr.Zero;
            }
            _wndProcDelegate = null;
        }

        private void MenuAuthorIconInternal_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Image img)
                {
                    string tempIconPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EricGameLauncher_TempIcon.ico");
                    if (System.IO.File.Exists(tempIconPath))
                    {
                        img.Source = new BitmapImage(new Uri(tempIconPath));
                    }
                }
            }
            catch (Exception) { }
        }

        private void SaveWindowState(Microsoft.UI.Windowing.AppWindowChangedEventArgs? args)
        {
            try
            {
                var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                if (presenter != null && (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized ||
                                         presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized))
                {
                    // Do not save window state when minimized or maximized
                    return;
                }

                var current = ConfigService.GetWindowBounds();
                int x = current.X, y = current.Y, width = current.Width, height = current.Height;

                // Use Size instead of ClientSize for consistent mapping with Resize
                var size = this.AppWindow.Size;
                var position = this.AppWindow.Position;

                bool changed = false;
                // If args is null, it means it's triggered by WM_EXITSIZEMOVE, so we always check for changes
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
                    ConfigService.SaveConfig();
                }
            }
            catch (Exception) { }
        }

        private void LoadSettings()
        {
            try
            {

                if (_toggleCloseAfterLaunch != null)
                {
                    _toggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
                }


                if (_sizeSlider != null)
                {
                    _sizeSlider.Value = ConfigService.IconSize;
                }
            }
            catch (Exception) { }
        }

        private void RefreshView()
        {
            _viewItems = new ObservableCollection<AppItem>(_allItems);
            AppGrid.ItemsSource = _viewItems;
            UpdateEmptyState();


            this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                UpdateGridItemSizes(IconSize);
            });
        }

        private void UpdateEmptyState()
        {
            try
            {

                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
            catch (Exception) { }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadData()
        {
            try
            {
                if (ConfigService.RequiresMigration)
                {
                    MigrationOverlay.Visibility = Visibility.Visible;
                    await Task.Delay(200); // 确保遮罩层渲染

                    string configPath = System.IO.Path.Combine(ConfigService.CurrentDataPath, "config.json");
                    try
                    {
                        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EricGameLauncher");
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
                            Arguments = $"\"{configPath}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        var process = System.Diagnostics.Process.Start(processStartInfo);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Silent Migration Failed: {ex.Message}");
                    }

                    // 自动重启应用以载入新版本配置
                    try
                    {
                        string? currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(currentExe))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = currentExe,
                                UseShellExecute = true
                            });
                        }
                    }
                    catch { }

                    Microsoft.UI.Xaml.Application.Current.Exit();
                    return;
                }

                var items = ConfigService.LoadItems();
                _allItems = new ObservableCollection<AppItem>(items);


                RefreshView();


                var rebuildTasks = new List<Task>();
                bool anyRebuildNeeded = false;

                foreach (var item in _allItems)
                {
                    if (string.IsNullOrEmpty(item.IconPath) || !File.Exists(item.IconPath))
                    {
                        anyRebuildNeeded = true;
                        rebuildTasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                string? sourcePath = item.ExePath;
                                string? resolvedPath = null;

                                if (SteamHelper.ExtractAppIdFromUrl(item.ExePath!) is int)
                                {
                                    resolvedPath = SteamHelper.GetExecutableFromSteamUrl(item.ExePath!);
                                }
                                else if (GamePlatformHelper.DetectPlatform(item.ExePath!)?.PlatformName == "Epic Games")
                                {
                                    resolvedPath = EpicGamesHelper.GetExecutableFromEpicUrl(item.ExePath!);
                                }
                                else if (!string.IsNullOrEmpty(item.ExePath) &&
                                         (item.ExePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                          item.ExePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
                                {
                                    if (File.Exists(item.ExePath))
                                    {
                                        if (item.ExePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var info = ShortcutResolver.GetShortcutInfo(item.ExePath);
                                            if (info != null && !string.IsNullOrEmpty(info.TargetPath))
                                                resolvedPath = info.TargetPath;
                                        }
                                        else if (item.ExePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                                        {
                                            var info = ShortcutResolver.GetUrlFileInfo(item.ExePath);
                                            if (info != null && !string.IsNullOrEmpty(info.TargetPath))
                                                resolvedPath = info.TargetPath;
                                        }
                                    }
                                }
                                else
                                {
                                    resolvedPath = item.ExePath;
                                }

                                string? iconPath = null;
                                if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                                {
                                    iconPath = await IconHelper.GetIconPathAsync(resolvedPath, item.Id);
                                    if (string.IsNullOrEmpty(iconPath) && !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath) && sourcePath != resolvedPath)
                                    {
                                        iconPath = await IconHelper.GetIconPathAsync(sourcePath, item.Id);
                                    }
                                }
                                else if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
                                {
                                    iconPath = await IconHelper.GetIconPathAsync(sourcePath, item.Id);
                                }

                                if (!string.IsNullOrEmpty(iconPath))
                                {
                                    DispatcherQueue.TryEnqueue(() =>
                                    {
                                        item.IconPath = iconPath;
                                    });
                                }
                            }
                            catch (Exception) { }
                        }));
                    }
                }

                if (anyRebuildNeeded)
                {
                    // R3: use Task.WhenAll so we save only after ALL icons have been
                    // extracted to disk, eliminating the prior 200ms race condition.
                    _ = Task.WhenAll(rebuildTasks).ContinueWith(_ =>
                    {
                        // Each rebuild task has already called DispatcherQueue.TryEnqueue
                        // to update item.IconPath. Because the DispatcherQueue is FIFO,
                        // this save request will execute AFTER those updates.
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            ConfigService.SaveItems(_allItems.ToList());
                            RefreshView();
                        });
                    }, TaskScheduler.Default);
                }



                // 3. Reconstruct missing config items (like 'platform') in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // 稍微延迟，避开启动高峰
                        await ConfigService.ReconstructMissingConfigAsync();

                        // M2 fix: ReconstructMissingConfigAsync updates DTOs (AppItemDto) in
                        // _configData.Items, but _allItems holds separate ViewModel objects.
                        // We must sync the reconstructed Platform field back to live ViewModels
                        // before calling RefreshView, otherwise badges won't appear.
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            var updated = ConfigService.LoadItems();
                            foreach (var upd in updated)
                            {
                                var vm = _allItems?.FirstOrDefault(x => x.Id == upd.Id);
                                if (vm != null && vm.Platform != upd.Platform)
                                    vm.Platform = upd.Platform;
                            }
                            RefreshView();
                        });
                    }
                    catch (Exception) { }
                });

                // P1: Preload shortcut sources in background so they are ready when
                // the user opens the property panel for the first time.
                _preloadedStartMenuTask = Task.Run(() => ShortcutScanner.GetStartMenuItems());
                _preloadedDesktopTask = Task.Run(() => ShortcutScanner.GetDesktopItems());
            }
            catch (Exception) { }
        }

        private void SaveData()
        {
            try
            {
                ConfigService.SaveItems(_allItems.ToList());
            }
            catch (Exception) { }
        }


        private void AppGrid_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                var item = e.ClickedItem as AppItem;
                if (item != null)
                {
                    LaunchItem(item);
                }
            }
            catch (Exception) { }
        }




        private void LaunchItem(AppItem item)
        {
            try
            {

                if (item.UseAlternativeLaunch && !string.IsNullOrEmpty(item.AlternativeLaunchCommand))
                {

                    RunProcess(item.AlternativeLaunchCommand, item.IsAltAdmin);
                }
                else if (!string.IsNullOrEmpty(item.ExePath))
                {

                    RunProcess(item.ExePath, item.IsAdmin);


                    if (item.RunAlongside && !string.IsNullOrEmpty(item.AlongsideCommand))
                    {
                        RunProcess(item.AlongsideCommand, item.IsAlongsideAdmin);
                    }
                }
            }
            catch (Exception) { }
        }

        private void RunProcess(string path, bool admin)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {

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
                        // S4: Use -EncodedCommand (Base64) to safely pass the path
                        // without risk of PowerShell injection via special characters
                        // such as ", $, `, & that could remain after simple escaping.
                        string psScript = $"Start-Process '{path.Replace("'", "''")}' -Verb RunAs";
                        string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));
                        psi.FileName = "powershell.exe";
                        psi.Arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
                        psi.UseShellExecute = true;
                        psi.CreateNoWindow = true;
                    }
                    else
                    {
                        psi.FileName = "explorer.exe";
                        psi.Arguments = path;
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
                    if (!string.IsNullOrEmpty(arguments))
                    {
                        psi.Arguments = arguments;
                    }
                    if (admin)
                        psi.Verb = "runas";
                }

                Process? process = Process.Start(psi);

                if (ConfigService.CloseAfterLaunch)
                    Application.Current.Exit();
            }
            catch (Exception) { }
        }









        private (string filePath, string arguments) SplitPathAndArguments(string input)
        {
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
                if (sender is MenuFlyout menu && menu.Target is FrameworkElement target)
                {
                    return target.Tag as AppItem;
                }
                if (sender is FrameworkElement fe) return fe.Tag as AppItem;
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void MenuRun_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    LaunchItem(item);
                }
            }
            catch (Exception) { }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
            if (sender is MenuFlyout menu)
            {
                var item = GetTag(menu);
                if (item == null) return;

                // 1. Localization & Basic Filtering
                bool isPeFile = false;
                try
                {
                    string path = (item.ExePath ?? "").Trim('\"');
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) isPeFile = true;
                    else if (item.ExePath?.Contains(" ") == true && !item.ExePath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) isPeFile = true; // Likely exe with args
                }
                catch { }

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

                            // L7: Hide "Open File Location" for non-PE files
                            if (si.Symbol == Symbol.Folder)
                            {
                                menuItem.Visibility = isPeFile ? Visibility.Visible : Visibility.Collapsed;
                            }

                            // L4: We'll handle Manager item specially below
                            if (si.Symbol == Symbol.Repair)
                            {
                                menuItem.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                }

                // 2. Clear dynamic items
                var toRemove = menu.Items.Where(i =>
                    i.Tag is CustomMenuItem ||
                    (i is MenuFlyoutSeparator sep && sep.Name == "DynamicSeparator") ||
                    (i.Tag as string == "DynamicManager")
                ).ToList();
                foreach (var r in toRemove) menu.Items.Remove(r);

                // 3. Inject Dynamic Managers (L2-9)
                int insertIndex = 1; // After "Run"

                // Primary Manager
                var platform = GamePlatformHelper.DetectPlatform(item.ExePath ?? "");
                var mgrPlatform = !string.IsNullOrEmpty(item.MgrPath) ? GamePlatformHelper.DetectPlatform(item.MgrPath) : null;
                bool isXbox = item.PlatformName == "Xbox";
                bool hasCustomMgr = !string.IsNullOrEmpty(item.MgrPath);

                if (hasCustomMgr || platform != null || isXbox)
                {
                    var mgrItem = new MenuFlyoutItem
                    {
                        Tag = "DynamicManager",
                        Icon = new SymbolIcon(Symbol.Repair)
                    };

                    // Priority: 1. Manual MgrPath matches a platform, 2. Auto-detected ExePath platform, 3. Xbox, 4. Generic Custom
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

                // Custom Items
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
                        RunProcess(ci.Command, ci.IsAdmin);
                    }
                }
            }
            catch (Exception) { }
        }

        private void MenuRunMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item == null) return;

                string? managerPath = item.RuntimeManagerPath;

                // Robustness: Double check Xbox binding for UWP games
                if (string.IsNullOrEmpty(managerPath) && item.PlatformName == "Xbox")
                {
                    managerPath = "xbox://";
                }

                if (!string.IsNullOrEmpty(managerPath))
                {
                    RunProcess(managerPath, item.IsMgrAdmin);
                }
            }
            catch (Exception) { }
        }

        private void MenuLoc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null && !string.IsNullOrEmpty(item.ExePath))
                {
                    string? dir = Path.GetDirectoryName(item.ExePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start("explorer.exe", $"/select,\"{item.ExePath}\"");
                    }
                }
            }
            catch (Exception) { }
        }

        private void MenuDel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    _allItems.Remove(item);
                    _viewItems.Remove(item);
                    SaveData();
                }
            }
            catch (Exception) { }
        }

        private void MenuProp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    OpenPropertyWindow(item);
                }
            }
            catch (Exception) { }
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

                // Initial identification
                // M2: capture ExePath to a local variable before entering Task.Run to
                // avoid a race condition where _currentEditingItem could be set to null
                // on the UI thread (e.g. user closes the panel) while the background
                // thread is still running.
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


                if (!string.IsNullOrEmpty(_currentEditingItem.IconPath) && File.Exists(_currentEditingItem.IconPath))
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            // H1: use LastWriteTime as cache key so unchanged icons can be
                            // reused from the WinUI bitmap cache instead of being re-decoded
                            // on every panel open.
                            var bitmap = new BitmapImage();
                            long cacheKey = new FileInfo(_currentEditingItem!.IconPath!).LastWriteTime.Ticks;
                            var uri = new Uri($"file:///{_currentEditingItem.IconPath.Replace("\\", "/")}?t={cacheKey}");
                            bitmap.UriSource = uri;
                            PropIcon.Source = bitmap;
                        }
                        catch
                        {
                            PropIcon.Source = null;
                        }
                    });
                }
                else
                {
                    PropIcon.Source = null;
                }
            }
            catch (Exception) { }
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
                // L5: only overwrite Platform if the badge shows a non-empty value;
                // an empty badge means detection failed and we should preserve any
                // previously stored Platform (e.g. "Steam") rather than erasing it.
                string? detectedPlatform = PropPlatformBadge.Text?.Trim();
                if (!string.IsNullOrEmpty(detectedPlatform))
                    _currentEditingItem.Platform = detectedPlatform;





                if (string.IsNullOrEmpty(_currentEditingItem.ExePath))
                {
                }
            }
            catch (Exception) { }
        }




        private void RefreshIconDisplay(string iconPath)
        {
            try
            {
                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                {
                    PropIcon.Source = null;
                    return;
                }


                this.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // H1: use file modification time as cache key (consistent with ImagePathConverter).
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
                PropIcon.Source = null;
            }
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
            catch (Exception) { }
        }

        private void ShowPropertyPanel()
        {
            PropertyPanel.Visibility = Visibility.Visible;
            PopulateShortcutMenus();

            var transform = new TranslateTransform { X = 400 };
            PropertyPanel.RenderTransform = transform;

            var storyboard = new Storyboard();
            var panelAnimation = new DoubleAnimation
            {
                From = 400,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(panelAnimation, transform);
            Storyboard.SetTargetProperty(panelAnimation, "X");
            storyboard.Children.Add(panelAnimation);
            storyboard.Begin();

            AppGrid.Padding = new Thickness(20, 20, 420, 20);
        }

        // P1: async void so it returns immediately to the caller, letting the
        // property-panel animation start without waiting for shell enumeration.
        private async void PopulateShortcutMenus()
        {
            try
            {
                // Clear menus IMMEDIATELY (before any await) so that if this method is
                // invoked again while the previous call is still awaiting, the second
                // call's clear runs first and prevents duplicate items from the first call.
                MenuExeStartMenu.Items.Clear();
                MenuExeDesktop.Items.Clear();
                MenuAltStartMenu.Items.Clear();
                MenuAltDesktop.Items.Clear();
                MenuAlongStartMenu.Items.Clear();
                MenuAlongDesktop.Items.Clear();
                MenuMgrStartMenu.Items.Clear();
                MenuMgrDesktop.Items.Clear();

                // Use preloaded data when available; fall back to background load otherwise.
                // H3: consume and null-out the preloaded tasks so subsequent panel openings
                // (after the first) re-scan the sources and pick up newly installed apps.
                List<ShortcutScanner.FileItem> startMenuItems = _preloadedStartMenuTask != null
                    ? await _preloadedStartMenuTask
                    : await Task.Run(() => ShortcutScanner.GetStartMenuItems());
                _preloadedStartMenuTask = null;

                List<ShortcutScanner.FileItem> desktopItems = _preloadedDesktopTask != null
                    ? await _preloadedDesktopTask
                    : await Task.Run(() => ShortcutScanner.GetDesktopItems());
                _preloadedDesktopTask = null;

                // Clear again after await in case another invocation fired between
                // the first clear and here (double-clear is safe and ensures no duplicates).
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

                for (int i = 0; i < 10; i++)
                {
                    int index = i;
                    var flyout = new MenuFlyout();
                    var startMenuSub = new MenuFlyoutSubItem { Text = I18n.T("Menu_StartMenu"), Icon = new FontIcon { Glyph = "\uE700" } };
                    var desktopSub = new MenuFlyoutSubItem { Text = I18n.T("Menu_Desktop"), Icon = new FontIcon { Glyph = "\uE8FC" } };
                    var browseItem = new MenuFlyoutItem { Text = I18n.T("Menu_Browse"), Icon = new FontIcon { Glyph = "\uE8E5" } };

                    browseItem.Click += (s, e) => BtnBrowseCustom_Click(index);

                    PopulateMenuItems(startMenuSub, startMenuItems, _customCommands[i]);
                    PopulateMenuItems(desktopSub, desktopItems, _customCommands[i]);

                    flyout.Items.Add(startMenuSub);
                    flyout.Items.Add(desktopSub);
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    flyout.Items.Add(browseItem);

                    _customBrowses[i].Flyout = flyout;
                }
            }
            catch (Exception) { }
        }

        private void BtnBrowseCustom_Click(int index)
        {
            BrowseFile(_customCommands[index]);
        }

        private void PopulateMenuItems(MenuFlyoutSubItem parent, List<ShortcutScanner.FileItem> items, TextBox targetTextBox)
        {
            // Flat list as requested by user
            foreach (var item in items)
            {
                if (!item.IsFolder)
                {
                    var menuItem = new MenuFlyoutItem { Text = item.Name, Tag = item.FullPath };
                    menuItem.Click += (s, e) => OnShortcutMenuItemClick(item.FullPath, targetTextBox, item.Name);
                    parent.Items.Add(menuItem);
                }
                else if (item.IsFolder && item.Children.Count > 0)
                {
                    // If it is a folder, still flatten its contents into the same menu (optional, but keep it recursive-flattened if needed)
                    // Given ShortcutScanner now returns flat for Start Menu, this recursive part is mostly for Desktop items
                    PopulateMenuItems(parent, item.Children, targetTextBox);
                }
            }
        }

        private async void OnShortcutMenuItemClick(string filePath, TextBox targetTextBox, string? displayName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath)) return;
                bool isStoreApp = filePath.StartsWith("shell:AppsFolder\\");
                if (!isStoreApp && !File.Exists(filePath)) return;


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

                targetTextBox.Text = actualPath;


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
                            iconPath = await IconHelper.GetIconPathAsync(steamExePath, _currentEditingItem.Id);
                        }
                        else
                        {
                        }
                    }
                    else
                    {

                        string iconSource = filePath;
                        bool shouldExtractFromLnk = extractFromLnk;

                        // UWP Priority: Use shell:AppsFolder path for better icon extraction
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

                        iconPath = await IconHelper.GetIconPathAsync(iconSource, _currentEditingItem.Id);
                    }

                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        // M1: removed magic Task.Delay(100); File.Exists above is sufficient
                        // to confirm the icon is on disk before updating the UI.
                        _currentEditingItem.IconPath = null;
                        _currentEditingItem.IconPath = iconPath;
                        RefreshIconDisplay(iconPath);
                    }
                }
            }
            catch (Exception) { }
        }

        private void HidePropertyPanel()
        {

            var transform = PropertyPanel.RenderTransform as TranslateTransform;
            if (transform == null)
            {
                transform = new TranslateTransform { X = 0 };
                PropertyPanel.RenderTransform = transform;
            }


            var storyboard = new Storyboard();


            var panelAnimation = new DoubleAnimation
            {
                From = 0,
                To = 400,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            Storyboard.SetTarget(panelAnimation, transform);
            Storyboard.SetTargetProperty(panelAnimation, "X");
            storyboard.Children.Add(panelAnimation);

            storyboard.Completed += (s, e) =>
            {
                PropertyPanel.Visibility = Visibility.Collapsed;
                PropertyPanel.RenderTransform = null;
                _currentEditingItem = null;
                _isNewItemMode = false;


                AppGrid.Padding = new Thickness(20);
            };

            storyboard.Begin();
        }

        private void BtnCloseProperty_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                HidePropertyPanel();
            }
            catch (Exception) { }
        }

        private async void BtnSaveProperty_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentEditingItem == null) return;


                if (string.IsNullOrWhiteSpace(PropExePath.Text))
                {
                    // R6: give user visual feedback instead of silently discarding the save
                    PropExePath.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                    _ = Task.Delay(2000).ContinueWith(_ =>
                        DispatcherQueue.TryEnqueue(() => PropExePath.ClearValue(TextBox.BorderBrushProperty)),
                        TaskScheduler.Default);
                    PropExePath.Focus(FocusState.Programmatic);
                    return;
                }


                if (_isNewItemMode)
                {
                    var existing = _allItems.FirstOrDefault(x =>
                        !string.IsNullOrEmpty(x.ExePath) &&
                        x.ExePath.Equals(PropExePath.Text, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        // H2: give the user visual feedback instead of silently discarding
                        // the save (mirrors the R6 treatment of an empty ExePath).
                        PropExePath.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
                        _ = Task.Delay(2000).ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => PropExePath.ClearValue(TextBox.BorderBrushProperty)),
                            TaskScheduler.Default);
                        PropExePath.Focus(FocusState.Programmatic);
                        return;
                    }
                }


                SaveToItem();


                if (_isNewItemMode)
                {
                    _allItems.Add(_currentEditingItem);
                    _viewItems.Add(_currentEditingItem);
                }
                else
                {
                }

                SaveData();
                HidePropertyPanel();
            }
            catch (Exception) { }
        }

        private void BtnDeleteProperty_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentEditingItem == null) return;

                _allItems.Remove(_currentEditingItem);
                _viewItems.Remove(_currentEditingItem);
                SaveData();
                HidePropertyPanel();
            }
            catch (Exception) { }
        }

        private async void BtnChangeIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {

                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string filter = "Image Files (*.png;*.ico;*.exe;*.dll;*.lnk)\0*.png;*.ico;*.exe;*.dll;*.lnk\0All Files (*.*)\0*.*\0\0";
                string? filePath = Win32FileDialog.ShowOpenFileDialog(hwnd, "Select Icon File", filter);

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {


                    PropIcon.Source = null;


                    string? newPath = await IconHelper.GetIconPathAsync(filePath, _currentEditingItem!.Id);

                    if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                    {

                        // M1: removed magic Task.Delay(200); confirmed newPath exists via
                        // the File.Exists guard below — no delay needed.
                        _currentEditingItem.IconPath = null;
                        _currentEditingItem.IconPath = newPath;


                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                // H1: use file modification time as cache key.
                                var bitmap = new BitmapImage();
                                long cacheKey = new FileInfo(newPath).LastWriteTime.Ticks;
                                var uri = new Uri($"file:///{newPath.Replace("\\", "/")}?t={cacheKey}");
                                bitmap.UriSource = uri;
                                PropIcon.Source = bitmap;
                            }
                            catch
                            {
                                PropIcon.Source = null;
                            }
                        });
                    }
                    else
                    {
                    }
                }
            }
            catch (Exception) { }
        }




        private async void BrowseFile(Microsoft.UI.Xaml.Controls.TextBox target)
        {
            try
            {
                if (target == null)
                {
                    return;
                }


                IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                string? filePath = Win32FileDialog.ShowOpenFileDialog(hwnd, "Select Executable File");

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
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
                                iconPath = await IconHelper.GetIconPathAsync(steamExePath, _currentEditingItem.Id);
                            }
                            else
                            {
                            }
                        }
                        else
                        {

                            string iconSource = filePath;

                            // UWP Priority: Use shell:AppsFolder path for better icon extraction
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
                            iconPath = await IconHelper.GetIconPathAsync(iconSource, _currentEditingItem.Id);
                        }

                        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        {
                            // M1: removed magic Task.Delay(200); File.Exists already confirms
                            // the icon is persisted to disk.
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
            catch (Exception) { }
        }

        private void BtnBrowseExe_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropExePath); } catch (Exception) { }
        }

        private void BtnBrowseAlt_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropAlternativeLaunchCommand); } catch (Exception) { }
        }

        private void BtnBrowseAlongside_Click(object sender, RoutedEventArgs e)
        {
            try { BrowseFile(PropAlongsideCommand); } catch (Exception) { }
        }

        private void BtnBrowseMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BrowseFile(PropMgrPath);
            }
            catch (Exception) { }
        }


        private void SearchFlyout_Opened(object sender, object e)
        {
            try
            {

                SearchBoxFlyout.Focus(FocusState.Programmatic);
            }
            catch (Exception) { }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (sender == null) return;

                string query = (sender.Text ?? "").ToLower().Trim();

                // L1: replace ItemsSource with a new collection in one shot instead of
                // Clear() + individual Add() calls, which each fire CollectionChanged
                // and trigger incremental UI re-layout on every item.
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
            catch (Exception) { }
        }

        private void EditOrderFlyout_Opening(object sender, object e)
        {
            try
            {

                OrderItemsControl.ItemsSource = null;
                OrderItemsControl.ItemsSource = _allItems;
                _orderItemsControl = OrderItemsControl as ListView;

                // Localize sort flyout text
                PropSaveText.Text = I18n.T("Button_Save");
                PropDeleteText.Text = I18n.T("Button_Delete");

                PropCustomMenuLabel.Text = I18n.T("Property_CustomMenu");
                for (int i = 0; i < 10; i++)
                {
                    _customTitles[i].PlaceholderText = I18n.T("Property_CustomTitlePlaceholder");
                    _customCommands[i].PlaceholderText = I18n.T("Property_CustomCommandPlaceholder");
                    ToolTipService.SetToolTip(_customAdmins[i], I18n.T("Property_RunAsAdmin"));
                }
                SortTitle.Text = I18n.T("Sort_Title");
                SortDescription.Text = I18n.T("Sort_Description");
            }
            catch (Exception) { }
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
            catch (Exception) { }
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            // M4: This handler is intentionally empty — editing is triggered by double-clicking
            // or via the right-click context menu (MenuProp_Click). If a dedicated Edit button
            // is added to the UI, implement the body here.
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.Tag as AppItem;
                MoveItem(item, -1);
            }
            catch (Exception) { }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.Tag as AppItem;
                MoveItem(item, 1);
            }
            catch (Exception) { }
        }

        private void MoveItem(AppItem? item, int offset)
        {
            if (item == null) return;

            int index = _allItems.IndexOf(item);
            int newIndex = index + offset;

            if (newIndex >= 0 && newIndex < _allItems.Count)
            {
                _allItems.Move(index, newIndex);

                RefreshView();
                SaveData();

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
            // L6: assign a stable random Id upfront so the item has a unique identity
            // before ExePath is set. Relying solely on the ExePath setter side-effect
            // leaves a window where Id is empty (if ExePath is set to empty string).
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
                FileName = "https://github.com/EricZhang233",
                UseShellExecute = true
            });
        }

        private void MenuIconSize_Click(object sender, RoutedEventArgs e)
        {
            SizeFlyout.ShowAt(BtnMore);
        }

        private void MenuSort_Click(object sender, RoutedEventArgs e)
        {
            EditOrderFlyout.ShowAt(BtnMore);
        }


        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            UpdateStorageModeUI();


            if (ToggleCloseAfterLaunch != null)
            {
                ToggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
            }
            if (SizeSlider != null)
            {
                SizeSlider.Value = ConfigService.IconSize;
            }

            SettingsFlyout.ShowAt(BtnMore);
        }

        private void MenuInstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                string appName = "EricGameLauncher";
                string description = "Eric Game Launcher";

                // Paths
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcutPath = Path.Combine(desktopPath, $"{appName}.lnk");

                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string startMenuPath = Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs");
                string startMenuShortcutPath = Path.Combine(startMenuPath, $"{appName}.lnk");

                // 1. Remove old shortcuts if they exist
                if (File.Exists(desktopShortcutPath)) File.Delete(desktopShortcutPath);
                if (File.Exists(startMenuShortcutPath)) File.Delete(startMenuShortcutPath);

                // 2. Create new shortcuts
                ShortcutResolver.CreateShortcut(exePath, desktopShortcutPath, description);

                if (!Directory.Exists(startMenuPath)) Directory.CreateDirectory(startMenuPath);
                ShortcutResolver.CreateShortcut(exePath, startMenuShortcutPath, description);
            }
            catch (Exception) { }
        }

        private void MenuUninstall_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string appName = "EricGameLauncher";

                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcutPath = Path.Combine(desktopPath, $"{appName}.lnk");

                string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string startMenuShortcutPath = Path.Combine(appDataRoaming, @"Microsoft\Windows\Start Menu\Programs", $"{appName}.lnk");

                if (File.Exists(desktopShortcutPath)) File.Delete(desktopShortcutPath);
                if (File.Exists(startMenuShortcutPath)) File.Delete(startMenuShortcutPath);
            }
            catch (Exception) { }
        }


        private void ToggleCloseAfterLaunch_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _toggleCloseAfterLaunch = toggle;

                if (ConfigService.CloseAfterLaunch != toggle.IsOn)
                {
                    ConfigService.CloseAfterLaunch = toggle.IsOn;
                    ConfigService.SaveConfig();
                }
            }
        }

        private void SizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider)
            {
                _sizeSlider = slider;
                IconSize = slider.Value;
                ConfigService.IconSize = slider.Value;
                ConfigService.SaveConfig();


                UpdateGridItemSizes(slider.Value);
            }
        }

        private void UpdateGridItemSizes(double size)
        {

            if (AppGrid == null || AppGrid.Items == null || AppGrid.Items.Count == 0)
                return;

            foreach (var item in AppGrid.Items)
            {
                var container = AppGrid.ContainerFromItem(item);
                if (container is GridViewItem gvi)
                {
                    ApplySizeToContainer(gvi, size);
                }
            }
        }

        private void AppGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue) return;

            // Apply current IconSize to this container as it gets realized
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

        private async void BtnSwitchStorageMode_Click(object sender, RoutedEventArgs e)
        {

            bool switchToSystemMode = !ConfigService.IsSystemMode;

            await ConfigService.SwitchStorageModeAsync(switchToSystemMode);


            UpdateStorageModeUI();


            await LoadData();
        }




        private void UpdateStorageModeUI()
        {
            if (ConfigService.IsSystemMode)
            {
                StorageModeText.Text = I18n.T("Settings_SystemMode");
                SwitchStorageModeText.Text = I18n.T("Settings_SwitchToPortable");
            }
            else
            {
                StorageModeText.Text = I18n.T("Settings_PortableMode");
                SwitchStorageModeText.Text = I18n.T("Settings_SwitchToSystem");
            }


            StoragePathText.Text = ConfigService.CurrentDataPath;
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
                FileName = folder,
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




        // L4: FindChildByName performs a linear recursive VisualTree walk on every call.
        // For the current item sizes (≤ a few hundred items and shallow sub-trees inside
        // each GridViewItem template) the cost is negligible. If the collection ever grows
        // large, consider caching results per container or shifting to x:Name code-behind
        // references for the fixed template elements (IconGrid, TitleText, etc.).
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
                ToolTipService.SetToolTip(SearchButton, I18n.T("TitleBar_Search"));
                SearchBoxFlyout.PlaceholderText = I18n.T("TitleBar_SearchPlaceholder");
                ToolTipService.SetToolTip(BtnMore, I18n.T("TitleBar_More"));
                MenuIconSizeItem.Text = I18n.T("Menu_IconSize");
                MenuAddItem.Text = I18n.T("Menu_Add");
                MenuSortItem.Text = I18n.T("Menu_Sort");
                MenuSettingsItem.Text = I18n.T("Menu_Settings");
                MenuCheckUpdateItem.Text = I18n.T("Menu_CheckUpdate");
                MenuSystemIntegrationItem.Text = I18n.T("Menu_SystemIntegration");
                MenuInstallItem.Text = I18n.T("Menu_Install");
                MenuUninstallItem.Text = I18n.T("Menu_Uninstall");
                SizeFlyoutTitle.Text = I18n.T("Menu_IconSize");
                SortTitle.Text = I18n.T("Sort_Title");
                SortDescription.Text = I18n.T("Sort_Description");
                SettingsTitle.Text = I18n.T("Settings_Title");
                SettingsGeneralLabel.Text = I18n.T("Settings_General");
                ToggleCloseAfterLaunch.Header = I18n.T("Settings_CloseAfterLaunch");
                SettingsDataLocationLabel.Text = I18n.T("Settings_DataLocation");
                StorageModeText.Text = ConfigService.IsSystemMode
                    ? I18n.T("Settings_SystemMode")
                    : I18n.T("Settings_PortableMode");
                SwitchStorageModeText.Text = ConfigService.IsSystemMode
                    ? I18n.T("Settings_SwitchToPortable")
                    : I18n.T("Settings_SwitchToSystem");
                ToolTipService.SetToolTip(BtnSwitchStorageMode.Parent as FrameworkElement ?? BtnSwitchStorageMode, I18n.T("Settings_OpenConfigFolder"));
                SettingsMigrateNote.Text = I18n.T("Settings_MigrateNote");
                var languages = I18n.GetAvailableLanguages();
                LanguageComboBox.SelectionChanged -= LanguageComboBox_SelectionChanged;
                LanguageComboBox.Items.Clear();
                int selectedIndex = 0;
                for (int i = 0; i < languages.Count; i++)
                {
                    LanguageComboBox.Items.Add(I18n.GetDisplayName(languages[i]));
                    if (languages[i] == I18n.CurrentLanguage)
                        selectedIndex = i;
                }
                LanguageComboBox.Tag = languages;
                LanguageComboBox.SelectedIndex = selectedIndex;
                LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

                try
                {
                    // Remove erroneous tooltip from migration button
                    ToolTipService.SetToolTip(BtnSwitchStorageMode, null);

                    // Set correct tooltip for folder button
                    if (BtnOpenConfigFolder != null)
                        ToolTipService.SetToolTip(BtnOpenConfigFolder, I18n.T("Settings_OpenConfigFolder"));
                }
                catch (Exception) { }

                PropTitleLabel.Text = I18n.T("Property_Title");
                try
                {
                    var closeBtn = PropTitleLabel.Parent is Grid g ? g.Children.OfType<Button>().FirstOrDefault() : null;
                    if (closeBtn != null)
                        ToolTipService.SetToolTip(closeBtn, I18n.T("Property_Close"));
                }
                catch (Exception) { }

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
                catch (Exception) { }

                try
                {
                    var exeDropDown = PropExePath?.Parent is Grid eg ? eg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (exeDropDown != null)
                        ToolTipService.SetToolTip(exeDropDown, I18n.T("Property_SelectFile"));

                    var mgrDropDown = PropMgrPath?.Parent is Grid mg ? mg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (mgrDropDown != null)
                        ToolTipService.SetToolTip(mgrDropDown, I18n.T("Property_SelectFile"));
                }
                catch (Exception) { }

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
                catch (Exception) { }

                EmptyStateText.Text = I18n.T("Empty_Description");

                // Custom Menu Localization
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
            }
            catch (Exception)
            {
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
            // M5: Custom menu slots use a progressive disclosure pattern.
            // Slot N+1 is shown only when slot N has a non-empty Command.
            // Slots must therefore be filled sequentially — clearing a middle
            // slot's Command will hide all subsequent slots (data is preserved
            // internally, just not displayed until restored).
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
                    // Show if previous one has a command typed
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
        private async Task StartUpdateFlowAsync(UpdateService.ReleaseInfo release)
        {
            try
            {
                string downloadUrl = release.assets.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.browser_download_url ?? "";
                if (string.IsNullOrEmpty(downloadUrl)) return;

                // 1. 准备原生 Markdown 控件 (彻底摆脱 WebView2 缓存文件夹)
                var markdownText = new CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock
                {
                    Text = $"# {release.name}\n\n{release.body}",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                };

                // 设置原生样式以适配应用主题
                markdownText.Header1FontSize = 22;
                markdownText.Header2FontSize = 18;
                markdownText.Header1FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                markdownText.Header2FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                markdownText.ParagraphMargin = new Thickness(0, 5, 0, 10);

                var scrollViewer = new ScrollViewer
                {
                    Height = 400,
                    Content = markdownText,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(10)
                };

                ContentDialog confirmDialog = new ContentDialog
                {
                    Title = I18n.T("Update_DialogTitle"),
                    Content = scrollViewer,
                    PrimaryButtonText = I18n.T("Update_DialogConfirm"),
                    CloseButtonText = I18n.T("Update_DialogCancel"),
                    XamlRoot = this.Content.XamlRoot
                };

                if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

                // 2. 启动外部更新器 (由更新器独立负责下载、进度展示与覆盖)
                UpdateService.StartUpdater(downloadUrl);
            }
            catch { }
        }
        private async void VersionText_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (HasUpdate && _pendingUpdate != null)
            {
                await StartUpdateFlowAsync(_pendingUpdate);
            }
        }
    }
}

