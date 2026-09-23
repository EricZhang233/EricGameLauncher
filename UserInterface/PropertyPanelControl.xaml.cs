using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EricGameLauncher;

public sealed partial class PropertyPanelControl : UserControl
{
    private AppItem? _editingItem;
    private bool _isNewItemMode = false;
    private CancellationTokenSource? _platformDetectCts;

    private readonly StackPanel[] _customSections;
    private readonly TextBox[] _customTitles;
    private readonly TextBox[] _customCommands;
    private readonly CheckBox[] _customAdmins;
    private readonly DropDownButton[] _customBrowses;
    private readonly TextBlock[] _customAdminLabels;
    private readonly TextBlock[] _customSlotLabels;

    public event Action? SaveRequested;
    public event Action? DeleteRequested;
    public event Action? MenusRequested;

    public IntPtr OwnerWindowHandle { get; set; } = IntPtr.Zero;

    public AppItem? EditingItem => _editingItem;
    public bool IsNewItemMode => _isNewItemMode;
    public string ExePathText => PropExePath.Text?.Trim() ?? "";

    public PropertyPanelControl()
    {
        InitializeComponent();

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
                _platformDetectCts?.Cancel();
                _platformDetectCts = new CancellationTokenSource();
                var token = _platformDetectCts.Token;
                string path = PropExePath.Text?.Trim() ?? "";
                try { await Task.Delay(300, token); } catch { return; }
                var platform = await GamePlatformHelper.DetectPlatformAsync(path);

                if (token.IsCancellationRequested) return;
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

        LogService.Write("UI", "Property panel control constructed");
    }

    public void ApplyLocalization()
    {
        PropTitleLabel.Text = Text.T("Property_Title");
        try
        {
            var closeBtn = PropTitleLabel.Parent is Grid g ? g.Children.OfType<Button>().FirstOrDefault() : null;
            if (closeBtn != null)
                ToolTipService.SetToolTip(closeBtn, Text.T("Property_Close"));
        }
        catch (Exception ex) { LogService.Write("UI", "ApplyLocalization failed", ex); }

        PropDisplayNameLabel.Text = Text.T("Property_DisplayName");
        PropMainExePathLabel.Text = Text.T("Property_MainExePath");
        MenuExeStartMenu.Text = Text.T("Source_StartMenu");
        MenuExeDesktop.Text = Text.T("Source_Desktop");
        MenuExeBrowse.Text = Text.T("Property_BrowseFile");

        MenuAltStartMenu.Text = Text.T("Source_StartMenu");
        MenuAltDesktop.Text = Text.T("Source_Desktop");
        MenuAltBrowse.Text = Text.T("Property_BrowseFile");

        MenuAlongStartMenu.Text = Text.T("Source_StartMenu");
        MenuAlongDesktop.Text = Text.T("Source_Desktop");
        MenuAlongBrowse.Text = Text.T("Property_BrowseFile");

        MenuMgrStartMenu.Text = Text.T("Source_StartMenu");
        MenuMgrDesktop.Text = Text.T("Source_Desktop");
        MenuMgrBrowse.Text = Text.T("Property_BrowseFile");
        PropSubstituteExeLabel.Text = Text.T("Property_SubstituteExe");
        PropRunAtLaunchLabel.Text = Text.T("Property_RunAtLaunch");
        PropManagerPathLabel.Text = Text.T("Property_ManagerPath");
        PropOptionalLabel.Text = Text.T("Property_Optional");

        string adminText = Text.T("Property_Admin");
        PropAdminLabel1.Text = adminText;
        PropAdminLabel2.Text = adminText;
        PropAdminLabel3.Text = adminText;
        PropAdminLabel4.Text = adminText;
        try
        {
            var iconGrid = PropIcon?.Parent as Border;
            var changeIconBtn = iconGrid?.Parent is Grid ig ? ig.Children.OfType<Button>().FirstOrDefault(b => b != null) : null;
            if (changeIconBtn != null)
                ToolTipService.SetToolTip(changeIconBtn, Text.T("Property_ChangeIcon"));
        }
        catch (Exception ex) { LogService.Write("UI", "ApplyLocalization failed", ex); }

        try
        {
            var exeDropDown = PropExePath?.Parent is Grid eg ? eg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
            if (exeDropDown != null)
                ToolTipService.SetToolTip(exeDropDown, Text.T("Property_SelectFile"));

            var mgrDropDown = PropMgrPath?.Parent is Grid mg ? mg.Children.OfType<DropDownButton>().FirstOrDefault() : null;
            if (mgrDropDown != null)
                ToolTipService.SetToolTip(mgrDropDown, Text.T("Property_SelectFile"));
        }
        catch (Exception ex) { LogService.Write("UI", "ApplyLocalization failed", ex); }

        PropDeleteText.Text = Text.T("Menu_Delete");
        ToolTipService.SetToolTip(PropBtnDelete, Text.T("Property_DeleteItem"));
        PropSaveText.Text = Text.T("Property_Save");
        try
        {
            var saveBtnParent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(PropSaveText);
            var saveBtn = saveBtnParent != null ? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(saveBtnParent) as Button : null;
            if (saveBtn != null)
                ToolTipService.SetToolTip(saveBtn, Text.T("Property_Save"));
        }
        catch (Exception ex) { LogService.Write("UI", "ApplyLocalization failed", ex); }

        PropCustomMenuLabel.Text = Text.T("Property_CustomMenu");
        string titlePlaceholder = Text.T("Property_CustomTitlePlaceholder");
        string cmdPlaceholder = Text.T("Property_CustomCommandPlaceholder");
        string selectTooltip = Text.T("Property_SelectFile");
        string adminTooltip = Text.T("Property_Admin");

        for (int i = 0; i < 10; i++)
        {
            _customTitles[i].PlaceholderText = titlePlaceholder;
            _customCommands[i].PlaceholderText = cmdPlaceholder;
            ToolTipService.SetToolTip(_customBrowses[i], selectTooltip);
            ToolTipService.SetToolTip(_customAdmins[i], adminTooltip);
            _customAdminLabels[i].Text = adminTooltip;
        }
        UpdateCustomVisibility();
    }

