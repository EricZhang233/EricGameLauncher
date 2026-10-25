using EricGameLauncher;

internal static class CoreGapFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        ClearItems();
        PathHashTests(check);
        TextTests(check);
        NormalizeStateTests(check);
        HasChangedTests(check);
        SaveOrderTests(check);
        ScanInvalidTests(check, root);
        await MigrationTestsAsync(check);
        QuickStartTests(check);
        PlatformTests(check);
        HostPipeTests(check);
        ClearItems();
        Text.Load("EN");
        ConfigService.Reload();
    }

    private static void ClearItems() => ConfigService.MutateItems((items, recycled) =>
    {
        items.Clear();
        recycled.Clear();
    });

    private static void PathHashTests(Action<bool, string> check)
    {
        string path = @"C:\Games\Sample Game.exe";
        string hash = PathHashHelper.GetPathHash(path);
        check(hash.Length == 16, "path hash uses 16 hex characters");
        check(PathHashHelper.VerifyPathHash(path, hash), "path hash verification accepts the matching hash");
        check(PathHashHelper.VerifyPathHash(path.ToUpperInvariant().Replace('\\', '/'), hash), "path hash verification ignores case and slash direction");
        check(!PathHashHelper.VerifyPathHash(path + ".copy", hash), "path hash verification rejects a different path");
        check(PathHashHelper.PathKey("C:/Games/Sample Game.exe") == PathHashHelper.PathKey(@"c:\games\SAMPLE GAME.EXE"), "path key normalizes case and separators");
        check(PathHashHelper.GetPathHash("") != PathHashHelper.GetPathHash(""), "empty path hash generates a fresh identity each call");
        Environment.SetEnvironmentVariable("EGL_PATH_KEY_DIR", @"C:\Games");
        try { check(PathHashHelper.PathKey("%EGL_PATH_KEY_DIR%\\Sample Game.exe") == PathHashHelper.PathKey(path), "path key expands environment variables"); }
        finally { Environment.SetEnvironmentVariable("EGL_PATH_KEY_DIR", null); }
    }

    private static void TextTests(Action<bool, string> check)
    {
        var languages = Text.GetAvailableLanguages();
        check(languages.Contains("Zh-CN") && languages.Contains("EN"), "available languages expose Chinese and English");
        string detected = Text.DetectSystemLanguage();
        check(detected == "Zh-CN" || detected == "EN", "system language detection returns a supported code");

        Text.Load("Zh-CN");
        check(Text.T("DataFile_Error") != "DataFile_Error", "Chinese lookup resolves a known key");
        check(Text.GetDisplayName("Zh-CN") == "简体中文", "Chinese display name resolves the native language name");

        Text.Load("EN");
        check(Text.T("DataFile_Error") != "DataFile_Error", "English lookup resolves a known key");
        check(Text.GetDisplayName("EN") == "English", "English display name resolves the localized language name");
        check(Text.T("__EGL_missing_key__") == "__EGL_missing_key__", "missing text key falls back to the key itself");
        check(Text.Cli("__EGL_missing_cli_key__") == "__EGL_missing_cli_key__", "missing CLI text key falls back to the key itself");
    }

    private static void NormalizeStateTests(Action<bool, string> check)
    {
        var normal = ItemService.CreateItem("Normalize Normal", @"C:\egl-normalize-normal.exe");
        var misplaced = ItemService.CreateItem("Normalize Misplaced", @"C:\egl-normalize-misplaced.exe");
        misplaced.Status = (int)AppItemStatus.Recycled;
        var recycleOk = ItemService.CreateItem("Normalize Recycle Ok", @"C:\egl-normalize-recycle-ok.exe");
        recycleOk.Status = (int)AppItemStatus.Recycled;
        recycleOk.RecycleId = "egl-recycle-ok";
        var missingId = ItemService.CreateItem("Normalize Missing Id", @"C:\egl-normalize-missing-id.exe");
        missingId.Status = (int)AppItemStatus.Recycled;
        missingId.RecycleId = null;
        var duplicateId = ItemService.CreateItem("Normalize Duplicate Id", @"C:\egl-normalize-duplicate-id.exe");
        duplicateId.Status = (int)AppItemStatus.Recycled;
        duplicateId.RecycleId = "egl-recycle-ok";

        var unchanged = ItemService.NormalizeState(new List<AppItem> { normal }, new List<AppItem> { recycleOk });
        check(!unchanged.changed && unchanged.normalItems.Count == 1 && unchanged.recycleItems.Count == 1, "normalize state keeps a consistent library unchanged");

        var normalized = ItemService.NormalizeState(new List<AppItem> { normal, misplaced }, new List<AppItem> { missingId, duplicateId });
        check(normalized.changed, "normalize state reports changes for misplaced entries and recycled identities");
        check(normalized.normalItems.Count == 1 && normalized.normalItems[0].Id == normal.Id, "normalize state drops recycled entries from the active list");
        check(normalized.recycleItems.Count == 3 && normalized.recycleItems.Any(x => x.Id == misplaced.Id), "normalize state moves misplaced entries into the recycle bin");
        check(normalized.recycleItems.All(x => x.Status == (int)AppItemStatus.Recycled), "normalize state marks every recycle entry as recycled");
        check(normalized.recycleItems.All(x => !string.IsNullOrEmpty(x.RecycleId)), "normalize state assigns missing recycle ids");
        check(normalized.recycleItems.Select(x => x.RecycleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "normalize state keeps recycle ids unique");

        var persistedItems = ConfigService.LoadItems();
        var persistedRecycle = ConfigService.LoadRecycleBinItems();
        check(persistedItems.Count == 1 && persistedItems[0].Id == normal.Id, "normalize state persists the normalized active list");
        check(persistedRecycle.Count == 3 && persistedRecycle.All(x => x.Status == (int)AppItemStatus.Recycled), "normalize state persists the normalized recycle bin");
        ClearItems();
    }

    private static void HasChangedTests(Action<bool, string> check)
    {
        var first = ItemService.CreateItem("Change First", @"C:\egl-change-first.exe");
        var second = ItemService.CreateItem("Change Second", @"C:\egl-change-second.exe");
        var recycled = ItemService.CreateItem("Change Recycled", @"C:\egl-change-recycled.exe");
        recycled.Status = (int)AppItemStatus.Recycled;
        recycled.RecycleId = "egl-change-recycle";

        check(!ItemService.HasChanged(new List<AppItem> { first }, new List<AppItem> { first }, new List<AppItem>(), new List<AppItem>()), "change detector accepts identical lists");
        var renamed = ItemService.CreateItem("Change First", @"C:\egl-change-first.exe");
        renamed.Title = "Change Renamed";
        check(ItemService.HasChanged(new List<AppItem> { first }, new List<AppItem> { renamed }, new List<AppItem>(), new List<AppItem>()), "change detector reports a changed item field");
        check(ItemService.HasChanged(new List<AppItem>(), new List<AppItem>(), new List<AppItem>(), new List<AppItem> { recycled }), "change detector reports recycle bin changes");
        check(ItemService.HasChanged(new List<AppItem> { first, second }, new List<AppItem> { second, first }, new List<AppItem>(), new List<AppItem>()), "change detector reports reordered items");
    }

    private static void SaveOrderTests(Action<bool, string> check)
    {
        var one = ItemService.CreateItem("Order One", @"C:\egl-order-one.exe");
        var two = ItemService.CreateItem("Order Two", @"C:\egl-order-two.exe");
        var three = ItemService.CreateItem("Order Three", @"C:\egl-order-three.exe");
        var four = ItemService.CreateItem("Order Four", @"C:\egl-order-four.exe");
        ConfigService.MutateItems((items, _) =>
        {
            items.Clear();
            items.AddRange(new[] { one, two, three, four });
        });

        ItemService.SaveOrder(new List<AppItem> { four, three, two, one });
        var reloaded = ConfigService.LoadItems();
        check(reloaded.Select(x => x.Id).SequenceEqual(new[] { four.Id, three.Id, two.Id, one.Id }), "saved order persists a full reorder");

        ItemService.SaveVisibleOrder(new[] { one.Id, three.Id });
        var subset = ConfigService.LoadItems();
        check(subset.Select(x => x.Id).SequenceEqual(new[] { four.Id, one.Id, two.Id, three.Id }), "saved visible order reorders only the supplied entries");
        ClearItems();
    }

    private static void ScanInvalidTests(Action<bool, string> check, string root)
    {
        string existing = Path.Combine(root, "scan-existing.exe");
        File.WriteAllText(existing, "stub");
        string missingUser = @"C:\egl-scan-missing-user.exe";
        string missingSteam = @"C:\egl-scan-missing-steam.exe";
        string installedSteam = @"C:\egl-scan-installed-steam.exe";
        var userInvalid = ItemService.CreateItem("Scan User Invalid", missingUser);
        var userValid = ItemService.CreateItem("Scan User Valid", existing);
        var protocol = ItemService.CreateItem("Scan Protocol", "egl-test://run/1");
        var steamGone = ItemService.CreateItem("Scan Steam Gone", missingSteam, platform: "Steam");
        var steamFound = ItemService.CreateItem("Scan Steam Found", installedSteam, platform: "Steam");
        var scanned = new List<ScannedGame> { new() { Title = "Scan Steam Found", ExePath = installedSteam, PlatformBadge = "Steam" } };

        var invalid = ScanService.FindInvalidGames(new List<AppItem> { userInvalid, userValid, protocol, steamGone, steamFound }, scanned, true, false);
        check(invalid.Any(x => x.ItemId == userInvalid.Id && x.PlatformBadge == "User"), "invalid scan flags a missing user target");
        check(!invalid.Any(x => x.ItemId == userValid.Id), "invalid scan keeps an existing user target");
        check(!invalid.Any(x => x.ItemId == protocol.Id), "invalid scan ignores protocol launches");
        check(invalid.Any(x => x.ItemId == steamGone.Id && x.PlatformBadge == "Steam"), "invalid scan flags a steam target that disappeared");
        check(!invalid.Any(x => x.ItemId == steamFound.Id), "invalid scan keeps a steam target reported by the scan");
        var invalidWithoutSteam = ScanService.FindInvalidGames(new List<AppItem> { steamGone }, scanned, false, false);
        check(!invalidWithoutSteam.Any(x => x.ItemId == steamGone.Id), "invalid scan skips steam validation when steam is unavailable");

        var steamUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { installedSteam };
        check(ScanService.IsItemStillInvalid(userInvalid, false, false, steamUrls, new HashSet<string>()), "still invalid for a missing user target");
        check(!ScanService.IsItemStillInvalid(userValid, false, false, steamUrls, new HashSet<string>()), "still valid for an existing user target");
        check(!ScanService.IsItemStillInvalid(steamGone, false, false, steamUrls, new HashSet<string>()), "still valid when steam cannot be validated");
        check(ScanService.IsItemStillInvalid(steamGone, true, false, steamUrls, new HashSet<string>()), "still invalid when steam can validate and the path is gone");
        check(!ScanService.IsItemStillInvalid(steamFound, true, false, steamUrls, new HashSet<string>()), "still valid when steam reports the installed path");

        ItemService.AddItem(userInvalid);
        ItemService.AddItem(steamGone);
        var toDelete = new List<ScannedGame>
        {
            new() { ItemId = userInvalid.Id, Title = userInvalid.Title ?? "", ExePath = missingUser, PlatformBadge = "User" },
            new() { ItemId = userInvalid.Id, Title = userInvalid.Title ?? "", ExePath = missingUser, PlatformBadge = "User" },
            new() { ItemId = steamGone.Id, Title = steamGone.Title ?? "", ExePath = missingSteam, PlatformBadge = "Steam" }
        };
        int removed = ScanService.DeleteInvalidGames(toDelete);
        check(removed == 2, "invalid scan deletion removes each invalid item once despite duplicate rows");
        check(ItemService.FindItem(userInvalid.Id, null) == null && ItemService.FindItem(steamGone.Id, null) == null, "invalid scan deletion removes them from the active list");
        ClearItems();
    }

    private static async Task MigrationTestsAsync(Action<bool, string> check)
    {
        string itemsPath = ConfigService.ItemsFilePath;
        string backup = File.Exists(itemsPath) ? File.ReadAllText(itemsPath) : "";
        try
        {
            var keep = ItemService.CreateItem("Migration Keep", @"C:\egl-migration-keep.exe");
            ItemService.AddItem(keep);
            await MigrationService.RunMigrationAsync();
            check(ConfigService.LoadItems().Any(x => x.Id == keep.Id), "migration keeps items after a successful run");

            File.WriteAllText(itemsPath, "version: 0\nitems: []\nrecycleBin: []\n");
            bool threw = false;
            try { await MigrationService.RunMigrationAsync(); }
            catch (DataFileException) { threw = true; }
            check(threw, "migration surfaces a data file error when the version cannot be upgraded");
        }
        finally
        {
            File.WriteAllText(itemsPath, backup);
            ConfigService.Reload();
            ClearItems();
        }
    }

    private static void QuickStartTests(Action<bool, string> check)
    {
        string executable = QuickStartService.ResolveMainExecutable();
        check(!string.IsNullOrEmpty(executable) && File.Exists(executable), "quick start resolves an existing executable");
        string command = QuickStartService.BuildAutoStartCommand();
        check(command == $"\"{executable}\" {QuickStartService.ServiceArgument}", "quick start command quotes the executable and appends the service argument");
        check(QuickStartService.IsAutoStartRegistered() == !string.IsNullOrEmpty(QuickStartService.GetRegisteredCommand()), "auto start registration state matches the registry value");
    }

    private static void PlatformTests(Action<bool, string> check)
    {
        check(GamePlatformHelper.DetectPlatform("steam://rungameid/570")?.PlatformName == "Steam", "platform detection recognizes steam urls");
        check(GamePlatformHelper.DetectPlatform("com.epicgames.launcher://apps/EpicApp?action=launch")?.PlatformName == "Epic Games", "platform detection recognizes epic urls");
        check(GamePlatformHelper.DetectPlatform("xbox://launch/12345")?.PlatformName == "Xbox", "platform detection recognizes xbox urls");
        check(GamePlatformHelper.DetectPlatform(@"C:\Games\Game.exe") == null, "platform detection ignores local executables");
        check(GamePlatformHelper.GetPlatformDisplayName("steam://rungameid/570") == "Steam", "platform display name resolves for a supported url");
        check(GamePlatformHelper.IsSupportedPlatformUrl("steam://rungameid/570"), "supported platform url accepts steam");
        check(!GamePlatformHelper.IsSupportedPlatformUrl(@"C:\Games\Game.exe"), "supported platform url rejects local executables");
        check(!StoreHelper.IsAppInstalled("shell:AppsFolder\\EGL.Missing.App_1!None"), "store helper reports a missing app as not installed");
    }

    private static void HostPipeTests(Action<bool, string> check)
    {
        if (!ServiceHost.TryAcquireHost())
        {
            check(true, "service host mutex is owned by another process, pipe coverage stays with the manual matrix");
            return;
        }

        bool woke = false, exited = false;
        ServiceHost.StartServer(() => woke = true, () => exited = true);
        try
        {
            check(ServiceHost.IsHostRunning(), "service host mutex reports a running host");
            check(ServiceHost.NotifyWake(), "service host wake signal is delivered");
            for (int i = 0; i < 100 && !woke; i++) Thread.Sleep(20);
            check(woke, "service host wake callback runs");
            check(ServiceHost.NotifyExit(), "service host exit signal is delivered");
            for (int i = 0; i < 100 && !exited; i++) Thread.Sleep(20);
            check(exited, "service host exit callback runs");
        }
        finally { ServiceHost.StopServer(); }
        Thread.Sleep(300);
        check(!ServiceHost.NotifyWake(), "service host wake signal fails when no server is listening");
    }
}
