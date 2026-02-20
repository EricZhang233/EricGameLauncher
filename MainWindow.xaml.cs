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
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

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

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            this.InitializeComponent();

            
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
                    catch (Exception ex)
                    {
                        Logger.Log(ex);
                    }
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
            catch (Exception ex) { Logger.Log(ex); }

            
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
            
            _ = LoadData();
        }

        private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
        {
            
            if (args.DidSizeChange || args.DidPositionChange)
            {
                SaveWindowState();
            }
        }

        private void RestoreWindowState()
        {
            try
            {
                var (x, y, width, height) = ConfigService.GetWindowBounds();
                
                
                if (width > 0 && height > 0)
                {
                    this.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(width, height));
                }
                else
                {
                    this.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(660, 420));
                }
                
                
                if (x >= 0 && y >= 0)
                {
                    var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                        this.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                    
                    
                    if (x < displayArea.WorkArea.Width - 100 && y < displayArea.WorkArea.Height - 100)
                    {
                        this.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                this.AppWindow.ResizeClient(new Windows.Graphics.SizeInt32(660, 420));
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            
            
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void SaveWindowState()
        {
            try
            {
                
                var presenter = this.AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                if (presenter != null && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
                {
                    return;
                }
                
                var size = this.AppWindow.ClientSize;
                var position = this.AppWindow.Position;
                
                ConfigService.SetWindowBounds(position.X, position.Y, size.Width, size.Height);
                ConfigService.SaveConfig();
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async Task LoadData()
        {
            try
            {
                
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
                            catch (Exception ex) { Logger.Log(ex); }
                        }));
                    }
                }

                if (anyRebuildNeeded)
                {

                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            
                            await Task.Delay(200);
                            
                            DispatcherQueue.TryEnqueue(() => 
                            {
                                ConfigService.SaveItems(_allItems.ToList());
                                RefreshView();
                            });
                        }
                        catch (Exception ex) { Logger.Log(ex); }
                    });
                }
                
                
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void SaveData()
        {
            try
            {
                ConfigService.SaveItems(_allItems.ToList());
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        
        
        
        private void LaunchItem(AppItem item)
        {
            try
            {
                
                if (item.UseAlternativeLaunch && !string.IsNullOrEmpty(item.AlternativeLaunchCommand))
                {
                    
                    RunProcess(item.AlternativeLaunchCommand, false);
                }
                else if (!string.IsNullOrEmpty(item.ExePath))
                {
                    
                    RunProcess(item.ExePath, item.IsAdmin);
                    
                    
                    if (item.RunAlongside && !string.IsNullOrEmpty(item.AlongsideCommand))
                    {
                        RunProcess(item.AlongsideCommand, false);
                    }
                }
            }
            catch (Exception ex) { Logger.Log(ex); }
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

                
                if (path.Contains("://"))
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
            catch (Exception ex) { Logger.Log(ex); }
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
                return (sender as FrameworkElement)?.Tag as AppItem;
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void MenuRunMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var item = GetTag(sender);
                if (item != null)
                {
                    
                    string? managerPath = item.RuntimeManagerPath;
                    if (!string.IsNullOrEmpty(managerPath))
                    {
                        RunProcess(managerPath, item.IsMgrAdmin);
                        
                        
                        if (string.IsNullOrEmpty(item.MgrPath) && item.IsPlatformUrl)
                        {
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
                
                
                PropUseAlternativeLaunch.IsChecked = _currentEditingItem.UseAlternativeLaunch;
                PropAlternativeLaunchCommand.Text = _currentEditingItem.AlternativeLaunchCommand ?? "";
                PropRunAlongside.IsChecked = _currentEditingItem.RunAlongside;
                PropAlongsideCommand.Text = _currentEditingItem.AlongsideCommand ?? "";

                
                if (!string.IsNullOrEmpty(_currentEditingItem.IconPath) && File.Exists(_currentEditingItem.IconPath))
                {
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            var uri = new Uri($"file:///{_currentEditingItem.IconPath.Replace("\\", "/")}?t={DateTime.Now.Ticks}");
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
            catch (Exception ex) { Logger.Log(ex); }
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
                _currentEditingItem.MgrPath = PropMgrPath.Text?.Trim() ?? "";
                _currentEditingItem.IsMgrAdmin = PropIsMgrAdmin.IsChecked ?? false;
                
                
                _currentEditingItem.UseAlternativeLaunch = PropUseAlternativeLaunch.IsChecked ?? false;
                _currentEditingItem.AlternativeLaunchCommand = PropAlternativeLaunchCommand.Text?.Trim() ?? "";
                _currentEditingItem.RunAlongside = PropRunAlongside.IsChecked ?? false;
                _currentEditingItem.AlongsideCommand = PropAlongsideCommand.Text?.Trim() ?? "";

                
                

                
                if (string.IsNullOrEmpty(_currentEditingItem.ExePath))
                {
                }
            }
            catch (Exception ex) { Logger.Log(ex); }
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
                        var bitmap = new BitmapImage();
                        var uri = new Uri($"file:///{iconPath.Replace("\\", "/")}?t={DateTime.Now.Ticks}");
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
            catch (Exception ex) { Logger.Log(ex); }
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

        private void PopulateShortcutMenus()
        {
            try
            {
                
                MenuExeStartMenu.Items.Clear();
                MenuExeDesktop.Items.Clear();
                MenuMgrStartMenu.Items.Clear();
                MenuMgrDesktop.Items.Clear();

                
                var startMenuItems = ShortcutScanner.GetStartMenuItems();
                var desktopItems = ShortcutScanner.GetDesktopItems();

                
                PopulateMenuItems(MenuExeStartMenu, startMenuItems, PropExePath);
                PopulateMenuItems(MenuMgrStartMenu, startMenuItems, PropMgrPath);

                
                PopulateMenuItems(MenuExeDesktop, desktopItems, PropExePath);
                PopulateMenuItems(MenuMgrDesktop, desktopItems, PropMgrPath);
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void PopulateMenuItems(MenuFlyoutSubItem parent, List<ShortcutScanner.FileItem> items, TextBox targetTextBox)
        {
            foreach (var item in items)
            {
                if (item.IsFolder && item.Children.Count > 0)
                {
                    
                    var subMenu = new MenuFlyoutSubItem { Text = item.Name };
                    PopulateMenuItems(subMenu, item.Children, targetTextBox);
                    parent.Items.Add(subMenu);
                }
                else if (!item.IsFolder)
                {
                    
                    var menuItem = new MenuFlyoutItem { Text = item.Name, Tag = item.FullPath };
                    menuItem.Click += (s, e) => OnShortcutMenuItemClick(item.FullPath, targetTextBox);
                    parent.Items.Add(menuItem);
                }
            }
        }

        private async void OnShortcutMenuItemClick(string filePath, TextBox targetTextBox)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return;

                
                string actualPath = filePath;
                ShortcutInfo? shortcutInfo = null;
                bool isUrlProtocol = false;
                bool extractFromLnk = false;

                if (filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    shortcutInfo = ShortcutResolver.GetShortcutInfo(filePath);
                    if (shortcutInfo != null)
                    {
                        if (shortcutInfo.IsUrl && !string.IsNullOrEmpty(shortcutInfo.ActualUrl))
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
                    if (string.IsNullOrEmpty(PropTitle.Text))
                    {
                        PropTitle.Text = Path.GetFileNameWithoutExtension(filePath);
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
                        
                        
                        if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath))
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
                    
                    if (!string.IsNullOrEmpty(iconPath))
                    {
                        _currentEditingItem.IconPath = null;
                        _currentEditingItem.IconPath = iconPath;
                        await Task.Delay(100);
                        RefreshIconDisplay(iconPath);
                    }
                }
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private async void BtnSaveProperty_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentEditingItem == null) return;

                
                if (string.IsNullOrWhiteSpace(PropExePath.Text))
                {
                    
                    return;
                }

                
                if (_isNewItemMode)
                {
                    var existing = _allItems.FirstOrDefault(x => 
                        !string.IsNullOrEmpty(x.ExePath) && 
                        x.ExePath.Equals(PropExePath.Text, StringComparison.OrdinalIgnoreCase));
                    
                    if (existing != null)
                    {
                        
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
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
                        
                        _currentEditingItem.IconPath = null;
                        _currentEditingItem.IconPath = newPath;

                        
                        await Task.Delay(200);

                        
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                var bitmap = new BitmapImage();
                                var uri = new Uri($"file:///{newPath.Replace("\\", "/")}?t={DateTime.Now.Ticks}");
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
            catch (Exception ex) { Logger.Log(ex); }
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
                            if (shortcutInfo.IsUrl)
                            {
                                actualPath = shortcutInfo.ActualUrl ?? shortcutInfo.TargetPath ?? filePath;
                                isUrlProtocol = true;
                            }
                            else if (!string.IsNullOrEmpty(shortcutInfo.TargetPath))
                            {
                                actualPath = shortcutInfo.TargetPath;
                            }
                        }
                        else
                        {
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
                        if (string.IsNullOrEmpty(PropTitle.Text))
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
                            
                            
                            if (shortcutInfo != null && !string.IsNullOrEmpty(shortcutInfo.IconPath) && File.Exists(shortcutInfo.IconPath))
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
                            _currentEditingItem.IconPath = null;
                            _currentEditingItem.IconPath = iconPath;
                            await Task.Delay(200);
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void BtnBrowseExe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BrowseFile(PropExePath);
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void BtnBrowseMgr_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BrowseFile(PropMgrPath);
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        
        private void SearchFlyout_Opened(object sender, object e)
        {
            try
            {
                
                SearchBoxFlyout.Focus(FocusState.Programmatic);
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            try
            {
                if (sender == null) return;

                string query = (sender.Text ?? "").ToLower().Trim();
                _viewItems.Clear();

                if (string.IsNullOrEmpty(query))
                {
                    
                    foreach (var item in _allItems)
                        _viewItems.Add(item);
                }
                else
                {
                    
                    foreach (var item in _allItems)
                    {
                        bool matched = false;

                        
                        if (!string.IsNullOrEmpty(item.Title) && item.Title.ToLower().Contains(query))
                        {
                            matched = true;
                        }
                        
                        else if (!string.IsNullOrEmpty(item.ExePath) && item.ExePath.ToLower().Contains(query))
                        {
                            matched = true;
                        }
                        
                        else if (!string.IsNullOrEmpty(item.TitlePinyin) && item.TitlePinyin.Contains(query))
                        {
                            matched = true;
                        }
                        
                        else if (!string.IsNullOrEmpty(item.TitlePinyinInitial) && item.TitlePinyinInitial.Contains(query))
                        {
                            matched = true;
                        }
                        
                        else if (!string.IsNullOrEmpty(item.TitleEnglishInitial) && item.TitleEnglishInitial.Contains(query))
                        {
                            matched = true;
                        }

                        if (matched)
                        {
                            _viewItems.Add(item);
                        }
                    }
                }

                IsFiltered = !string.IsNullOrEmpty(query);
                UpdateEmptyState();
            }
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void EditOrderFlyout_Opening(object sender, object e)
        {
            try
            {
                
                OrderItemsControl.ItemsSource = null;
                OrderItemsControl.ItemsSource = _allItems;
                _orderItemsControl = OrderItemsControl as ListView;

                // Localize sort flyout text
                SortTitle.Text = I18n.T("Sort_Title");
                SortDescription.Text = I18n.T("Sort_Description");
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
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
            catch (Exception ex) { Logger.Log(ex); }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.Tag as AppItem;
                MoveItem(item, 1);
            }
            catch (Exception ex) { Logger.Log(ex); }
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
            
            var newItem = new AppItem
            {
                Title = string.Empty,
                ExePath = string.Empty
            };
            
            _currentEditingItem = newItem;
            _isNewItemMode = true; 
            
            
            PropTitle.Text = string.Empty;
            PropExePath.Text = string.Empty;
            PropIsAdmin.IsChecked = false;
            PropMgrPath.Text = string.Empty;
            PropIsMgrAdmin.IsChecked = false;
            PropIcon.Source = null;
            
            
            PropUseAlternativeLaunch.IsChecked = false;
            PropAlternativeLaunchCommand.Text = string.Empty;
            PropRunAlongside.IsChecked = false;
            PropAlongsideCommand.Text = string.Empty;
            
            
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

        
        
        
        private DependencyObject? FindChildByName(DependencyObject parent, string name)
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is FrameworkElement element && element.Name == name)
                {
                    return child;
                }

                var result = FindChildByName(child, name);
                if (result != null)
                {
                    return result;
                }
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
                    var grid = BtnSwitchStorageMode.Parent as Grid;
                    if (grid != null && grid.Children.Count > 4)
                    {
                        var openFolderBtn = grid.Children[4] as Button;
                        if (openFolderBtn != null)
                            ToolTipService.SetToolTip(openFolderBtn, I18n.T("Settings_OpenConfigFolder"));
                    }
                }
                catch (Exception ex) { Logger.Log(ex); }

                PropTitleLabel.Text = I18n.T("Property_Title");
                try
                {
                    var closeBtn = PropTitleLabel.Parent is Grid g ? g.Children.OfType<Button>().FirstOrDefault() : null;
                    if (closeBtn != null)
                        ToolTipService.SetToolTip(closeBtn, I18n.T("Property_Close"));
                }
                catch (Exception ex) { Logger.Log(ex); }

                PropDisplayNameLabel.Text = I18n.T("Property_DisplayName");
                PropMainExePathLabel.Text = I18n.T("Property_MainExePath");
                MenuExeStartMenu.Text = I18n.T("Source_StartMenu");
                MenuExeDesktop.Text = I18n.T("Source_Desktop");
                MenuExeBrowse.Text = I18n.T("Property_BrowseFile");
                MenuMgrStartMenu.Text = I18n.T("Source_StartMenu");
                MenuMgrDesktop.Text = I18n.T("Source_Desktop");
                MenuMgrBrowse.Text = I18n.T("Property_BrowseFile");
                PropUseAlternativeLaunch.Content = I18n.T("Property_SubstituteExe");
                PropRunAlongside.Content = I18n.T("Property_RunAtLaunch");
                PropManagerPathLabel.Text = I18n.T("Property_ManagerPath");
                PropOptionalLabel.Text = I18n.T("Property_Optional");
                PropRunAsAdminLabel.Text = I18n.T("Property_RunAsAdmin");
                PropIsAdmin.Content = I18n.T("Property_MainExe");
                PropIsMgrAdmin.Content = I18n.T("Property_Manager");
                try
                {
                    var iconGrid = PropIcon?.Parent as Border;
                    var changeIconBtn = iconGrid?.Parent is Grid ig ? ig.Children.OfType<Button>().FirstOrDefault(b => b != null) : null;
                    if (changeIconBtn != null)
                        ToolTipService.SetToolTip(changeIconBtn, I18n.T("Property_ChangeIcon"));
                }
                catch (Exception ex) { Logger.Log(ex); }

                try
                {
                    var exeDropDown = PropExePath?.Parent is Grid eg ? eg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (exeDropDown != null)
                        ToolTipService.SetToolTip(exeDropDown, I18n.T("Property_SelectFile"));

                    var mgrDropDown = PropMgrPath?.Parent is Grid mg ? mg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
                    if (mgrDropDown != null)
                        ToolTipService.SetToolTip(mgrDropDown, I18n.T("Property_SelectFile"));
                }
                catch (Exception ex) { Logger.Log(ex); }

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
                catch (Exception ex) { Logger.Log(ex); }

                EmptyStateText.Text = I18n.T("Empty_Description");
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        private void ContextMenu_Opening(object sender, object e)
        {
            if (sender is MenuFlyout flyout)
            {
                foreach (var item in flyout.Items)
                {
                    if (item is MenuFlyoutItem menuItem)
                    {
                        var text = menuItem.Text;
                            // We identify items by their Icon property
                            if (menuItem.Icon is SymbolIcon si)
                            {
                                menuItem.Text = si.Symbol switch
                                {
                                    Symbol.Play => I18n.T("Menu_Run"),
                                    Symbol.Folder => I18n.T("Menu_OpenFileLocation"),
                                    Symbol.Edit => I18n.T("Menu_Properties"),
                                    Symbol.Delete => I18n.T("Menu_Delete"),
                                    _ => text
                                };
                            }
                            else if (menuItem.Visibility == Microsoft.UI.Xaml.Visibility.Visible ||
                                     menuItem.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed)
                            {
                                var idx = flyout.Items.IndexOf(item);
                                if (idx == 1)
                                {
                                    menuItem.Text = I18n.T("Menu_RunManager");
                                }
                            }
                    }
                }
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
    }
}