    public void BeginEdit(AppItem item, bool isNew)
    {
        try
        {
            _editingItem = item;
            _isNewItemMode = isNew;

            LoadUI();

            PropBtnDelete.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;

            ShowPanel();
            LogService.Write("UI", $"Property panel begin edit id={item.Id} isNew={isNew}");
        }
        catch (Exception ex) { LogService.Write("UI", "BeginEdit failed", ex); }
    }

    public void ShowExePathError()
    {
        try
        {
            PropExePath.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 196, 43, 28));
            _ = Task.Delay(2000).ContinueWith(_ =>
                DispatcherQueue.TryEnqueue(() => PropExePath.ClearValue(TextBox.BorderBrushProperty)),
                TaskScheduler.Default);
            PropExePath.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { LogService.Write("UI", "ShowExePathError failed", ex); }
    }

    public void ApplyToItem()
    {
        try
        {
            if (_editingItem == null) return;

            _editingItem.Title = PropTitle.Text?.Trim() ?? "";

            string newExePath = PropExePath.Text?.Trim() ?? "";
            if (_editingItem.ExePath != newExePath)
            {
                _editingItem.ExePath = newExePath;
            }

            _editingItem.IsAdmin = PropIsAdmin.IsChecked ?? false;
            _editingItem.IsAltAdmin = PropIsAltAdmin.IsChecked ?? false;
            _editingItem.IsAlongsideAdmin = PropIsAlongsideAdmin.IsChecked ?? false;
            _editingItem.IsMgrAdmin = PropIsMgrAdmin.IsChecked ?? false;
            _editingItem.MgrPath = PropMgrPath.Text?.Trim() ?? "";

            _editingItem.UseAlternativeLaunch = PropUseAlternativeLaunch.IsChecked ?? false;
            _editingItem.AlternativeLaunchCommand = PropAlternativeLaunchCommand.Text?.Trim() ?? "";
            _editingItem.RunAlongside = PropRunAlongside.IsChecked ?? false;
            _editingItem.AlongsideCommand = PropAlongsideCommand.Text?.Trim() ?? "";

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
            _editingItem.SetCustomMenuItems(customItems);

            string? detectedPlatform = PropPlatformBadge.Text?.Trim();
            if (!string.IsNullOrEmpty(detectedPlatform))
                _editingItem.Platform = detectedPlatform;
        }
        catch (Exception ex) { LogService.Write("UI", "ApplyToItem failed", ex); }
    }

    public void ClosePanel()
    {
        HidePanel();
    }

    public void PopulateShortcutMenus(List<ShortcutScanner.FileItem>? startMenuItems, List<ShortcutScanner.FileItem>? desktopItems)
    {
        using (LogService.StartOperation("UI", "PopulateShortcutMenus"))
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

                PopulateMenuItems(MenuExeStartMenu, startMenuItems, PropExePath);
                PopulateMenuItems(MenuExeDesktop, desktopItems, PropExePath);
                PopulateMenuItems(MenuAltStartMenu, startMenuItems, PropAlternativeLaunchCommand);
                PopulateMenuItems(MenuAltDesktop, desktopItems, PropAlternativeLaunchCommand);
                PopulateMenuItems(MenuAlongStartMenu, startMenuItems, PropAlongsideCommand);
                PopulateMenuItems(MenuAlongDesktop, desktopItems, PropAlongsideCommand);
                PopulateMenuItems(MenuMgrStartMenu, startMenuItems, PropMgrPath);
                PopulateMenuItems(MenuMgrDesktop, desktopItems, PropMgrPath);

                LogService.Write("UI", "PopulateShortcutMenus populated standard menus and custom browse flyouts");

                for (int i = 0; i < 10; i++)
                {
                    int index = i;
                    var flyout = new MenuFlyout();
                    var startMenuSub = new MenuFlyoutSubItem { Text = Text.T("Source_StartMenu"), Icon = new FontIcon { Glyph = "\uE700" } };
                    var desktopSub = new MenuFlyoutSubItem { Text = Text.T("Source_Desktop"), Icon = new FontIcon { Glyph = "\uE8FC" } };
                    var browseItem = new MenuFlyoutItem { Text = Text.T("Property_BrowseFile"), Icon = new FontIcon { Glyph = "\uE8E5" } };

                    browseItem.Click += (s, e) => BtnBrowseCustom_Click(index);

                    PopulateMenuItems(startMenuSub, startMenuItems, _customCommands[i]);
                    PopulateMenuItems(desktopSub, desktopItems, _customCommands[i]);

                    LogService.Write("UI", $"PopulateShortcutMenus custom browse index={index} startCount={startMenuItems?.Count ?? 0} desktopCount={desktopItems?.Count ?? 0}");

                    flyout.Items.Add(startMenuSub);
                    flyout.Items.Add(desktopSub);
                    flyout.Items.Add(new MenuFlyoutSeparator());
                    flyout.Items.Add(browseItem);

                    _customBrowses[i].Flyout = flyout;
                }
            }
            catch (Exception ex) { LogService.Write("UI", "PopulateShortcutMenus failed", ex); }
        }
    }

    private void LoadUI()
    {
        try
        {
            PropTitle.Text = _editingItem!.Title ?? "";
            PropExePath.Text = _editingItem.ExePath ?? "";
            PropIsAdmin.IsChecked = _editingItem.IsAdmin;
            PropMgrPath.Text = _editingItem.MgrPath ?? "";
            PropIsMgrAdmin.IsChecked = _editingItem.IsMgrAdmin;
            PropDisplayNameLabel.Text = Text.T("Property_DisplayName");

            string? exePathSnapshot = _editingItem.ExePath;
            _ = Task.Run(async () =>
            {
                var platform = await GamePlatformHelper.DetectPlatformAsync(exePathSnapshot ?? "");
                DispatcherQueue.TryEnqueue(() =>
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

            PropUseAlternativeLaunch.IsChecked = _editingItem.UseAlternativeLaunch;
            PropAlternativeLaunchCommand.Text = _editingItem.AlternativeLaunchCommand ?? "";
            PropIsAltAdmin.IsChecked = _editingItem.IsAltAdmin;
            PropRunAlongside.IsChecked = _editingItem.RunAlongside;
            PropAlongsideCommand.Text = _editingItem.AlongsideCommand ?? "";
            PropIsAlongsideAdmin.IsChecked = _editingItem.IsAlongsideAdmin;

            var customItems = _editingItem.GetCustomMenuItems();
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

            UpdatePropIconView(_editingItem.IconPath);
        }
        catch (Exception ex) { LogService.Write("UI", "LoadUI failed", ex); }
    }

    private void UpdateCustomVisibility()
    {
        try
        {
            int visibleCount = 0;
            string customItemLabel = Text.T("Property_CustomItem");
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
        catch (Exception ex) { LogService.Write("UI", "UpdateCustomVisibility failed", ex); }
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
                    var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
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

    private void ShowPanel()
    {
        PropertyPanel.Visibility = Visibility.Visible;
        MenusRequested?.Invoke();

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

    private void HidePanel()
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
            _editingItem = null;
            _isNewItemMode = false;
        };

        storyboard.Begin();
    }

    private void BtnCloseProperty_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HidePanel();
        }
        catch (Exception ex) { LogService.Write("UI", "BtnCloseProperty_Click failed", ex); }
    }

    private void BtnSaveProperty_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveRequested?.Invoke();
        }
        catch (Exception ex) { LogService.Write("UI", "BtnSaveProperty_Click failed", ex); }
    }

    private void BtnDeleteProperty_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DeleteRequested?.Invoke();
        }
        catch (Exception ex) { LogService.Write("UI", "BtnDeleteProperty_Click failed", ex); }
    }

    private async void BtnChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        using (LogService.StartOperation("UI", "BtnChangeIcon_Click"))
        {
            try
            {
                string filter = Win32FileDialog.BuildFilter(Win32FileDialog.FilterExecutablesAndImages, Win32FileDialog.FilterAll);
                string? filePath = Win32FileDialog.ShowOpenFileDialog(OwnerWindowHandle, Text.T("FileDialog_SelectIconFile"), filter);

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    LogService.Write("UI", $"BtnChangeIcon_Click selectedFile={filePath}");

                    PropIcon.Source = null;

                    string? newPath = null;
                    try
                    {
                        if (_editingItem == null)
                        {
                            LogService.Write("UI", "BtnChangeIcon_Click aborted: no current editing item");
                            return;
                        }
                        newPath = await IconHelper.GetIconPathAsync(filePath, _editingItem.Id, forceExtract: true);
                        LogService.Write("UI", $"BtnChangeIcon_Click GetIconPathAsync returned newPath={newPath}");
                    }
                    catch (Exception ex)
                    {
                        LogService.Write("UI", "BtnChangeIcon_Click GetIconPathAsync failed", ex);
                    }

                    if (!string.IsNullOrEmpty(newPath) && File.Exists(newPath))
                    {
                        if (_editingItem != null)
                        {
                            _editingItem.IconPath = null;
                            _editingItem.IconPath = newPath;
                            UpdatePropIconView(newPath);
                            LogService.Write("UI", $"BtnChangeIcon_Click updated item icon id={_editingItem.Id} newPath={newPath}");
                        }
                        else
                        {
                            LogService.Write("UI", "BtnChangeIcon_Click: current editing item became null before update");
                        }
                    }
                    else
                    {
                        LogService.Write("UI", $"BtnChangeIcon_Click no valid icon produced for selectedFile={filePath} newPath={newPath}");
                    }
                }
            }
            catch (Exception ex) { LogService.Write("UI", "BtnChangeIcon_Click failed", ex); }
        }
    }

    private void PopulateMenuItems(MenuFlyoutSubItem parent, List<ShortcutScanner.FileItem>? items, TextBox? targetTextBox)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            if (!item.IsFolder)
            {
                var menuItem = new MenuFlyoutItem { Text = item.Name, Tag = item.FullPath };
                LogService.Write("UI", $"PopulateMenuItems adding item name={item.Name} path={item.FullPath} targetTextBox={(targetTextBox?.Name ?? "")}");
                menuItem.Click += (s, e) => OnShortcutMenuItemClick(item.FullPath, targetTextBox, item.Name);
                parent.Items.Add(menuItem);
            }
            else if (item.IsFolder && item.Children.Count > 0)
            {
                LogService.Write("UI", $"PopulateMenuItems entering folder name={item.Name} childCount={item.Children.Count}");
                PopulateMenuItems(parent, item.Children, targetTextBox);
            }
        }
    }

    private async void OnShortcutMenuItemClick(string filePath, TextBox? targetTextBox, string? displayName = null)
    {
        using (LogService.StartOperation("UI", "OnShortcutMenuItemClick"))
        {
            try
            {
                LogService.Write("UI", $"OnShortcutMenuItemClick Start filePath={filePath} displayName={displayName}");
                if (string.IsNullOrEmpty(filePath))
                {
                    LogService.Write("UI", "OnShortcutMenuItemClick aborted: empty filePath");
                    return;
                }
                bool isStoreApp = filePath.StartsWith("shell:AppsFolder\\");

                var resolved = ShortcutResolver.ResolveTargetPath(filePath);
                string actualPath = resolved.ActualPath;
                ShortcutInfo? shortcutInfo = resolved.Info;
                bool isUrlProtocol = resolved.IsUrlProtocol;
                bool extractFromLnk = isUrlProtocol;

                LogService.Write("UI", $"OnShortcutMenuItemClick resolved actualPath={actualPath} isStoreApp={isStoreApp} isUrlProtocol={isUrlProtocol} extractFromLnk={extractFromLnk}");
                if (shortcutInfo != null)
                {
                    LogService.Write("UI", $"OnShortcutMenuItemClick shortcutInfo AUMID={shortcutInfo.AUMID} TargetPath={shortcutInfo.TargetPath} IconPath={shortcutInfo.IconPath} IsUrl={shortcutInfo.IsUrl}");
                }

                if (targetTextBox != null) targetTextBox.Text = actualPath;

                if (targetTextBox == PropExePath)
                {
                    if (string.IsNullOrEmpty(PropTitle.Text) || _isNewItemMode)
                    {
                        PropTitle.Text = displayName ?? Path.GetFileNameWithoutExtension(filePath);
                    }

                    _editingItem!.ExePath = actualPath;

                    PropIcon.Source = null;
                    string? iconPath = null;

                    if (actualPath.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                    {
                        string? steamExePath = SteamHelper.GetExecutableFromSteamUrl(actualPath);
                        if (!string.IsNullOrEmpty(steamExePath) && File.Exists(steamExePath))
                        {
                            LogService.Write("UI", $"OnShortcutMenuItemClick extracting icon from steamExePath={steamExePath}");
                            iconPath = await IconHelper.GetIconPathAsync(steamExePath, _editingItem.Id, forceExtract: true);
                            LogService.Write("UI", $"OnShortcutMenuItemClick steam iconPath={iconPath}");
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

                        LogService.Write("UI", $"OnShortcutMenuItemClick calling GetIconPathAsync iconSource={iconSource} shouldExtractFromLnk={shouldExtractFromLnk}");
                        iconPath = await IconHelper.GetIconPathAsync(iconSource, _editingItem.Id, forceExtract: true);
                        LogService.Write("UI", $"OnShortcutMenuItemClick GetIconPathAsync returned iconPath={iconPath}");
                    }

                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        _editingItem.IconPath = null;
                        _editingItem.IconPath = iconPath;
                        RefreshIconDisplay(iconPath);
                    }
                }
            }
            catch (Exception ex) { LogService.Write("UI", "OnShortcutMenuItemClick failed", ex); }
        }
    }

    private void BtnBrowseCustom_Click(int index)
    {
        BrowseFile(_customCommands[index], Win32FileDialog.BuildFilter(Win32FileDialog.FilterAll));
    }

    private async void BrowseFile(TextBox target, string? filter = null)
    {
        using (LogService.StartOperation("UI", "BrowseFile"))
        {
            try
            {
                if (target == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(filter))
                {
                    filter = Win32FileDialog.BuildFilter(Win32FileDialog.FilterExecutables, Win32FileDialog.FilterAll);
                }
                LogService.Write("UI", $"BrowseFile invoking dialog filter={filter}");
                string? filePath = Win32FileDialog.ShowOpenFileDialog(OwnerWindowHandle, Text.T("FileDialog_SelectFile"), filter);
                LogService.Write("UI", $"BrowseFile dialog returned filePath={filePath}");

                if (!string.IsNullOrEmpty(filePath))
                {
                    var resolved = ShortcutResolver.ResolveTargetPath(filePath);
                    string actualPath = resolved.ActualPath;
                    bool isUrlProtocol = resolved.IsUrlProtocol;
                    ShortcutInfo? shortcutInfo = resolved.Info;

                    LogService.Write("UI", $"BrowseFile resolved actualPath={actualPath} isUrlProtocol={isUrlProtocol}");
                    if (shortcutInfo != null)
                    {
                        LogService.Write("UI", $"BrowseFile shortcutInfo AUMID={shortcutInfo.AUMID} TargetPath={shortcutInfo.TargetPath} IconPath={shortcutInfo.IconPath} IsUrl={shortcutInfo.IsUrl}");
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

                        _editingItem!.ExePath = actualPath;

                        PropIcon.Source = null;
                        string? iconPath = null;

                        if (actualPath.Contains("steam://", StringComparison.OrdinalIgnoreCase))
                        {
                            string? steamExePath = SteamHelper.GetExecutableFromSteamUrl(actualPath);
                            if (!string.IsNullOrEmpty(steamExePath) && File.Exists(steamExePath))
                            {
                                LogService.Write("UI", $"BrowseFile extracting steam icon from {steamExePath}");
                                iconPath = await IconHelper.GetIconPathAsync(steamExePath, _editingItem.Id, forceExtract: true);
                                LogService.Write("UI", $"BrowseFile steam iconPath={iconPath}");
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
                            LogService.Write("UI", $"BrowseFile calling GetIconPathAsync iconSource={iconSource}");
                            iconPath = await IconHelper.GetIconPathAsync(iconSource, _editingItem.Id, forceExtract: true);
                            LogService.Write("UI", $"BrowseFile GetIconPathAsync returned iconPath={iconPath}");
                        }

                        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        {
                            _editingItem.IconPath = null;
                            _editingItem.IconPath = iconPath;
                            RefreshIconDisplay(iconPath);
                        }
                    }
                }
            }
            catch (Exception ex) { LogService.Write("UI", "BrowseFile failed", ex); }
        }
    }

    private void BtnBrowseExe_Click(object sender, RoutedEventArgs e)
    {
        try { BrowseFile(PropExePath); } catch (Exception ex) { LogService.Write("UI", "BtnBrowseExe_Click inner BrowseFile failed", ex); }
    }

    private void BtnBrowseAlt_Click(object sender, RoutedEventArgs e)
    {
        try { BrowseFile(PropAlternativeLaunchCommand); } catch (Exception ex) { LogService.Write("UI", "BtnBrowseAlt_Click inner BrowseFile failed", ex); }
    }

    private void BtnBrowseAlongside_Click(object sender, RoutedEventArgs e)
    {
        try { BrowseFile(PropAlongsideCommand); } catch (Exception ex) { LogService.Write("UI", "BtnBrowseAlongside_Click inner BrowseFile failed", ex); }
    }

    private void BtnBrowseMgr_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BrowseFile(PropMgrPath);
        }
        catch (Exception ex) { LogService.Write("UI", "BtnBrowseMgr_Click failed", ex); }
    }
}
