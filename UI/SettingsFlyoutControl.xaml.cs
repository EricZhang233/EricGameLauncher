using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace EricGameLauncher;

public sealed partial class SettingsFlyoutControl : UserControl
{
    public event Action<string>? LaunchModeChanged;
    public event Action? UpdateChannelChanged;
    public event Action<Exception>? OperationFailed;
    public event Action? ConfigurationImported;
    public IntPtr OwnerWindowHandle { get; set; }
    private bool _transferring;
    private const string GitHubTokenUrl = "https://github.com/settings/personal-access-tokens";
    private bool _syncingQuickStart = false;
    private readonly List<(Button Button, Func<string> Text)> _helpButtons = new();

    public Flyout Flyout => SettingsFlyout;

    public SettingsFlyoutControl()
    {
        InitializeComponent();
        RegisterHelpButton(QuickStartHelpButton, () => Text.T("Settings_QuickStart_Desc"));
        RegisterHelpButton(UpdateChannelHelpButton, () => Text.T("Settings_UpdateChannel_Desc"));
        RegisterHelpButton(GitHubTokenHelpButton, () => Text.T("Settings_GitHubTokenDesc"));
        RegisterHelpButton(StorageLocationHelpButton, () => Text.T(ConfigService.IsStorageModeLocked ? "Settings_SystemOnlyNote" : "Settings_MigrateNote"));
    }

    private void RegisterHelpButton(Button button, Func<string> text)
    {
        _helpButtons.Add((button, text));
    }

    private void RefreshHelpTexts()
    {
        foreach (var (button, text) in _helpButtons)
            ToolTipService.SetToolTip(button, CreateHelpTextBlock(text()));
    }

    private static TextBlock CreateHelpTextBlock(string text)
        => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        string text = "";
        foreach (var entry in _helpButtons)
        {
            if (entry.Button == button) { text = entry.Text(); break; }
        }
        if (string.IsNullOrEmpty(text)) return;

