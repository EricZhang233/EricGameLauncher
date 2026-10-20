using EricGameLauncher;
using System.Text.Json;

internal static class LegacyFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        var a = ItemService.CreateItem("原神 Alpha", "C:\\EGL-legacy-a.exe");
        var b = ItemService.CreateItem("Beta", "C:\\EGL-legacy-b.exe");
        var c = ItemService.CreateItem("Gamma", "C:\\EGL-legacy-c.exe");
        var pool = new List<AppItem> { a, b, c };
        check(ItemService.Search(pool, "ALPHA").Single() == a, "search ignores title case");
        check(ItemService.Search(pool, "原神").Single() == a, "search matches Chinese title");
        check(ItemService.Search(pool, "yuanshen").Single() == a, "search matches full pinyin");
        check(ItemService.Search(pool, "ys").Single() == a, "search matches pinyin initials");
        check(ItemService.Search(pool, "legacy-b.exe").Single() == b, "search matches launch path");
        check(ItemService.Search(pool, " ").Count == 0 && ItemService.Search(pool, "absent").Count == 0, "blank and unmatched searches are empty");
        check(ItemService.FindItem(a.Id.ToUpperInvariant(), null, pool) == a && ItemService.FindItem(null, "BETA", pool) == b, "find supports ID and case-insensitive title");
        check(ItemService.MoveUp(pool, a.Id) == -1 && ItemService.MoveDown(pool, c.Id) == -1 && ItemService.MoveDown(pool, "absent") == -1, "sort boundary and missing ID leave order unchanged");
        check(ItemService.MoveDown(pool, b.Id) == 2 && pool[^1] == b, "move down reaches last slot");
        check(ItemService.MoveUp(pool, b.Id) == 1 && pool[1] == b, "move up returns new slot");
        check(ItemService.Swap(pool, a.Id, c.Id) && pool[0] == c && !ItemService.Swap(pool, a.Id, "absent"), "swap validates both identities");
        var visible = ItemService.ReorderVisibleItems([a, b, c], [c.Id, c.Id, "absent", a.Id]);
        check(visible.SequenceEqual(new[] { c, b, a }), "visible reorder ignores unknown and duplicate IDs and preserves hidden slot");
        check(ItemService.CheckDuplicate(" c:\\EGL-legacy-a.exe ", [a]), "duplicate check normalizes case and whitespace");
        check(!ItemService.CheckDuplicate(" ", [a]), "blank path is not duplicate");
        foreach (var item in new[] { a, b, c }) ItemService.AddItem(item);
        string initial = File.ReadAllText(ConfigService.ItemsFilePath);
        bool rejected = false;
        try { ItemService.AddItem(ItemService.CreateItem("Duplicate", a.ExePath!)); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && File.ReadAllText(ConfigService.ItemsFilePath) == initial, "duplicate add fails without persisting changes");
        ItemService.SaveVisibleOrder([c.Id, a.Id]);
        check(ConfigService.LoadItems().Select(x => x.Id).SequenceEqual(new[] { c.Id, b.Id, a.Id }), "visible order is persisted with unselected item in place");
        check(ItemService.RemoveItems([a.Id, b.Id, a.Id, "absent"]) == 2, "batch removal ignores duplicate and absent IDs");
        check(ItemService.FindInRecycle(a.Id, null)?.Status == 1, "soft delete moves item to recycle bin");
        check(ItemService.MarkPendingDeletion(a.Id), "pending deletion starts for recycled item");
        var deadline = ItemService.FindInRecycle(a.Id, null)!.DeletedAt;
        check(deadline.HasValue && ItemService.MarkPendingDeletion(a.Id) && ItemService.FindInRecycle(a.Id, null)!.DeletedAt == deadline, "repeated pending deletion retains original deadline");
        check(!ItemService.MarkPendingDeletion("absent"), "unknown recycled ID does not become pending");
        check(ItemService.MarkAllPendingDeletion() == 1, "batch pending deletion counts only newly marked items");
        ConfigService.MutateItems((_, recycled) => recycled.Single(x => x.Id == a.Id).DeletedAt = DateTimeOffset.UtcNow.AddHours(-73));
        ItemService.AutoCleanExpired();
        check(ItemService.FindInRecycle(a.Id, null) == null && ItemService.FindInRecycle(b.Id, null) != null, "cleanup deletes expired item and retains recent pending item");
        ItemService.RestoreAll();
        check(ItemService.FindItem(b.Id, null)?.Status == 0 && ConfigService.LoadRecycleBinItems().Count == 0, "restore all clears recycle state");
        ItemService.RemoveItem(b.Id, null, true);
        check(ItemService.FindItem(b.Id, null) == null && ItemService.FindInRecycle(b.Id, null) == null, "permanent removal skips recycle bin");
        ItemService.RemoveItem(c.Id, null);
        ItemService.EmptyRecycle();
        check(ConfigService.LoadRecycleBinItems().Count == 0, "empty recycle removes all entries");
        check(ItemService.ImportGames([new() { Title = "Imported", ExePath = "C:\\EGL-import-test.exe", PlatformBadge = "Steam" }, new() { Title = "Duplicate import", ExePath = "C:\\EGL-import-test.exe" }]) == 1, "single import batch deduplicates its own inputs");
        var imported = ConfigService.LoadItems().Single();
        ItemService.RemoveItem(imported.Id, null, true);
        var configured = ItemService.CreateItem("Launch", "main.exe", true, mgr: "manager.exe", alt: "alt.exe", alongside: "side.exe");
        configured.IsAltAdmin = false;
        configured.IsMgrAdmin = true;
        configured.RunAlongside = true;
        configured.IsAlongsideAdmin = true;
        check(LaunchService.GetLaunchTarget(configured) == ("main.exe", true), "default launch uses main target and privilege");
        check(LaunchService.GetLaunchTarget(configured, true) == ("alt.exe", false), "alternative launch uses independent privilege");
        check(LaunchService.GetAlongsideTarget(configured) == ("side.exe", true), "enabled alongside target is selected");
        configured.RunAlongside = false;
        check(LaunchService.GetAlongsideTarget(configured) == null, "disabled alongside target is omitted");
        check(LaunchService.GetManagerTarget(configured) == ("manager.exe", true), "custom manager uses independent privilege");
        configured.CustomMenuItems = [new() { Title = "Custom", Command = "custom.exe --value", IsAdmin = true }];
        string fingerprint = configured.GetContentFingerprint();
        configured.CustomMenuItems[0].Command = "custom.exe --changed";
        check(configured.GetContentFingerprint() != fingerprint, "content fingerprint includes custom menu edits");
        var copy = ItemService.CreateItem("Copy", "copy.exe");
        string copyId = copy.Id;
        ItemService.CopyContent(copy, configured);
        check(copy.Id == copyId && copy.ExePath == configured.ExePath && copy.AlternativeLaunchCommand == "alt.exe" && copy.CustomMenuItems.Single().Command == "custom.exe --changed", "content copy preserves destination identity and all launch/menu fields");
        check(!LaunchService.Launch(ItemService.CreateItem("Empty", "")).Success, "empty launch target fails");
        check(ProcessRunner.SplitPath("\"C:\\Program Files\\Game\\game.exe\" --arg \"a b\"") == ("C:\\Program Files\\Game\\game.exe", "--arg \"a b\""), "quoted executable keeps argument quoting");
        check(ProcessRunner.SplitPath(" ") == ("", ""), "blank command splits to empty target");
        string existing = Path.Combine(root, "existing game.exe");
        File.WriteAllText(existing, "fixture, never execute");
        string urlFile = Path.Combine(root, "fixture.url");
        File.WriteAllText(urlFile, "[InternetShortcut]\nURL=steam://run/570\nIconFile=cover.ico\nIconIndex=2\n");
        var url = ShortcutResolver.GetUrlFileInfo(urlFile);
        check(url?.ActualUrl == "steam://run/570" && url.IconIndex == 2 && url.IconPath == "cover.ico", "URL shortcut reads protocol and icon metadata");
        check(ShortcutResolver.ResolveTargetPath(urlFile).IsUrlProtocol && ShortcutResolver.ResolveTargetPath(urlFile).ActualPath == "steam://run/570", "URL shortcut resolves to protocol launch target");
        File.WriteAllText(urlFile, "[InternetShortcut]\nIconIndex=invalid\n");
        check(ShortcutResolver.GetUrlFileInfo(urlFile) == null, "URL shortcut without URL is rejected");
        string shortcut = Path.Combine(root, "fixture.lnk");
        ShortcutResolver.CreateShortcut(existing, shortcut, "isolated fixture");
        check(File.Exists(shortcut) && string.Equals(ShortcutResolver.GetLnkTarget(shortcut), existing, StringComparison.OrdinalIgnoreCase), "LNK creation and target resolution work in isolated directory");
        foreach (string legacyIcon in new[] { "ico\\legacy.ico", "ico/legacy.ico", "legacy.ico" })
        {
            var dto = new AppItemDto { Id = "legacy-icon", Icon = legacyIcon };
            check(dto.ToViewModel(ConfigService.FixedCachePath).IconPath == Path.Combine(ConfigService.FixedCachePath, "legacy.ico"), "legacy icon path resolves in active cache: " + legacyIcon);
        }
        Environment.SetEnvironmentVariable("EGL_TEST_EXECUTABLE", existing);
        try { check(ProcessRunner.SplitPath("\"%EGL_TEST_EXECUTABLE%\" --flag") == (existing, "--flag"), "command parser expands environment variable without losing arguments"); }
        finally { Environment.SetEnvironmentVariable("EGL_TEST_EXECUTABLE", null); }
        check(ProcessRunner.SplitPath(existing + " --flag") == (existing, "--flag"), "unquoted existing path with spaces splits at executable");
        check(!ScanService.IsUserLaunchTargetInvalid("\"" + existing + "\" --flag"), "invalid scan retains quoted existing executable");
        check(ScanService.IsUserLaunchTargetInvalid(Path.Combine(root, "missing.exe")), "invalid scan detects missing executable");
        check(!ScanService.IsUserLaunchTargetInvalid("custom-protocol://game/1") && !ScanService.IsUserLaunchTargetInvalid(root), "invalid scan retains protocol and existing directory");
        var classified = ScanService.Classify([new() { Title = "different", ExePath = a.ExePath! }, new() { Title = "new", ExePath = "C:\\new.exe" }], [a]);
        check(classified.existingGames.Count == 1 && classified.newGames.Count == 1, "scan classifies existing path independently of title");
        check(GamePlatformHelper.DetectPlatform("STEAM://run/570")?.PlatformName == "Steam", "Steam protocol recognition ignores case");
        check(GamePlatformHelper.DetectPlatform("com.epicgames.launcher://apps/fixture")?.PlatformName == "Epic Games", "Epic protocol is recognized");
        check(GamePlatformHelper.DetectPlatform("https://example.invalid") == null, "ordinary URL has no platform badge");
        check(SteamHelper.ExtractAppIdFromUrl("steam://run/570") == 570 && SteamHelper.ExtractAppIdFromUrl("steam://rungameid/730") == 730, "Steam launch forms yield app ID");
        check(SteamHelper.ExtractAppIdFromUrl("steam://run/99999999999999999999") == null, "Steam ID overflow is rejected");
        check(GamePlatformHelper.GetRuntimeManagerPath("custom.exe", "steam://run/570") == "custom.exe", "explicit manager overrides platform default");
        foreach (string command in new[] { "list", "settings", "recycle", "version" })
        {
            var result = await cli([command, "--json"]);
            bool valid = false;
            try { using var json = JsonDocument.Parse(result.Output); valid = json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String; } catch (JsonException) { }
            check(result.Code == 0 && valid, command + " emits valid JSON on stdout");
        }
        var help = await cli(["-help"]);
        check(help.Code == 0 && help.Output.Length > 100, "CLI help is available");
        var skill = await cli(["skill"]);
        check(skill.Code == 0 && skill.Output.Contains("launch", StringComparison.OrdinalIgnoreCase), "embedded CLI skill describes commands");
        var absent = await cli(["remove", "--id", "absent", "--json"]);
        check(absent.Code != 0, "CLI missing item returns failure");
        check((await cli(["settings", "--set", "appTitle=Legacy fixture", "--json"])).Code == 0, "settings fixture is persisted before rejection tests");
        ConfigService.Reload();
        string beforeSetting = File.ReadAllText(ConfigService.SettingsFilePath);
        var unknown = await cli(["settings", "--set", "unrecognizedSetting=true", "--json"]);
        check(unknown.Code != 0 && File.ReadAllText(ConfigService.SettingsFilePath) == beforeSetting, "unknown setting is rejected without mutation");
        foreach (var setting in new[] { "launchMode=triple", "closeAfterLaunch=maybe", "quickStart=maybe", "splashFadeIn=maybe", "splashFadeOut=maybe", "iconSize=31", "iconSize=513", "updateChannel=invalid", "windowWidth=399", "windowHeight=299", "windowX=invalid", "windowY=invalid" })
        {
            var result = await cli(["settings", "--set", setting, "--json"]);
            check(result.Code != 0 && File.ReadAllText(ConfigService.SettingsFilePath) == beforeSetting, "setting validation preserves data: " + setting);
        }
        foreach (var setting in new[] { "launchMode=double", "closeAfterLaunch=true", "splashFadeIn=false", "splashFadeOut=false", "iconSize=32", "iconSize=512", "updateChannel=latest", "appTitle=Round trip", "windowWidth=400", "windowHeight=300", "windowX=-1200", "windowY=-100" })
        {
            var result = await cli(["settings", "--set", setting, "--json"]);
            check(result.Code == 0, "valid setting is saved: " + setting);
        }
        ConfigService.Reload();
        check(ConfigService.LaunchMode == "double" && ConfigService.IconSize == 512 && ConfigService.UpdateChannel == "latest" && ConfigService.AppTitle == "Round trip", "settings survive independent CLI process and reload");
        var bilingual = new Announcement { TitleZh = "  旧字段标题  ", BodyZh = "旧字段正文", Position = " BOTTOM ", Time = "2026-10-19T08:00:00+08:00" };
        check(bilingual.GetDisplayTitle() == "旧字段标题" && bilingual.GetDisplayBody() == "旧字段正文", "announcement falls back to legacy Chinese fields and trims whitespace");
        check(bilingual.GetPosition() == "bottom" && bilingual.GetPositionPriority() == 2, "announcement normalizes bottom position and priority");
        bilingual.Position = "unknown";
        check(bilingual.GetPosition() == "normal" && bilingual.GetPositionPriority() == 1, "unknown announcement position becomes normal");
        check(bilingual.GetTimeValue() == new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), "announcement time converts to UTC");
        bilingual.Time = "invalid";
        check(bilingual.GetTimeValue() == null, "invalid announcement time is absent");
        Text.Load("EN");
        check(Text.CurrentLanguage == "EN" && Text.T("Launch_Failed") != "Launch_Failed", "English resources resolve production keys");
        Text.Load("Zh-CN");
        check(Text.CurrentLanguage == "Zh-CN" && Text.T("Launch_Failed") != "Launch_Failed", "Chinese resources resolve production keys");
        Text.Load("EN");
        check(ConfigService.GetWindowBounds().Width > 0, "default window bounds have positive width");
        ConfigService.SetWindowBounds(-1600, -200, 900, 600);
        var bounds = ConfigService.GetWindowBounds();
        check(bounds.X == -1600 && bounds.Y == -200 && bounds.Width == 900 && bounds.Height == 600, "window persistence retains negative monitor coordinates");
        await CliFeatures.RunAsync(check, cli);
        await NetworkFeatures.RunAsync(check);
    }
}
