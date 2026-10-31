using EricGameLauncher;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using updater.main;

if (args.Length != 2) throw new ArgumentException("Pass the CLI and launch-fixture executable paths.");
string cli = Path.GetFullPath(args[0]);
string root = EricGameLauncher.Testing.TestWorkspace.NewFixture("EGL.Regression.");
Directory.CreateDirectory(root);
ConfigService.ApplyDebugMode(root);
Text.Load("EN");
ConfigService.Initialize();
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL " + name);
    Console.WriteLine("PASS " + name); checks++;
}

async Task<(int Code, string Output, string Error)> Cli(params string[] arguments)
{
    var start = new ProcessStartInfo(cli) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.StandardOutputEncoding = new UTF8Encoding(false);
    start.StandardErrorEncoding = new UTF8Encoding(false);
    start.ArgumentList.Add("-debug");
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
    catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
    return (process.ExitCode, await output, await error);
}

try
{
    JumpListFeatures.Run(Check);
    await AlongsideFeatures.RunAsync(Check, Cli, root, Path.GetFullPath(args[1]));
    await WeGameFeatures.RunAsync(Check, Cli, root);
    await LaunchCountFeatures.RunAsync(Check);
    await LegacyFeatures.RunAsync(Check, Cli, root);
    await IconFeatures.RunAsync(Check, root);
    await CoreGapFeatures.RunAsync(Check, root);
    var first = ItemService.CreateItem("First", "C:\\EGL-regression-missing-A.exe");
    ItemService.AddItem(first);
    string id = first.Id;
    ItemService.EditItem(first, item => item.ExePath = "C:\\EGL-regression-missing-B.exe");
    Check(ConfigService.LoadItems().Count == 1 && first.Id == id, "path editing preserves identity and count");
    var external = await Cli("add", "--title", "External", "--path", "C:\\EGL-regression-external.exe");
    Check(external.Code == 0, "child process adds item");
    ConfigService.SaveAll();
    Check(ConfigService.LoadItems().Any(x => x.Title == "External"), "stale parent shutdown cannot erase child addition");
    ConfigService.AppTitle = "Parent title";
    await Cli("settings", "--set", "closeAfterLaunch=true");
    ConfigService.SaveSettings();
    ConfigService.Reload();
    Check(ConfigService.CloseAfterLaunch && ConfigService.AppTitle == "Parent title", "settings merge changes by field");
    var simultaneous = Enumerable.Range(0, 6).Select(i => Cli("add", "--title", "Concurrent" + i, "--path", "C:\\EGL-concurrent-" + i + ".exe")).ToArray();
    Check((await Task.WhenAll(simultaneous)).All(x => x.Code == 0), "concurrent CLI writes succeed");
    Check(ConfigService.LoadItems().Count(x => x.Title!.StartsWith("Concurrent")) == 6, "concurrent additions all survive");
    ItemService.RemoveItem(id, null);
    var recycledFirst = ConfigService.LoadRecycleBinItems().Single(x => x.Id == id);
    Check(!string.IsNullOrEmpty(recycledFirst.RecycleId) && recycledFirst.RecycleId != recycledFirst.Id, "recycle bin keeps original id and adds a distinct recycle id");
    Check(ItemService.ImportGames([new() { Title = "Duplicate", ExePath = first.ExePath!, PlatformBadge = "Steam" }]) == 1, "import ignores recycle bin paths");
    var reimported = ConfigService.LoadItems().Single(x => x.ExePath == first.ExePath);
    bool restoreBlocked = false;
    try { ItemService.RestoreItem(recycledFirst.RecycleId, null); } catch (ItemConflictException) { restoreBlocked = true; }
    Check(restoreBlocked && ConfigService.LoadRecycleBinItems().Count == 1, "restore rejects a path already present in the main list");
    ItemService.RemoveItem(reimported.Id, null, true);
    ItemService.RestoreItem(recycledFirst.RecycleId, null);
    var restoredFirst = ConfigService.LoadItems().Single(x => x.Id == id);
    Check(restoredFirst.RecycleId == null, "restore strips recycle state and keeps original identity");
    var ordered = ConfigService.LoadItems();
    string penultimate = ordered[^2].Id;
    var down = await Cli("sort", "--id", penultimate, "--move-down");
    Check(down.Code == 0 && ConfigService.LoadItems()[^1].Id == penultimate, "CLI moves penultimate item to last");
    var invalid = await Cli("unknown-command", "--json");
    using (var json = JsonDocument.Parse(invalid.Output)) Check(invalid.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean(), "unknown command returns JSON failure and nonzero exit");
    var launch = await Cli("launch", "--path", "C:\\EGL-regression-does-not-exist.exe", "--json");
    using (var json = JsonDocument.Parse(launch.Output)) Check(launch.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean(), "launch failure reaches CLI exit and JSON");
    string icon = Path.Combine(root, "external-cover.png");
    await File.WriteAllBytesAsync(icon, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/ZbcAAAAASUVORK5CYII="));
    var covered = ItemService.CreateItem("Cover", "C:\\EGL-cover.exe", icon: icon);
    ItemService.AddItem(covered);
    string savedIcon = ItemService.FindItem(covered.Id, null)!.IconPath!;
    Check(savedIcon != icon && File.Exists(savedIcon), "external icon imported into persistent cache");
    string fake = "ghp_EGL_FAKE_TOKEN_ONLY_FOR_REGRESSION";
    var token = await Cli("settings", "--set", "githubToken=" + fake);
    Check(token.Code == 0 && !token.Output.Contains(fake) && !token.Error.Contains(fake), "token never printed");
    Check(!Directory.GetFiles(Path.Combine(root, "Cache", "log"), "*.log").Any(file =>
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Contains(fake);
    }), "token absent from child logs");
    ConfigService.Reload();
    Check(ConfigService.GitHubToken == fake, "DPAPI round trip succeeds");
    await Cli("settings", "--set", "githubToken=");
    ConfigService.Reload();
    Check(ConfigService.GitHubToken == "", "external token clear resets memory");
    string? registeredBefore = QuickStartService.GetRegisteredCommand();
    var quickStart = await Cli("settings", "--set", "quickStart=true");
    Check(quickStart.Code == 0 && QuickStartService.GetRegisteredCommand() == registeredBefore, "debug quick-start setting leaves real autostart registration unchanged");
    await Cli("settings", "--set", "quickStart=false");
    string original = File.ReadAllText(ConfigService.ItemsFilePath);
    await File.WriteAllTextAsync(ConfigService.ItemsFilePath, "version: [broken");
    var corrupted = await Cli("settings", "--set", "appTitle=Safe", "--json");
    Check(corrupted.Code != 0 && File.ReadAllText(ConfigService.ItemsFilePath) == "version: [broken", "corrupt YAML remains untouched and command fails");
    AtomicFile.Write(ConfigService.ItemsFilePath, original);
    ConfigService.Reload();
    Check(File.Exists(ConfigService.ItemsFilePath + ".bak"), "atomic replacement creates backup");
    using (var locked = new FileStream(ConfigService.ItemsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool failed = false;
        try { ItemService.AddItem(ItemService.CreateItem("Locked", "C:\\EGL-locked.exe")); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
        Check(failed, "locked target reports persistence failure");
    }
    Check(File.ReadAllText(ConfigService.ItemsFilePath) == original, "failed replacement preserves original content");
    Check(MarkdownHtml.ToHtml("# Heading\n\nfirst line\nsecond line").Contains("<h1>Heading</h1><p>first line<br/>second line</p>"), "built-in update renderer preserves headings and line breaks");
    Check(!MarkdownHtml.ToHtml("<script>alert(1)</script>").Contains("<script>"), "raw Markdown HTML disabled");

    byte[] Package()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("EricGameLauncher.exe").Open())) writer.Write("new fixture; not executable");
            using (var writer = new StreamWriter(zip.CreateEntry("new.dll").Open())) writer.Write("new file");
        }
        return bytes.ToArray();
    }
    foreach (string scenario in new[] { "success", "install-failure", "rollback-failure" })
    {
        string install = Path.Combine(root, scenario); Directory.CreateDirectory(install);
        await File.WriteAllTextAsync(Path.Combine(install, "EricGameLauncher.exe"), "old fixture");
        Directory.CreateDirectory(Path.Combine(install, "Data"));
        await File.WriteAllTextAsync(Path.Combine(install, "Data", "keep.txt"), "user data");
        bool failed = false;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(Package()), ZipArchiveMode.Read);
            UpdateTransaction.Apply(install, zip, step =>
            {
                if (scenario != "success" && step == "commit") throw new IOException("injected install failure");
                if (scenario == "rollback-failure" && step.StartsWith("rollback-restore:")) throw new IOException("injected recovery failure");
            });
        }
        catch (IOException) { failed = true; }
        Check(File.ReadAllText(Path.Combine(install, "Data", "keep.txt")) == "user data", scenario + ": user data preserved");
        if (scenario == "success") Check(!failed && !Directory.Exists(Path.Combine(install, "._update_backup")), "successful transaction cleans backup");
        if (scenario == "install-failure") Check(failed && File.ReadAllText(Path.Combine(install, "EricGameLauncher.exe")) == "old fixture" && !File.Exists(Path.Combine(install, "new.dll")), "rollback restores old files and removes newly added files");
        if (scenario == "rollback-failure")
        {
            Check(failed && File.Exists(Path.Combine(install, "._update_backup", "files", "EricGameLauncher.exe")), "failed rollback preserves recoverable backup");
            using var retry = new ZipArchive(new MemoryStream(Package()), ZipArchiveMode.Read);
            UpdateTransaction.Apply(install, retry);
            Check(File.ReadAllText(Path.Combine(install, "EricGameLauncher.exe")).StartsWith("new fixture"), "next update recovers unfinished transaction before installing");
        }
    }
    foreach (bool fail in new[] { false, true })
    {
        string source = Path.Combine(root, "storage-source-" + fail), destination = Path.Combine(root, "storage-destination-" + fail);
        Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(Path.Combine(source, "settings.yaml"), "appTitle: Source\n");
        await File.WriteAllTextAsync(Path.Combine(source, "items.yaml"), "version: 1\nitems: []\nrecycleBin: []\n");
        await File.WriteAllTextAsync(Path.Combine(destination, "settings.yaml"), "appTitle: Previous\n");
        string mode = Path.Combine(root, "mode-" + fail);
        bool failed = false;
        try { StorageMigration.Transfer(source, destination, true, step => { if (fail && step == "commit") throw new IOException("injected migration failure"); }, mode); }
        catch (IOException) { failed = true; }
        Check(File.Exists(Path.Combine(source, "settings.yaml")) && File.Exists(Path.Combine(source, "items.yaml")), "storage migration retains source: " + fail);
        Check(fail ? failed && !File.Exists(mode) && File.ReadAllText(Path.Combine(destination, "settings.yaml")).Contains("Previous") : !failed && File.ReadAllText(mode) == "system" && File.Exists(Path.Combine(destination, "items.yaml")), "storage migration commits or restores complete target: " + fail);
    }
    int requests = 0;
    UpdateService.ConfigureTransport(new MockHttp(request =>
    {
        requests++;
        int page = request.RequestUri!.Query.Contains("page=2") ? 2 : 1;
        var entries = Enumerable.Range(page == 1 ? 0 : 100, page == 1 ? 100 : 1).Select(i => new UpdateService.ReleaseInfo { tag_name = "v1.0." + i, name = "Release " + i, published_at = "2026-01-01T00:00:00Z" }).ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(entries)) };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"fixture\"");
        if (request.Headers.IfNoneMatch.Count > 0) response.StatusCode = HttpStatusCode.NotModified;
        return response;
    }));
    Check((await UpdateService.GetReleaseHistoryAsync())!.Count == 101 && requests == 2, "release history reads beyond 100 items");
    await UpdateService.GetReleaseHistoryAsync();
    Check(requests == 2, "release history short cache avoids redundant requests");
    UpdateService.InvalidateCache();
    Check((await UpdateService.GetReleaseHistoryAsync())!.Count == 101 && requests == 4, "ETag 304 reuses each cached page");
    ConfigService.GitHubToken = "ghp_old_fixture";
    UpdateService.ConfigureTransport(new MockHttp(request =>
    {
        if (request.Headers.Authorization != null)
        {
            Check(request.Headers.Authorization.Parameter == "ghp_old_fixture", "request uses current token");
            ConfigService.GitHubToken = "ghp_new_fixture";
            return new(HttpStatusCode.Unauthorized);
        }
        return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
    }));
    await UpdateService.GetReleaseHistoryAsync();
    Check(ConfigService.GitHubToken == "ghp_new_fixture", "old 401 cannot clear newer token");
    UpdateService.ConfigureTransport(new MockHttp(_ => new(HttpStatusCode.TooManyRequests)));
    bool limited = false;
    try { await UpdateService.CheckUpdateStatusAsync("stable"); } catch (UpdateFetchException) { limited = true; }
    Check(limited, "rate limit is an error, not no-update success");
    var migratedItem = new Dictionary<object, object> { ["status"] = 1, ["path"] = "sample.exe" };
    var migrationRoot = new Dictionary<object, object> { ["version"] = 0, ["items"] = new List<object> { migratedItem } };
    var transformations = new List<object>
    {
        new Dictionary<object, object> { ["type"] = "SplitRecycleBin" },
        new Dictionary<object, object> { ["type"] = "MoveAndGroup", ["target"] = "main", ["sourceFields"] = new Dictionary<object, object> { ["path"] = "path" } }
    };
    var rule = new Dictionary<object, object> { ["from"] = 0, ["to"] = 1, ["transformations"] = transformations };
    updater.cfgver.MigrationRules.Apply(migrationRoot, [rule], 1);
    Check(((List<object>)migrationRoot["items"]).Count == 0 && ((List<object>)migrationRoot["recycleBin"]).Count == 1 && migratedItem.ContainsKey("main"), "migration split then grouping transforms recycled items without enumeration errors");
    bool cycleRejected = false;
    try { updater.cfgver.MigrationRules.Apply(new() { ["version"] = 0 }, [new Dictionary<object, object> { ["from"] = 0, ["to"] = 0 }], 1); } catch (InvalidDataException) { cycleRejected = true; }
    Check(cycleRejected, "migration rejects non-advancing rule");
    Check(!SensitiveText.Redact("githubToken: ghp_FixtureSecret").Contains("FixtureSecret") && !SensitiveText.Redact("header ghp_FixtureSecret").Contains("FixtureSecret"), "token redaction covers YAML and arbitrary log context");
    string iconSource = Path.Combine(root, "rebuild.ico");
    File.Copy(AppIconService.GetIconPath(), iconSource);
    var rebuilding = ItemService.CreateItem("Before rebuild", iconSource);
    ItemService.AddItem(rebuilding);
    Task rebuildingTask = ConfigService.RefreshGlobalAsync();
    var duringRebuild = ItemService.CreateItem("Added during rebuild", "C:\\EGL-during-rebuild.exe");
    ItemService.AddItem(duringRebuild);
    ItemService.EditItem(rebuilding, item => item.Title = "Edited during rebuild");
    await rebuildingTask;
    Check(ItemService.FindItem(duringRebuild.Id, null) != null && ItemService.FindItem(rebuilding.Id, null)?.Title == "Edited during rebuild", "asynchronous icon completion preserves concurrent addition and edit");
    var sample = Enumerable.Range(0, 1000).Select(i => ItemService.CreateItem("性能样本 " + i, "C:\\EGL-perf-" + i + ".exe")).ToList();
    var announcementState = new AnnouncementListState();
    var draftSession = new PropertyDraftSession();
    string draftIcon = Path.Combine(root, "draft-owned.png"), committedIcon = Path.Combine(root, "draft-committed.png");
    await File.WriteAllTextAsync(draftIcon, "temporary");
    Check(await draftSession.TrackAsync(() => Task.FromResult<string?>(draftIcon)) == draftIcon, "active edit owns extracted temporary icon");
    draftSession.Advance();
    Check(!File.Exists(draftIcon), "cancel edit cleans temporary icon");
    await File.WriteAllTextAsync(committedIcon, "committed");
    await draftSession.TrackAsync(() => Task.FromResult<string?>(committedIcon));
    draftSession.Commit(committedIcon);
    draftSession.Advance();
    Check(File.Exists(committedIcon), "commit keeps saved icon when edit closes");
    var lateIcon = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task<string?> lateExtraction = draftSession.TrackAsync(() => lateIcon.Task);
    int oldGeneration = draftSession.Generation;
    draftSession.Advance();
    string latePath = Path.Combine(root, "draft-late.png");
    await File.WriteAllTextAsync(latePath, "late result");
    lateIcon.SetResult(latePath);
    Check(await lateExtraction == null && !File.Exists(latePath), "old extraction completing after cancel is discarded and cleaned");
    Check(draftSession.Generation != oldGeneration, "new edit invalidates previous close-animation generation");
    Check(await draftSession.TrackAsync(() => Task.FromResult<string?>(null)) == null, "failed extraction does not create a draft icon");
    var announcement = new Announcement { Id = "regression-announcement", TitleCn = "First", TitleEn = "First", BodyCn = "Original", BodyEn = "Original", Position = "top" };
    Check(announcementState.Refresh([announcement]) && announcementState.Items.Count == 1, "announcement state loads initial content");
    var originalRow = announcementState.Items[0];
    Check(!announcementState.Refresh([announcement]) && ReferenceEquals(originalRow, announcementState.Items[0]), "unchanged announcement retains row instance");
    originalRow.IsExpanded = true;
    announcement.TitleCn = announcement.TitleEn = "Updated";
    announcement.BodyCn = announcement.BodyEn = "Replacement body";
    announcement.Position = "bottom";
    Check(announcementState.Refresh([announcement]) && announcementState.Items[0].Body == "Replacement body" && announcementState.Items[0].Title == "Updated", "same announcement ID refreshes edited title and body");
    Check(announcementState.Items[0].IsExpanded && announcementState.Items[0].MarkerGlyph == "\uE896", "announcement refresh retains expansion and updates bottom marker");
    ServerConfigManager.MarkAsRead(announcement.Id, notify: false);
    Check(announcementState.Refresh([announcement]) && announcementState.Items[0].IsRead, "external read-state change refreshes announcement row");
    Check(announcementState.Refresh([]) && announcementState.Items.Count == 0, "removed announcements clear old rows");
    var sampleClock = Stopwatch.StartNew();
    ConfigService.MutateItems((items, _) => items.AddRange(sample));
    long writeMilliseconds = sampleClock.ElapsedMilliseconds;
    sampleClock.Restart();
    var loadedSample = ConfigService.LoadItems();
    long readMilliseconds = sampleClock.ElapsedMilliseconds;
    sampleClock.Restart();
    var searchedSample = ItemService.Search(loadedSample, "性能样本 999");
    long searchMilliseconds = sampleClock.ElapsedMilliseconds;
    Check(loadedSample.Count >= 1000 && searchedSample.Count == 1, "1000-item persistence and search sample remains correct");
    Console.WriteLine($"PERF 1000 items: transaction={writeMilliseconds}ms load={readMilliseconds}ms search={searchMilliseconds}ms");
    Console.WriteLine($"{checks} checks passed. Fixture: {root}");
}
finally
{
    LogService.FlushAndStop();
    EricGameLauncher.Testing.TestWorkspace.DeleteFixture(root);
}

sealed class MockHttp(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
}