        var flyout = new Flyout { Content = CreateHelpTextBlock(text) };
        flyout.Opened += (s, args) => ToolTipService.SetToolTip(button, null);
        flyout.Closed += (s, args) => ToolTipService.SetToolTip(button, CreateHelpTextBlock(text));
        flyout.ShowAt(button);
        LogService.Write("UI", $"SettingsFlyoutControl help opened for={button.Name}");
    }

    public void Sync()
    {
        _syncingQuickStart = true;
        ToggleQuickStart.IsOn = ConfigService.QuickStart;
        _syncingQuickStart = false;
        ToggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
        ComboUpdateChannel.SelectedIndex = ConfigService.UpdateChannel == "latest" ? 1 : 0;
        UpdateGitHubTokenStatus();
        GitHubTokenEditRow.Visibility = Visibility.Collapsed;
        GitHubTokenStatusText.Visibility = Visibility.Visible;
        GitHubTokenEditBtn.IsEnabled = true;
        UpdateStorageModeUI();
    }

    public void ApplyLocalization()
    {
        SettingsTitle.Text = Text.T("Settings_Title");
        SettingsQuickStartLabel.Text = Text.T("Settings_QuickStart");
        RefreshHelpTexts();
        SettingsCloseAfterLaunchLabel.Text = Text.T("Settings_CloseAfterLaunch");
        SettingsLaunchModeLabel.Text = Text.T("Settings_LaunchMode");

        ComboLaunchMode.SelectionChanged -= ComboLaunchMode_SelectionChanged;
        ComboLaunchMode.Items.Clear();
        ComboLaunchMode.Items.Add(new ComboBoxItem { Content = Text.T("Settings_LaunchMode_Single"), Tag = "single" });
        ComboLaunchMode.Items.Add(new ComboBoxItem { Content = Text.T("Settings_LaunchMode_Double"), Tag = "double" });
        ComboLaunchMode.SelectedIndex = ConfigService.LaunchMode == "double" ? 1 : 0;
        ComboLaunchMode.SelectionChanged += ComboLaunchMode_SelectionChanged;

        SettingsUpdateChannelLabel.Text = Text.T("Settings_UpdateChannel");
        ComboUpdateChannel.SelectionChanged -= ComboUpdateChannel_SelectionChanged;
        ComboUpdateChannel.Items.Clear();
        ComboUpdateChannel.Items.Add(Text.T("Settings_UpdateChannel_Stable"));
        ComboUpdateChannel.Items.Add(Text.T("Settings_UpdateChannel_Latest"));
        ComboUpdateChannel.SelectedIndex = ConfigService.UpdateChannel == "latest" ? 1 : 0;
        ComboUpdateChannel.SelectionChanged += ComboUpdateChannel_SelectionChanged;

        SettingsGitHubTokenLabel.Text = Text.T("Settings_GitHubTokenLabel");
        GitHubTokenBox.PlaceholderText = Text.T("Settings_GitHubTokenPlaceholder");
        ToolTipService.SetToolTip(SettingsGitHubTokenLink, Text.T("Settings_GitHubTokenLink"));
        ToolTipService.SetToolTip(GitHubTokenEditBtn, Text.T("Settings_GitHubTokenEdit"));
        GitHubTokenSaveBtn.Content = Text.T("Settings_GitHubTokenSave");
        ToolTipService.SetToolTip(GitHubTokenSaveBtn, Text.T("Settings_GitHubTokenSave"));
        GitHubTokenCancelBtn.Content = Text.T("Settings_GitHubTokenCancel");
        ToolTipService.SetToolTip(GitHubTokenCancelBtn, Text.T("Settings_GitHubTokenCancel"));
        UpdateGitHubTokenStatus();

        SettingsDataLocationLabel.Text = Text.T("Settings_DataLocation");
        ToolTipService.SetToolTip(BtnConfigurationTransfer, Text.T("ConfigArchive_Entry"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BtnConfigurationTransfer, Text.T("ConfigArchive_Entry"));
        ToolTipService.SetToolTip(BtnOpenConfigFolder, Text.T("Settings_OpenConfigFolder"));
        ToolTipService.SetToolTip(BtnOpenCacheFolder, Text.T("Settings_OpenCacheFolder"));
        UpdateStorageModeUI();
    }

    private void ToggleCloseAfterLaunch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && ConfigService.CloseAfterLaunch != toggle.IsOn)
        {
            ConfigService.CloseAfterLaunch = toggle.IsOn;
            PersistSettings();
        }
    }

    private void ToggleQuickStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncingQuickStart) return;
        if (sender is not ToggleSwitch toggle) return;
        if (ConfigService.QuickStart == toggle.IsOn) return;

        bool applied;
        try { applied = QuickStartService.SetEnabled(toggle.IsOn); }
        catch (Exception ex) { OperationFailed?.Invoke(ex); applied = false; }
        if (!applied)
        {
            _syncingQuickStart = true;
            toggle.IsOn = ConfigService.QuickStart;
            _syncingQuickStart = false;
            LogService.Write("QuickStart", "Quick start toggle reverted, auto start registration failed", null, null, LogService.LogLevel.Error);
            return;
        }

        LogService.Write("UI", $"SettingsFlyoutControl quick start changed to={toggle.IsOn}");
    }

    private void ComboLaunchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboLaunchMode.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ConfigService.LaunchMode = val;
            PersistSettings();
            LaunchModeChanged?.Invoke(val);
            LogService.Write("UI", $"SettingsFlyoutControl launch mode changed to={val}");
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
                PersistSettings();
                UpdateChannelChanged?.Invoke();
                LogService.Write("UI", $"SettingsFlyoutControl update channel changed to={newChannel}");
            }
        }
    }

    private void GitHubTokenEditBtn_Click(object sender, RoutedEventArgs e)
    {
        GitHubTokenStatusText.Visibility = Visibility.Collapsed;
        GitHubTokenEditBtn.IsEnabled = false;
        GitHubTokenEditRow.Visibility = Visibility.Visible;
        GitHubTokenBox.Text = ConfigService.GitHubToken;
        GitHubTokenBox.Focus(FocusState.Programmatic);
        LogService.Write("UI", "GitHubTokenEditBtn: entering edit mode");
    }

    private void GitHubTokenSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        string newValue = GitHubTokenBox.Text ?? "";
        try
        {
            ConfigService.SaveGitHubToken(newValue);
            LogService.Write("UI", "GitHubTokenSaveBtn: token saved");
            SwitchToTokenView();
        }
        catch (Exception ex) { OperationFailed?.Invoke(ex); }
    }

    private void GitHubTokenCancelBtn_Click(object sender, RoutedEventArgs e) => SwitchToTokenView();

    private void GitHubTokenLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = GitHubTokenUrl,
                UseShellExecute = true
            });
            LogService.Write("UI", "GitHubTokenLink_Click opened personal access tokens page");
        }
        catch (Exception ex) { LogService.Write("UI", "GitHubTokenLink_Click failed", ex); }
    }

    private void SwitchToTokenView()
    {
        GitHubTokenEditRow.Visibility = Visibility.Collapsed;
        GitHubTokenStatusText.Visibility = Visibility.Visible;
        GitHubTokenEditBtn.IsEnabled = true;
        UpdateGitHubTokenStatus();
        LogService.Write("UI", "GitHubToken: switched to view mode");
    }

    private void UpdateGitHubTokenStatus()
    {
        bool hasToken = !string.IsNullOrEmpty(ConfigService.GitHubToken);
        GitHubTokenStatusText.Text = hasToken ? Text.T("Settings_GitHubTokenConfigured") : Text.T("Settings_GitHubTokenNotConfigured");
    }

    private async void BtnSwitchStorageMode_Click(object sender, RoutedEventArgs e)
    {
        if (DebugPaths.IsDebug() || ConfigService.IsStorageModeLocked)
        {
            try { LogService.Write("Config", "BtnSwitchStorageMode_Click ignored: storage switching disabled"); } catch { }
            return;
        }
        using (LogService.StartOperation("Config", "BtnSwitchStorageMode_Click"))
        {
            try
            {
                bool switchToSystemMode = !ConfigService.IsSystemMode;
                if (ConfigService.CloseAfterLaunch != ToggleCloseAfterLaunch.IsOn)
                {
                    ConfigService.CloseAfterLaunch = ToggleCloseAfterLaunch.IsOn;
                    LogService.Write("Config", $"ToggleCloseAfterLaunch changed to={ToggleCloseAfterLaunch.IsOn}");
                }
                await ConfigService.SwitchStorageModeAsync(switchToSystemMode);
                UpdateStorageModeUI();
                LogService.Write("Config", $"SwitchStorageMode completed target={(switchToSystemMode ? "System" : "Portable")}");
            }
            catch (Exception ex) { OperationFailed?.Invoke(ex); }
        }
    }

    private bool PersistSettings()
    {
        try { ConfigService.SaveSettings(); return true; }
        catch (Exception ex) { OperationFailed?.Invoke(ex); return false; }
    }

    private void SetTransferBusy(bool busy)
    {
        _transferring = busy;
        BtnConfigurationTransfer.IsEnabled = !busy;
    }

    private static string ConfigurationFilter => Win32FileDialog.BuildFilter(Text.T("ConfigArchive_Filter") + "\0*.zip\0");

    private async void BtnConfigurationTransfer_Click(object sender, RoutedEventArgs e)
    {
        if (_transferring || sender is not FrameworkElement element || element.XamlRoot == null) return;
        var root = element.XamlRoot;
        SetTransferBusy(true);
        SettingsFlyout.Hide();
        try
        {
            var transfer = CreateConfigurationDialog(root);
            await transfer.Dialog.ShowAsync();
        }
        catch (Exception ex) { OperationFailed?.Invoke(ex); }
        finally { SetTransferBusy(false); }
    }

    internal static (StackPanel Content, CheckBox Settings, CheckBox Items) CreateImportSelection()
    {
        var settings = new CheckBox { Content = Text.T("ConfigArchive_Settings"), IsChecked = true };
        var items = new CheckBox { Content = Text.T("ConfigArchive_Items"), IsChecked = true };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = Text.T("ConfigArchive_Select"), TextWrapping = TextWrapping.Wrap });
        var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        choices.Children.Add(settings);
        choices.Children.Add(items);
        content.Children.Add(choices);
        return (content, settings, items);
    }

    internal static string DescribeConfigurationBackup(ConfigurationBackupInfo information)
    {
        string unknown = Text.T("ConfigArchive_Unknown");
        return string.Join(Environment.NewLine,
            Text.T("ConfigArchive_BackupFileName") + information.OriginalFileName,
            Text.T("ConfigArchive_BackupRuleVersion") + information.RuleVersion,
            Text.T("ConfigArchive_BackupConfigVersion") + information.ConfigurationVersion,
            Text.T("ConfigArchive_BackupAppVersion") + (string.IsNullOrWhiteSpace(information.GeneratorVersion) ? unknown : information.GeneratorVersion),
            Text.T("ConfigArchive_BackupCreatedAt") + (information.GeneratedAt == default ? unknown
                : information.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture)));
    }

    internal (ContentDialog Dialog, Button Import, Button Export) CreateConfigurationDialog(XamlRoot root)
    {
        double viewWidth = root.Size.Width > 0 ? root.Size.Width : 950;
        double dialogWidth = Math.Max(560, viewWidth * 0.80);
        var import = new Button { HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock { Text = Text.T("ConfigArchive_Import"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center } };
        var export = new Button { HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock { Text = Text.T("ConfigArchive_Export"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center } };
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(export, 1);
        actions.Children.Add(import);
        actions.Children.Add(export);
        var status = new InfoBar { IsOpen = true, IsClosable = false, Message = Text.T("ConfigArchive_Operation") };
        var progress = new ProgressRing { Width = 24, Height = 24, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        var selection = CreateImportSelection();
        selection.Content.Visibility = Visibility.Collapsed;
        string group = "ConfigurationImport-" + Guid.NewGuid().ToString("N");
        var replace = new RadioButton { Content = Text.T("ConfigArchive_Replace"), GroupName = group };
        var merge = new RadioButton { Content = Text.T("ConfigArchive_Merge"), GroupName = group };
        var conflicts = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        conflicts.Children.Add(new TextBlock { Text = Text.T("ConfigArchive_ConflictBody"), TextWrapping = TextWrapping.Wrap });
        var conflictChoices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        conflictChoices.Children.Add(replace);
        conflictChoices.Children.Add(merge);
        conflicts.Children.Add(conflictChoices);
        var conflictDescription = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        conflicts.Children.Add(conflictDescription);
        var content = new StackPanel { Spacing = 12, Width = dialogWidth - 48 };
        content.Children.Add(actions);
        content.Children.Add(status);
        content.Children.Add(progress);
        content.Children.Add(selection.Content);
        content.Children.Add(conflicts);
        var dialog = new ContentDialog { XamlRoot = root, Title = Text.T("ConfigArchive_Title"),
            Content = new ScrollViewer { Content = content, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = Text.T("ConfigArchive_Close"), DefaultButton = ContentDialogButton.Close };
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;
        ConfigurationArchive? archive = null;
        ConfigurationImportPlan? plan = null;
        bool working = false;
        ConfigurationSections Sections() => (selection.Settings.IsChecked == true ? ConfigurationSections.Settings : 0)
            | (selection.Items.IsChecked == true ? ConfigurationSections.Items : 0);
        void UpdatePrimary()
        {
            dialog.PrimaryButtonText = archive == null ? "" : Text.T("ConfigArchive_BeginImport");
            dialog.IsPrimaryButtonEnabled = !working && archive != null && plan != null && Sections() != 0
                && (!plan.HasConflicts || replace.IsChecked == true || merge.IsChecked == true);
        }
        void UpdateConflictDescription()
        {
            string? key = replace.IsChecked == true ? "ConfigArchive_ReplaceDescription" : merge.IsChecked == true ? "ConfigArchive_MergeDescription" : null;
            conflictDescription.Text = key == null ? "" : Text.T(key);
            conflictDescription.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
            UpdatePrimary();
        }
        void SetWorking(bool value)
        {
            working = value;
            import.IsEnabled = export.IsEnabled = selection.Settings.IsEnabled = selection.Items.IsEnabled = replace.IsEnabled = merge.IsEnabled = !value;
            progress.IsActive = value;
            progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            dialog.CloseButtonText = value ? "" : Text.T("ConfigArchive_Close");
            dialog.DefaultButton = value ? ContentDialogButton.None : ContentDialogButton.Close;
            UpdatePrimary();
        }
        void ShowStatus(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        { status.Message = message; status.Severity = severity; }
        void ClearImport()
        {
            archive = null;
            plan = null;
            selection.Content.Visibility = conflicts.Visibility = Visibility.Collapsed;
            ShowStatus(Text.T("ConfigArchive_Operation"));
            UpdatePrimary();
        }
        async Task RefreshPlanAsync()
        {
            plan = null;
            conflicts.Visibility = Visibility.Collapsed;
            replace.IsChecked = merge.IsChecked = false;
            UpdatePrimary();
            if (archive == null || Sections() == 0) return;
            SetWorking(true);
            try
            {
                var sections = Sections();
                plan = await Task.Run(() => ConfigurationArchive.Preview(sections));
                conflicts.Visibility = plan.HasConflicts ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
            finally { SetWorking(false); }
        }
        import.Click += async (_, _) =>
        {
            if (working) return;
            ClearImport();
            SetWorking(true);
            try
            {
                string? path = Win32FileDialog.ShowOpenFileDialog(OwnerWindowHandle, Text.T("ConfigArchive_Import"), ConfigurationFilter);
                if (path == null) return;
                ShowStatus(Text.T("ConfigArchive_Working"));
                archive = await Task.Run(() => ConfigurationArchive.Read(path));
                selection.Content.Visibility = Visibility.Visible;
                ShowStatus(DescribeConfigurationBackup(archive.Information));
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
            finally { SetWorking(false); }
            if (archive != null) await RefreshPlanAsync();
        };
        export.Click += async (_, _) =>
        {
            if (working) return;
            ClearImport();
            SetWorking(true);
            try
            {
                string? path = Win32FileDialog.ShowSaveFileDialog(OwnerWindowHandle, Text.T("ConfigArchive_Export"), ConfigurationFilter,
                    "EricGameLauncher-config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", "zip");
                if (path == null) return;
                ShowStatus(Text.T("ConfigArchive_Working"));
                int missing = await Task.Run(() => ConfigurationArchive.Export(path));
                string message = Text.T("ConfigArchive_Exported");
                if (missing > 0) message += "\n" + string.Format(Text.T("ConfigArchive_MissingIcons"), missing);
                ShowStatus(message, missing > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
            finally { SetWorking(false); }
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (working || archive == null || plan == null || !dialog.IsPrimaryButtonEnabled) return;
            var sections = Sections();
            var mode = merge.IsChecked == true ? ConfigurationImportMode.Merge : ConfigurationImportMode.Replace;
            SetWorking(true);
            ShowStatus(Text.T("ConfigArchive_Working"));
            bool changed = false;
            try
            {
                var selectedArchive = archive;
                var selectedPlan = plan;
                var result = await Task.Run(() => selectedArchive.Import(sections, mode, selectedPlan));
                string message = (sections & ConfigurationSections.Items) != 0
                    ? string.Format(Text.T("ConfigArchive_Imported"), result.Added, result.Skipped) : Text.T("ConfigArchive_SettingsImported");
                bool warning = false;
                if ((sections & ConfigurationSections.Settings) != 0)
                {
                    if (!QuickStartService.SetEnabled(ConfigService.QuickStart)) { warning = true; message += "\n" + Text.T("ConfigArchive_QuickStartFailed"); }
                    message += "\n" + Text.T("ConfigArchive_RestartNote");
                }
                if (archive.MissingIcons > 0) { warning = true; message += "\n" + string.Format(Text.T("ConfigArchive_MissingIcons"), archive.MissingIcons); }
                ClearImport();
                ConfigurationImported?.Invoke();
                ApplyLocalization();
                Sync();
                dialog.Title = Text.T("ConfigArchive_Title");
                ((TextBlock)import.Content).Text = Text.T("ConfigArchive_Import");
                ((TextBlock)export.Content).Text = Text.T("ConfigArchive_Export");
                selection.Settings.Content = Text.T("ConfigArchive_Settings");
                selection.Items.Content = Text.T("ConfigArchive_Items");
                ((TextBlock)selection.Content.Children[0]).Text = Text.T("ConfigArchive_Select");
                ((TextBlock)conflicts.Children[0]).Text = Text.T("ConfigArchive_ConflictBody");
                replace.Content = Text.T("ConfigArchive_Replace");
                merge.Content = Text.T("ConfigArchive_Merge");
                UpdateConflictDescription();
                ShowStatus(message, warning ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            catch (ConfigurationChangedException) { changed = true; ShowStatus(Text.T("ConfigArchive_Changed"), InfoBarSeverity.Warning); }
            catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
            finally { SetWorking(false); }
            if (changed) await RefreshPlanAsync();
        };
        selection.Settings.Checked += async (_, _) => await RefreshPlanAsync();
        selection.Settings.Unchecked += async (_, _) => await RefreshPlanAsync();
        selection.Items.Checked += async (_, _) => await RefreshPlanAsync();
        selection.Items.Unchecked += async (_, _) => await RefreshPlanAsync();
        replace.Checked += (_, _) => UpdateConflictDescription();
        replace.Unchecked += (_, _) => UpdateConflictDescription();
        merge.Checked += (_, _) => UpdateConflictDescription();
        merge.Unchecked += (_, _) => UpdateConflictDescription();
        dialog.Closing += (_, args) => { if (working) args.Cancel = true; };
        UpdatePrimary();
        return (dialog, import, export);
    }

    private void UpdateStorageModeUI()
    {
        string baseModeText;
        if (ConfigService.IsSystemMode)
        {
            baseModeText = Text.T("Settings_SystemMode");
            ToolTipService.SetToolTip(BtnSwitchStorageMode, Text.T("Settings_SwitchToPortable"));
        }
        else
        {
            baseModeText = Text.T("Settings_PortableMode");
            ToolTipService.SetToolTip(BtnSwitchStorageMode, Text.T("Settings_SwitchToSystem"));
        }
        var displayText = baseModeText ?? "";
        if (DebugPaths.IsDebug())
        {
            displayText = string.IsNullOrEmpty(displayText) ? "DebugMode" : displayText + " DebugMode";
            try { LogService.Write("Config", "UpdateStorageModeUI: Debug mode active"); } catch { }
            BtnSwitchStorageMode.Visibility = Visibility.Collapsed;
            ToolTipService.SetToolTip(BtnSwitchStorageMode, "Debug mode: storage switching disabled");
        }
        else
        {
            BtnSwitchStorageMode.Visibility = ConfigService.IsStorageModeLocked ? Visibility.Collapsed : Visibility.Visible;
        }

        StorageModeText.Text = displayText;
        ToolTipService.SetToolTip(StorageModeText, ConfigService.CurrentDataPath);
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(ConfigService.CurrentDataPath);
    }

    private void BtnOpenCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(ConfigService.SystemCachePath);
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            if (!System.IO.Directory.Exists(folder))
                System.IO.Directory.CreateDirectory(folder);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder.Replace("\"", "\\\"")}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { LogService.Write("App", "OpenFolder failed", ex); }
    }
}
