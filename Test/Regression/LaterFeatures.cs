using EricGameLauncher;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

internal static class LaterFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root, string fixture)
    {
        check(!LaunchService.LaunchCustomCommand(new() { Command = Path.Combine(root, "missing-custom.exe") }).Success, "P4 custom launch reports missing target");
        check(!(await LaunchService.LaunchCustomCommandAsync(new() { Command = "" })).Success, "P4 asynchronous custom launch returns failure");
        string command = $"\"{fixture}\" --capture \"{Path.Combine(root, "unused-capture.json")}\"";
        check(ProcessRunner.FileTarget(command) == fixture && ItemService.SupportsIconExtraction(command), "P4 file helpers parse executable and arguments consistently");
        check(ItemService.SuggestTitle(command) == Path.GetFileNameWithoutExtension(fixture), "P4 title suggestion excludes arguments");
        var iconItem = ItemService.CreateItem("P4 icon command", command);
        await ItemService.AddItemAsync(iconItem);
        check(File.Exists(ItemService.FindItem(iconItem.Id, null)!.IconPath), "P4 quoted command extracts an icon");
        ItemService.RemoveItem(iconItem.Id, null, true);
        await DraftsAsync(check, root);
        await PlatformsAsync(check, root);
        await AnnouncementsAsync(check, root);
        await QueriesAsync(check, cli, root);
        ImportFailures(check, root);
        ShortcutFailures(check, root, fixture);
        MigrationRecovery(check, root);
        CacheOwnership(check);
        await PublicationLockAsync(check, root);
    }

    private static async Task DraftsAsync(Action<bool, string> check, string root)
    {
        var draft = new PropertyDraftSession();
        var first = new TaskCompletionSource<string?>();
        var second = new TaskCompletionSource<string?>();
        string oldIcon = Path.Combine(root, "old-request.png");
        string newIcon = Path.Combine(root, "new-request.png");
        File.WriteAllText(oldIcon, "old");
        File.WriteAllText(newIcon, "new");
        Task<string?> oldRequest = draft.TrackAsync(() => first.Task);
        Task<string?> newRequest = draft.TrackAsync(() => second.Task);
        second.SetResult(newIcon);
        check(await newRequest == newIcon, "P4 newest draft icon request completes");
        first.SetResult(oldIcon);
        check(await oldRequest == null && !File.Exists(oldIcon), "P4 old icon completion cannot replace new choice");
        draft.Advance();
        check(!File.Exists(newIcon), "P4 draft close removes temporary icon");
    }

    private static async Task PlatformsAsync(Action<bool, string> check, string root)
    {
        var item = ItemService.CreateItem("P4 platform draft", "steam://rungameid/570");
        await ItemService.AddItemAsync(item);
        var baseline = ItemService.Snapshot(item);
        var draft = ItemService.Snapshot(item);
        draft.ExePath = Path.Combine(root, "local-platform-fixture.txt");
        File.WriteAllText(draft.ExePath, "fixture");
        await ItemService.SaveDraftAsync(baseline, draft);
        check(ItemService.FindItem(item.Id, null)!.Platform == "User", "P4 save derives platform from current command instead of badge");
        baseline = ItemService.Snapshot(draft);
        draft.Platform = "Custom Library";
        await ItemService.SaveDraftAsync(baseline, draft);
        baseline = ItemService.Snapshot(draft);
        draft.ExePath = "steam://rungameid/571";
        await ItemService.SaveDraftAsync(baseline, draft);
        check(ItemService.FindItem(item.Id, null)!.Platform == "Custom Library", "P4 custom platform survives path edits");
        ItemService.RemoveItem(item.Id, null, true);
        check(GamePlatformHelper.GetRuntimeManagerPath(null, "shell:AppsFolder\\ordinary!App") == null, "P4 ordinary UWP app has no guessed Xbox manager");
        check(GamePlatformHelper.GetRuntimeManagerPath(null, "shell:AppsFolder\\game!App", "Xbox") != null, "P4 confirmed Xbox identity resolves manager");
        check(GamePlatformHelper.GetRuntimeManagerPath("custom-manager.exe", "googleplaygames://launch/?id=one") == "custom-manager.exe", "P4 custom manager has priority");
        check(GamePlatformHelper.GetRuntimeManagerPath(null, "googleplaygames://launch/?id=one") == null, "P4 Google manager capability stays absent without a verified target");
    }

    private static async Task AnnouncementsAsync(Action<bool, string> check, string root)
    {
        using var good = new HttpClient(new MockHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("announcements:\n- id: p4-fixture\n  title_en: Fixture\n") }));
        await ServerConfigManager.FetchConfigAsync(good, "http://fixture/");
        var snapshot = ServerConfigManager.CurrentConfig;
        check(snapshot != null, "P4 announcements publish a successful parsed snapshot");
        foreach (string body in new[] { "announcements: [broken", "" })
        {
            using var bad = new HttpClient(new MockHttp(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
            bool failed = false;
            try { await ServerConfigManager.FetchConfigAsync(bad, "http://fixture/"); }
            catch { failed = true; }
            check(failed && ReferenceEquals(snapshot, ServerConfigManager.CurrentConfig), "P4 invalid announcement response preserves last snapshot");
        }
        using var offline = new HttpClient(new MockHttp(_ => throw new HttpRequestException("offline fixture")));
        bool offlineFailed = false;
        try { await ServerConfigManager.FetchConfigAsync(offline, "http://fixture/"); }
        catch (HttpRequestException) { offlineFailed = true; }
        check(offlineFailed && ReferenceEquals(snapshot, ServerConfigManager.CurrentConfig), "P4 offline announcement refresh returns failure and retains snapshot");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bool cancelledFailed = false;
        try { await ServerConfigManager.FetchConfigAsync(good, "http://fixture/", cancelled.Token); }
        catch (OperationCanceledException) { cancelledFailed = true; }
        check(cancelledFailed && ReferenceEquals(snapshot, ServerConfigManager.CurrentConfig), "P4 cancelled announcements retain snapshot");

        ServerConfigManager.LoadReadIds();
        string first = "P5-parent-" + Guid.NewGuid().ToString("N");
        string second = "P5-child-" + Guid.NewGuid().ToString("N");
        var start = P2Features.ChildStartInfo();
        foreach (string argument in new[] { "--mark-read", root, second }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await Task.WhenAll(output, error);
            check(child.ExitCode == 0, "P5 second process writes a read marker");
            ServerConfigManager.MarkAsRead(first);
            ServerConfigManager.LoadReadIds();
            check(ServerConfigManager.IsRead(first) && ServerConfigManager.IsRead(second), "P5 stale parent read markers merge child changes");
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } }
        string before = File.ReadAllText(ServerConfigManager.ReadIdsPath);
        bool writeFailed = false;
        using (var locked = new FileStream(ServerConfigManager.ReadIdsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { ServerConfigManager.MarkAsRead("P5-failed-marker"); }
            catch (IOException) { writeFailed = true; }
        }
        check(writeFailed && File.ReadAllText(ServerConfigManager.ReadIdsPath) == before && !ServerConfigManager.IsRead("P5-failed-marker"), "P5 marker write failure preserves disk and memory");
    }

    private static async Task QueriesAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        string settings = File.ReadAllText(ConfigService.SettingsFilePath);
        try
        {
            foreach (string language in new[] { "EN", "Zh-CN", "system" })
            {
                check((await cli(["settings", "--set", "lang=" + language, "--json"])).Code == 0, "P4 language setting persists " + language);
                ConfigService.Reload();
                string expected = language == "system" ? Text.DetectSystemLanguage() : language;
                var result = await cli(["settings", "--get", "lang", "--json"]);
                using var json = JsonDocument.Parse(result.Output);
                check(result.Code == 0 && json.RootElement.GetProperty("value").GetString() == expected && ConfigService.Language == expected && Text.CurrentLanguage == expected, "P4 language is queryable after independent process restart " + language);
            }
        }
        finally { File.WriteAllText(ConfigService.SettingsFilePath, settings); ConfigService.Reload(); }
        var item = ItemService.CreateItem("P7 JSON fixture", Path.Combine(root, "p7-json.exe"));
        item.MgrPath = "manager.exe";
        item.IsMgrAdmin = true;
        item.AlternativeLaunchCommand = "alt.exe";
        item.IsAltAdmin = true;
        item.AlongsideCommand = "alongside.exe";
        item.IsAlongsideAdmin = true;
        ItemService.AddItem(item);
        ItemService.RemoveItem(item.Id, null);
        ItemService.MarkPendingDeletion(item.Id);
        var text = await cli(["list", "--recycle"]);
        check(text.Code == 0 && !text.Output.Contains("{0}"), "P5 recycle text formats pending deletion countdown");
        var query = await cli(["list", "--recycle", "--json"]);
        using (var json = JsonDocument.Parse(query.Output))
        {
            var entry = json.RootElement.GetProperty("recycleBinItems").EnumerateArray().Single(x => x.GetProperty("Id").GetString() == item.Id);
            check(entry.GetProperty("IsMgrAdmin").GetBoolean() && entry.GetProperty("IsAltAdmin").GetBoolean() && entry.GetProperty("IsAlongsideAdmin").GetBoolean(), "P7 item query exposes all launch privileges");
            check(entry.GetProperty("DeletedAt").GetDateTimeOffset().AddHours(72) == entry.GetProperty("deletionDeadline").GetDateTimeOffset(), "P7 item query exposes deletion timestamp and deadline");
        }
        ItemService.EmptyRecycle();
        var boundary = ItemService.CreateItem("Timestamp boundary", "boundary://fixture");
        boundary.DeletedAt = DateTimeOffset.MaxValue;
        check(ItemQuery.FromItem(boundary).DeletionDeadline == DateTimeOffset.MaxValue, "P7 extreme legacy timestamp has a safe query deadline");
        boundary.DeletedAt = new DateTimeOffset(9999, 12, 29, 0, 0, 0, TimeSpan.FromHours(14));
        check(ItemQuery.FromItem(boundary).DeletionDeadline == boundary.DeletedAt.Value.UtcDateTime.AddHours(72), "P7 positive-offset boundary deadline is calculated safely in UTC");
        using (var locked = new FileStream(ServerConfigManager.ReadIdsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var unavailable = await cli(["announcements", "--json"]);
            using var json = JsonDocument.Parse(unavailable.Output);
            check(unavailable.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean(), "P5 unavailable read markers cause explicit CLI query failure");
        }
    }

    private static void ImportFailures(Action<bool, string> check, string root)
    {
        var games = new List<ScannedGame> { new() { Title = "P4 import", ExePath = Path.Combine(root, "p4-import-missing.exe") } };
        string before = File.ReadAllText(ConfigService.ItemsFilePath);
        bool failed = false;
        using (var locked = new FileStream(ConfigService.ItemsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { ItemService.ImportGamesDetailed(games); }
            catch (IOException) { failed = true; }
        }
        check(failed && File.ReadAllText(ConfigService.ItemsFilePath) == before, "P4 failed scan import returns failure without completed rows");
        var imported = ItemService.ImportGamesDetailed(games);
        var repeated = ItemService.ImportGamesDetailed(games);
        check(imported.Changed == 1 && repeated.Changed == 0 && repeated.CompletedPaths.Contains(games[0].ExePath), "P4 scan import retry is idempotent and reports completed paths");
        var item = ItemService.FindItem(null, "P4 import")!;
        games[0].ItemId = item.Id;
        var deleted = ScanService.DeleteInvalidGamesDetailed(games);
        check(deleted.Changed == 1 && deleted.CompletedPaths.Contains(item.ExePath!), "P4 invalid deletion reports only completed candidates");
        ItemService.EmptyRecycle();
    }

    private static void ShortcutFailures(Action<bool, string> check, string root, string fixture)
    {
        string[] paths = [Path.Combine(root, "install-desktop.lnk"), Path.Combine(root, "install-start.lnk")];
        foreach (string path in paths) File.WriteAllText(path, "original shortcut");
        foreach (string phase in new[] { "create:", "install:" })
        {
            bool failed = false;
            try { AppInstallService.InstallShortcuts(fixture, paths, step => { if (step == phase + paths[1]) throw new IOException("injected shortcut failure"); }); }
            catch (IOException) { failed = true; }
            check(failed && paths.All(x => File.ReadAllText(x) == "original shortcut"), "P4 partial shortcut failure preserves existing entries " + phase);
        }
        AppInstallService.InstallShortcuts(fixture, paths);
        check(paths.All(x => ShortcutResolver.GetLnkTarget(x) == fixture), "P4 shortcut installation commits both validated entries");
    }

    private static void MigrationRecovery(Action<bool, string> check, string root)
    {
        string source = Path.Combine(root, "p6-source"), destination = Path.Combine(root, "p6-destination"), mode = Path.Combine(root, "p6-mode");
        Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(source, "settings.yaml"), "appTitle: New\n");
        File.WriteAllText(Path.Combine(source, "items.yaml"), "version: 1\nitems: []\nrecycleBin: []\n");
        File.WriteAllText(Path.Combine(destination, "settings.yaml"), "appTitle: Old\n");
        bool failed = false;
        try { StorageMigration.Transfer(source, destination, true, step => { if (step == "commit" || step.StartsWith("rollback:")) throw new IOException("injected migration interruption"); }, mode); }
        catch (IOException) { failed = true; }
        check(failed && Directory.GetDirectories(destination, ".migration-backup-*").Length == 1, "P6 failed migration rollback retains recoverable journal");
        StorageMigration.Transfer(source, destination, true, modeFile: mode);
        check(Directory.GetDirectories(destination, ".migration-backup-*").Length == 0 && File.ReadAllText(mode) == "system", "P6 next migration recovers and cleans completed backups");
    }

    private static void CacheOwnership(Action<bool, string> check)
    {
        string path = Path.Combine(ConfigService.FixedCachePath, "owned-cover-fixture.png");
        File.WriteAllText(path, "owned fixture");
        IconCacheMaintenance.Register(path);
        var first = ItemService.CreateItem("P6 shared one", "p6-shared://one", icon: path);
        var second = ItemService.CreateItem("P6 shared two", "p6-shared://two", icon: path);
        ItemService.AddItem(first); ItemService.AddItem(second);
        ItemService.RemoveItem(first.Id, null, true);
        check(File.Exists(path), "P6 cache sweep retains shared cover references");
        ItemService.RemoveItem(second.Id, null);
        check(File.Exists(path), "P6 recycle bin retains its cover");
        ItemService.EmptyRecycle();
        check(!File.Exists(path), "P6 unreferenced owned cover is reclaimed");
        File.WriteAllText(path, "draft fixture");
        IconCacheMaintenance.Register(path);
        var draft = new PropertyDraftSession();
        draft.Protect(path);
        IconCacheMaintenance.Referenced([path]);
        check(File.Exists(path), "P6 active draft pins an otherwise unreferenced cover");
        draft.Advance();
        check(!File.Exists(path), "P6 closed draft releases its cover for collection");
        string unknown = Path.Combine(ConfigService.FixedCachePath, "draft-user-selected.png");
        File.WriteAllText(unknown, "user-selected fixture");
        IconCacheMaintenance.Sweep();
        check(File.Exists(unknown), "P6 cache cleanup preserves files without ownership records");
        File.WriteAllText(path, "pending fixture");
        IconCacheMaintenance.Register(path);
        IconCacheMaintenance.Sweep();
        check(File.Exists(path), "P6 running producer pins an unpublished icon");
        IconCacheMaintenance.JumpReferences([path]);
        check(File.Exists(path), "P6 published Jump List icon remains referenced");
        IconCacheMaintenance.JumpReferences([]);
        check(!File.Exists(path), "P6 superseded Jump List icon is reclaimed");
        File.WriteAllText(path, "locked fixture");
        IconCacheMaintenance.Register(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            IconCacheMaintenance.Referenced([path]);
            check(File.Exists(path), "P6 interrupted cleanup keeps in-use icon");
        }
        IconCacheMaintenance.Sweep();
        check(!File.Exists(path), "P6 cleanup retries after icon lock is released");
        for (int i = 0; i < 32; i++)
        {
            string repeated = Path.Combine(ConfigService.FixedCachePath, "owned-cycle-" + i + ".png");
            File.WriteAllText(repeated, "cycle fixture");
            IconCacheMaintenance.Register(repeated);
            IconCacheMaintenance.Referenced([repeated]);
        }
        check(Directory.GetFiles(ConfigService.FixedCachePath, "owned-cycle-*").Length == 0, "P6 repeated cover replacement leaves no unused owned files");
        string oldJumpIcon = Path.Combine(ConfigService.FixedCachePath, "old-jump-fixture.ico");
        string newJumpIcon = Path.Combine(ConfigService.FixedCachePath, "new-jump-fixture.ico");
        File.WriteAllText(oldJumpIcon, "old");
        File.WriteAllText(newJumpIcon, "new");
        IconCacheMaintenance.Register(oldJumpIcon);
        IconCacheMaintenance.Register(newJumpIcon);
        IconCacheMaintenance.JumpReferences([oldJumpIcon]);
        IconCacheMaintenance.ProtectJumpReferences([newJumpIcon]);
        IconCacheMaintenance.Referenced([newJumpIcon]);
        check(File.Exists(oldJumpIcon) && File.Exists(newJumpIcon), "P6 interrupted Jump List publication preserves old and proposed icons");
        IconCacheMaintenance.JumpReferences([newJumpIcon]);
        check(!File.Exists(oldJumpIcon) && File.Exists(newJumpIcon), "P6 completed Jump List publication retires only superseded icons");
        IconCacheMaintenance.JumpReferences([]);
        string ledger = Path.Combine(ConfigService.FixedCachePath, ".owned-icons.json");
        using (var locked = new FileStream(ledger, FileMode.Open, FileAccess.Read, FileShare.Read))
            check(!IconCacheMaintenance.ProtectJumpReferences([newJumpIcon]), "P6 publication protection reports metadata write failure");
    }

    private static async Task PublicationLockAsync(Action<bool, string> check, string root)
    {
        string ready = Path.Combine(root, "publication-ready");
        var start = P2Features.ChildStartInfo();
        foreach (string argument in new[] { "--hold-publication", root, ready }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(ready))
            {
                if (child.HasExited) throw new InvalidOperationException(await error);
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Publication owner failed to start");
                await Task.Delay(20);
            }
            bool blocked = false;
            try { using var competing = JumpListService.EnterPublication(TimeSpan.Zero); }
            catch (TimeoutException) { blocked = true; }
            check(blocked, "P6 concurrent processes serialize Jump List publication and cache metadata");
            File.WriteAllText(ready + ".release", "release");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            using var acquired = JumpListService.EnterPublication(TimeSpan.Zero);
            check(child.ExitCode == 0, "P6 Jump List publication lease is reusable after owner exit");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            await Task.WhenAll(output, error);
        }
    }
}
