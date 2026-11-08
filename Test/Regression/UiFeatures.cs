using EricGameLauncher;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VirtualKey = Windows.System.VirtualKey;
using Installation = EricGameLauncher.Installation;

internal static class UiFeatures
{
    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);

    private static async Task InstallationRoutingAsync(Action<bool, string> check, MainView view, string fixture)
    {
        string isolation = @"Software\EricSoft\Tests\InstallationRouting-" + Guid.NewGuid().ToString("N");
        using var hive = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(isolation);
        var currentUser = new IntPtr(unchecked((int)0x80000001));
        string root = AppPaths.InstallDirectory;
        string uninstaller = Path.Combine(root, Installation.InstallRegistration.UninstallerName);
        string capture = Path.Combine(root, "uninstall-capture.json");
        string? previousCapture = Environment.GetEnvironmentVariable("EGL_UNINSTALL_TEST_CAPTURE");
        var copied = new List<string>();
        check(!File.Exists(uninstaller), "isolated UI host has no installed uninstaller");
        check(RegOverridePredefKey(currentUser, hive.Handle.DangerousGetHandle()) == 0, "installation routing uses an isolated user registry");
        try
        {
            var update = typeof(MainView).GetMethod("UpdateShortcutMenuState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var menu = (MenuFlyoutSubItem)view.FindName("MenuSystemIntegrationItem");
            update.Invoke(view, null);
            check(!AppInstallService.IsInstalled && menu.Visibility == Visibility.Visible && !AppInstallService.TryStartUninstaller(), "portable application displays shortcut management without launching an uninstaller");
            using var record = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Installation.InstallRegistration.RegistryPath);
            record.SetValue("InstallLocation", root);
            record.SetValue("InstallId", Guid.NewGuid().ToString("N"));
            record.SetValue("OwnerSid", Installation.InstallRegistration.CurrentUserSid);
            File.WriteAllText(uninstaller, "invalid uninstaller fixture");
            copied.Add(uninstaller);
            update.Invoke(view, null);
            check(AppInstallService.IsInstalled && menu.Visibility == Visibility.Collapsed, "registered installation hides the actual shortcut menu");
            check(StoragePolicy.RequiresSystemMode(root), "a writable registered installation still requires system storage");
            var debugMode = typeof(ConfigService).GetField("_debugModeApplied", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            object? debugBefore = debugMode.GetValue(null);
            try
            {
                debugMode.SetValue(null, false);
                bool rejected = false;
                try { ConfigService.SwitchStorageMode(false); } catch (InvalidOperationException) { rejected = true; }
                check(ConfigService.IsStorageModeLocked && rejected, "installed-mode storage cannot be switched to portable through Core");
            }
            finally { debugMode.SetValue(null, debugBefore); }
            foreach (var action in new Action[] { AppInstallService.Install, AppInstallService.Uninstall })
            {
                bool rejected = false;
                try { action(); } catch (InvalidOperationException) { rejected = true; }
                check(rejected, "installed shortcut operations are rejected before changing user shortcuts");
            }
            var install = typeof(CliService).GetMethod("CmdInstall", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var uninstall = typeof(CliService).GetMethod("CmdUninstall", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            check(await (Task<int>)install.Invoke(null, new object[] { new Dictionary<string, string>() })! != 0, "CLI install reports failure in installed mode");
            check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string>() })! != 0, "installed CLI uninstall requires an explicit data choice");
            check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string> { ["keep-data"] = "true" } })! != 0, "CLI uninstall reports launch failure instead of falling back to shortcut removal");
            foreach (string extension in new[] { ".dll", ".deps.json", ".runtimeconfig.json" })
            {
                string source = Path.ChangeExtension(fixture, extension);
                string destination = Path.Combine(root, Path.GetFileName(source));
                if (File.Exists(destination)) throw new IOException("Uninstaller fixture would overwrite " + destination);
                File.Copy(source, destination);
                copied.Add(destination);
            }
            File.Copy(fixture, uninstaller, true);
            Environment.SetEnvironmentVariable("EGL_UNINSTALL_TEST_CAPTURE", capture);
            check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string>() })! != 0 && !File.Exists(capture), "bare installed uninstall does not launch even an available uninstaller");
            check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string> { ["keep-data"] = "true", ["remove-data"] = "true" } })! != 0 && !File.Exists(capture), "installed uninstall rejects conflicting data choices without launching");
            foreach (string choice in new[] { "keep-data", "remove-data" })
            {
                File.Delete(capture);
                check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string> { [choice] = "true" } })! == 0, "CLI uninstall forwards explicit choice: " + choice);
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(capture) && DateTime.UtcNow < deadline) await Task.Delay(25);
                using var captured = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(capture));
                var arguments = captured.RootElement.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).ToArray();
                check(arguments.SequenceEqual(new[] { "--quiet", "--" + choice, "--directory", root, "--owner-sid", Installation.InstallRegistration.CurrentUserSid })
                    && captured.RootElement.GetProperty("WorkingDirectory").GetString() == root, "CLI uninstaller receives the selected data policy, silent mode, directory and owner");
                try
                {
                    using var worker = System.Diagnostics.Process.GetProcessById(captured.RootElement.GetProperty("ProcessId").GetInt32());
                    check(await Task.Run(() => worker.WaitForExit(5000)), "uninstaller test process exits before fixture cleanup");
                }
                catch (ArgumentException) { }
            }
            record.SetValue("InstallLocation", Path.Combine(root, "another-copy"));
            update.Invoke(view, null);
            check(!AppInstallService.IsInstalled && menu.Visibility == Visibility.Visible, "a portable copy displays the shortcut menu despite another directory being registered");
            check(await (Task<int>)uninstall.Invoke(null, new object[] { new Dictionary<string, string> { ["remove-data"] = "true" } })! != 0, "portable uninstall rejects data options instead of treating them as data deletion");
        }
        finally
        {
            Environment.SetEnvironmentVariable("EGL_UNINSTALL_TEST_CAPTURE", previousCapture);
            if (RegOverridePredefKey(currentUser, IntPtr.Zero) != 0) Environment.FailFast("Unable to restore the user registry after installation routing tests.");
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(isolation);
            foreach (string file in copied) File.Delete(file);
            File.Delete(capture);
            typeof(MainView).GetMethod("UpdateShortcutMenuState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
        }
    }

    internal static int Run(string root, string fixture)
    {
        int result = 1;
        var thread = new Thread(() =>
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(parameters =>
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    App? app = null;
                    app = new App(() => new UiSession(root, fixture, code => result = code, app!).Run());
                });
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(60))) return 1;
        return result;
    }

    private sealed class UiSession(string root, string fixture, Action<int> finish, App application)
    {
        internal async void Run()
        {
            MainWindow? window = null;
            int checks = 0;
            void Check(bool condition, string name)
            { if (!condition) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS UI " + name); }
            try
            {
                ConfigService.ApplyDebugMode(root);
                ConfigService.Initialize();
                Text.Load("EN");
                window = new MainWindow();
                window.PrepareOffscreenWarmup();
                window.StartInitialization();
                Check(await window.WaitStartupCompleteAsync(TimeSpan.FromSeconds(20)), "real XAML view completes startup");
                Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "ico.ico")), "window starts without an external default ICO");
                var windowIcon = (IntPtr)typeof(MainWindow).GetField("_customIconHandle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
                Check(windowIcon != IntPtr.Zero, "window receives a native icon handle from the embedded resource");
                var defaultIcon = await AppIconService.LoadBitmapImageAsync();
                Check(defaultIcon is { PixelWidth: > 0, PixelHeight: > 0 } && AppIconService.GetBitmapImage() != null, "embedded default icon decodes synchronously and asynchronously");
                Check(!File.Exists(Path.Combine(ConfigService.SystemCachePath, "EricGameLauncher_TempIcon.ico")) && !File.Exists(Path.Combine(ConfigService.SystemCachePath, "EricGameLauncher_TempIcon.png")), "default icon decoding creates no ICO or PNG cache");
                Check(!Directory.EnumerateFiles(AppContext.BaseDirectory, "*.pri", SearchOption.AllDirectories).Any(), "real XAML view starts without a PRI file");
                var xamlManager = EmbeddedXamlResources.CreateManager();
                var embeddedCandidate = xamlManager.MainResourceMap.GetValue("ms-resource://EricGameLauncher/Files/UI/MainView.xbf");
                Check(embeddedCandidate.Kind == Microsoft.Windows.ApplicationModel.Resources.ResourceCandidateKind.EmbeddedData && embeddedCandidate.ValueAsBytes.Take(4).SequenceEqual(new byte[] { 88, 66, 70, 0 }), "WinUI resolves compiled XAML directly from embedded bytes");
                Check(xamlManager.MainResourceMap.TryGetValue("ms-resource://other/Files/UI/MainView.xbf") == null && xamlManager.MainResourceMap.TryGetValue("Files/UI/Missing.xbf") == null, "XAML resolver preserves missing and foreign resources");
                var view = Find<MainView>((DependencyObject)window.Content)!;
                await InstallationRoutingAsync(Check, view, fixture);
                var storageSettings = new SettingsFlyoutControl();
                storageSettings.ApplyLocalization();
                storageSettings.Sync();
                var transferButton = (Button)storageSettings.FindName("BtnConfigurationTransfer");
                var dataActions = (StackPanel)storageSettings.FindName("StorageActionButtons");
                Check(transferButton.Visibility == Visibility.Visible && transferButton.Flyout == null
                    && dataActions.Children.Contains(transferButton)
                    && dataActions.Children.Contains((Button)storageSettings.FindName("BtnOpenConfigFolder"))
                    && dataActions.Children.Contains((Button)storageSettings.FindName("BtnOpenCacheFolder")), "one configuration transfer button shares the existing data action row");
                var transfer = storageSettings.CreateConfigurationDialog(view.XamlRoot);
                var transferActions = (Grid)((StackPanel)((ScrollViewer)transfer.Dialog.Content).Content).Children[0];
                Check(transferActions.Children.Count == 2 && transferActions.Children.Contains(transfer.Import)
                    && transferActions.Children.Contains(transfer.Export) && transfer.Dialog.PrimaryButtonText == "", "one modal contains import and export actions before an archive is selected");
                var transferOpened = new TaskCompletionSource();
                transfer.Dialog.Opened += (_, _) => transferOpened.TrySetResult();
                var transferOperation = transfer.Dialog.ShowAsync();
                await transferOpened.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(transfer.Import.XamlRoot == view.XamlRoot && transfer.Export.XamlRoot == view.XamlRoot, "configuration transfer opens as a modal in the current window");
                var conflictPanel = (StackPanel)((StackPanel)((ScrollViewer)transfer.Dialog.Content).Content).Children[4];
                var choices = (StackPanel)conflictPanel.Children[1];
                var explanation = (TextBlock)conflictPanel.Children[2];
                var replaceChoice = (RadioButton)choices.Children[0];
                var mergeChoice = (RadioButton)choices.Children[1];
                replaceChoice.IsChecked = true;
                Check(choices.Orientation == Orientation.Horizontal && explanation.Visibility == Visibility.Visible
                    && explanation.Text == Text.T("ConfigArchive_ReplaceDescription"), "selecting replace shows only its explanation below the shared choice row");
                replaceChoice.IsChecked = false;
                mergeChoice.IsChecked = true;
                Check(explanation.Text == Text.T("ConfigArchive_MergeDescription"), "selecting merge switches the explanation to the selected policy");
                transfer.Dialog.Hide();
                await transferOperation;
                var importSelection = SettingsFlyoutControl.CreateImportSelection();
                Check(importSelection.Settings.IsChecked == true && importSelection.Items.IsChecked == true
                    && ((StackPanel)importSelection.Content.Children[1]).Orientation == Orientation.Horizontal, "import presents settings and items-with-icons checkboxes on one row");
                importSelection.Settings.IsChecked = false;
                Check(importSelection.Items.IsChecked == true, "import section checkboxes support selecting only items and icons");
                var storageButton = (Button)storageSettings.FindName("BtnSwitchStorageMode");
                Check(storageButton.Visibility == Visibility.Visible, "writable installation displays the storage switch");
                var storageLock = typeof(ConfigService).GetField("_storageModeLocked", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                try
                {
                    storageLock.SetValue(null, true);
                    storageSettings.Sync();
                    Check(storageButton.Visibility == Visibility.Collapsed, "locked installation hides the storage switch");
                    Check(transferButton.Visibility == Visibility.Visible && transferButton.IsEnabled, "locked storage keeps configuration import and export available");
                }
                finally
                {
                    storageLock.SetValue(null, false);
                    storageSettings.Sync();
                }
                var panel = (PropertyPanelControl)view.FindName("PropertyControl");
                var closing = new MainWindow();
                closing.PrepareOffscreenWarmup();
                var closingRoot = (DependencyObject)closing.Content;
                closing.StartInitialization();
                closing.Close();
                await YieldAsync(view.DispatcherQueue);
                Check(Find<MainView>(closingRoot) == null, "closed window cannot create a delayed main view");
                var save = (Button)panel.FindName("PropSaveButton");
                var a = ItemService.CreateItem("UI A", "ui-a://launch");
                a.CustomMenuItems = Enumerable.Range(0, 11).Select(i => new CustomMenuItem { Title = "Menu" + i, Command = "menu://" + i }).ToList();
                ItemService.AddItem(a);
                panel.BeginEdit(a, false);
                ((TextBox)panel.FindName("PropTitle")).Text = "UI saved";
                var pending = new TaskCompletionSource();
                var entered = new TaskCompletionSource();
                panel.SaveOperation = async (baseline, draft, _) =>
                {
                    entered.TrySetResult();
                    await pending.Task;
                    await ItemService.SaveDraftAsync(baseline, draft);
                };
                ((IInvokeProvider)new ButtonAutomationPeer(save).GetPattern(PatternInterface.Invoke)).Invoke();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(!save.IsEnabled, "save event reaches production controller and disables duplicate submission");
                var b = ItemService.CreateItem("UI B", "ui-b://launch");
                panel.BeginEdit(b, true);
                pending.SetResult();
                await WaitAsync(() => ItemService.FindItem(a.Id, null)?.Title == "UI saved");
                await YieldAsync(view.DispatcherQueue);
                Check(panel.EditingItem?.Id == b.Id && save.IsEnabled, "A completion leaves actual B panel open and editable");
                Check(ItemService.FindItem(a.Id, null)!.CustomMenuItems.Count == 11, "actual input mapping and save preserve eleven menus");
                panel.SaveOperation = null;
                panel.ClosePanel();
                string output = Path.Combine(root, "keyboard-launch.json");
                var keyboard = ItemService.CreateItem("UI keyboard", "\"" + fixture + "\" --capture \"" + output + "\"");
                await ItemService.AddItemAsync(keyboard);
                ConfigService.SetSetting("launchmode", "double");
                var grid = (GridView)view.FindName("AppGrid");
                await WaitAsync(() =>
                {
                    grid.UpdateLayout();
                    return grid.Items.OfType<AppItem>().FirstOrDefault(x => x.Id == keyboard.Id) is AppItem entry && grid.ContainerFromItem(entry) is GridViewItem;
                });
                var container = (GridViewItem)grid.ContainerFromItem(grid.Items.OfType<AppItem>().First(x => x.Id == keyboard.Id));
                Check(!view.HandleActivationKey(new TextBox(), VirtualKey.Enter, false), "text input does not activate an item");
                Check(!view.HandleActivationKey(container, VirtualKey.Space, true), "repeated keyboard activation is suppressed");
                Check(view.HandleActivationKey(container, VirtualKey.Enter, false), "Enter activates a rendered item in double click mode");
                await WaitAsync(() => File.Exists(output));
                Check(File.ReadAllText(output).Contains("WorkingDirectory"), "keyboard path launches the fixture process");
                await Task.Delay(550);
                File.Delete(output);
                Check(view.HandleActivationKey(container, VirtualKey.Space, false), "Space activates a rendered item in double click mode");
                await WaitAsync(() => File.Exists(output));
                await Task.Delay(550);
                File.Delete(output);
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(container);
                Check(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "rendered item exposes accessibility Invoke in double click mode");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitAsync(() => File.Exists(output));
                Check(ItemService.FindItem(keyboard.Id, null)!.LaunchCount >= 3, "accessibility Invoke reaches the shared launch path");
                ItemService.RemoveItem(a.Id, null, true);
                ItemService.RemoveItem(keyboard.Id, null, true);
                Console.WriteLine($"{checks} checks passed.");
                finish(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); finish(1); }
            finally { window?.Close(); application.Exit(); }
        }

        private static async Task WaitAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!condition()) await Task.Delay(20, timeout.Token);
        }

        private static Task YieldAsync(DispatcherQueue queue)
        {
            var complete = new TaskCompletionSource();
            queue.TryEnqueue(DispatcherQueuePriority.Low, () => complete.SetResult());
            return complete.Task;
        }

        private static T? Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T found) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (Find<T>(VisualTreeHelper.GetChild(root, i)) is T child) return child;
            return null;
        }
    }
}
