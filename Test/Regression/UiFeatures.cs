using EricGameLauncher;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VirtualKey = Windows.System.VirtualKey;

internal static class UiFeatures
{
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
                var view = Find<MainView>((DependencyObject)window.Content)!;
                var storageSettings = new SettingsFlyoutControl();
                storageSettings.ApplyLocalization();
                storageSettings.Sync();
                var storageButton = (Button)storageSettings.FindName("BtnSwitchStorageMode");
                Check(storageButton.Visibility == Visibility.Visible, "writable installation displays the storage switch");
                var storageLock = typeof(ConfigService).GetField("_storageModeLocked", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                try
                {
                    storageLock.SetValue(null, true);
                    storageSettings.Sync();
                    Check(storageButton.Visibility == Visibility.Collapsed, "locked installation hides the storage switch");
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
