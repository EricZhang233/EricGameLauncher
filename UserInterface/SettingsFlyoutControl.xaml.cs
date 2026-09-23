using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EricGameLauncher;

public sealed partial class SettingsFlyoutControl : UserControl
{
    public event Action<string>? LaunchModeChanged;
    public event Action? UpdateChannelChanged;

    public Flyout Flyout => SettingsFlyout;

    public SettingsFlyoutControl()
    {
        InitializeComponent();
    }

    public void Sync()
    {
        ToggleCloseAfterLaunch.IsOn = ConfigService.CloseAfterLaunch;
        ComboUpdateChannel.SelectedIndex = ConfigService.UpdateChannel == "latest" ? 1 : 0;
        UpdateGitHubTokenStatus();
        GitHubTokenEditRow.Visibility = Visibility.Collapsed;
        GitHubTokenViewRow.Visibility = Visibility.Visible;
        UpdateStorageModeUI();
    }

    public void ApplyLocalization()
    {
        SettingsTitle.Text = Text.T("Settings_Title");
        SettingsGeneralLabel.Text = Text.T("Settings_General");
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

        SettingsUpdateChannelDesc.Text = Text.T("Settings_UpdateChannel_Desc");
        SettingsGitHubTokenLabel.Text = Text.T("Settings_GitHubTokenLabel");
        GitHubTokenBox.PlaceholderText = Text.T("Settings_GitHubTokenPlaceholder");
        SettingsGitHubTokenDesc.Text = Text.T("Settings_GitHubTokenDesc");
        SettingsGitHubTokenLink.Content = Text.T("Settings_GitHubTokenLink");
        GitHubTokenEditBtn.Content = Text.T("Settings_GitHubTokenEdit");
        ToolTipService.SetToolTip(GitHubTokenEditBtn, Text.T("Settings_GitHubTokenEdit"));
        GitHubTokenSaveBtn.Content = Text.T("Settings_GitHubTokenSave");
        ToolTipService.SetToolTip(GitHubTokenSaveBtn, Text.T("Settings_GitHubTokenSave"));
        GitHubTokenCancelBtn.Content = Text.T("Settings_GitHubTokenCancel");
        ToolTipService.SetToolTip(GitHubTokenCancelBtn, Text.T("Settings_GitHubTokenCancel"));
        UpdateGitHubTokenStatus();

        SettingsDataLocationLabel.Text = Text.T("Settings_DataLocation");
        SettingsMigrateNote.Text = Text.T("Settings_MigrateNote");
        ToolTipService.SetToolTip(BtnOpenConfigFolder, Text.T("Settings_OpenConfigFolder"));
        ToolTipService.SetToolTip(BtnOpenCacheFolder, Text.T("Settings_OpenCacheFolder"));
        UpdateStorageModeUI();
    }

    private void ToggleCloseAfterLaunch_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && ConfigService.CloseAfterLaunch != toggle.IsOn)
            ConfigService.CloseAfterLaunch = toggle.IsOn;
    }

    private void ComboLaunchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboLaunchMode.SelectedItem is ComboBoxItem item && item.Tag is string val)
        {
            ConfigService.LaunchMode = val;
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
                UpdateChannelChanged?.Invoke();
                LogService.Write("UI", $"SettingsFlyoutControl update channel changed to={newChannel}");
            }
        }
    }

    private void GitHubTokenEditBtn_Click(object sender, RoutedEventArgs e)
    {
        GitHubTokenViewRow.Visibility = Visibility.Collapsed;
        GitHubTokenEditRow.Visibility = Visibility.Visible;
        GitHubTokenBox.Text = ConfigService.GitHubToken;
        GitHubTokenBox.Focus(FocusState.Programmatic);
        LogService.Write("UI", "GitHubTokenEditBtn: entering edit mode");
    }

    private void GitHubTokenSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        string newValue = GitHubTokenBox.Text ?? "";
        if (ConfigService.GitHubToken != newValue)
        {
            ConfigService.GitHubToken = newValue;
            ConfigService.SaveAll();
            LogService.Write("UI", "GitHubTokenSaveBtn: token updated");
        }
        SwitchToTokenView();
    }

    private void GitHubTokenCancelBtn_Click(object sender, RoutedEventArgs e) => SwitchToTokenView();

    private void SwitchToTokenView()
    {
        GitHubTokenEditRow.Visibility = Visibility.Collapsed;
        GitHubTokenViewRow.Visibility = Visibility.Visible;
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
                if (ConfigService.CloseAfterLaunch != ToggleCloseAfterLaunch.IsOn)
                {
                    ConfigService.CloseAfterLaunch = ToggleCloseAfterLaunch.IsOn;
                    LogService.Write("Config", $"ToggleCloseAfterLaunch changed to={ToggleCloseAfterLaunch.IsOn}");
                }
                await ConfigService.SwitchStorageModeAsync(switchToSystemMode);
                UpdateStorageModeUI();
                LogService.Write("Config", $"SwitchStorageMode completed target={(switchToSystemMode ? "System" : "Portable")}");
            }
            catch (Exception ex) { LogService.Write("Config", "BtnSwitchStorageMode_Click failed", ex); }
        }
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
            BtnSwitchStorageMode.Visibility = Visibility.Visible;
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
