using EricGameLauncher;
using System.Net;
using System.Net.Http;

internal static class RemainingFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        var item = ItemService.CreateItem("Remaining original", "remaining-fixture://launch");
        ItemService.AddItem(item);
        foreach (string[] invalid in new string[][]
        {
            ["edit", "--id", item.Id, "--title"], ["edit", "--id", item.Id, "--pin", "typo"],
            ["edit", "--id", item.Id], ["edit", "--id", item.Id, "--title", "a", "--title", "b"],
            ["edit", "--id", item.Id, "--unknown", "a"], ["remove", "--id", item.Id, "--title", item.Title!],
            ["scan", "--steam", "--invalid", "--import"], ["scan", "--delete-invalid", "--import"],
            ["settings", "--set", "language=EN", "--list"], ["add", "--path"],
            ["edit", "--id", item.Id, "--custom-add", "bad"], ["edit", "--id", item.Id, "--admin", "false", "--pin", "invalid"]
        })
        {
            string file = ConfigService.ItemsFilePath;
            byte[] before = File.ReadAllBytes(file);
            var files = Directory.GetFiles(ConfigService.FixedCachePath).Order().ToArray();
            var plain = await cli(invalid);
            var json = await cli(invalid.Concat(["--json"]).ToArray());
            check(plain.Code != 0 && json.Code != 0 && System.Text.Json.JsonDocument.Parse(json.Output).RootElement.GetProperty("success").GetBoolean() == false,
                "Remaining CLI rejects malformed request in both formats: " + string.Join(' ', invalid.Take(3)));
            check(before.SequenceEqual(File.ReadAllBytes(file)) && files.SequenceEqual(Directory.GetFiles(ConfigService.FixedCachePath).Order()), "Remaining rejected CLI leaves bytes and icons unchanged");
        }
        check((await cli(["edit", "--id", item.Id, "--pin", "1", "--admin", "false"])).Code == 0, "Remaining strict parser accepts documented Boolean forms");
        ItemService.RemoveItem(item.Id, null, true);
        await SessionsAsync(check, root);
        await ScansAsync(check, root);
        await DownloadsAsync(check);
        int attempts = 0;
        check(await ExitSaveController.ConfirmAsync(() => { if (++attempts == 1) throw new IOException(); }, _ => Task.FromResult(ExitSaveDecision.Retry)) && attempts == 2, "Remaining exit retries a failed save");
        check(!await ExitSaveController.ConfirmAsync(() => throw new IOException(), _ => Task.FromResult(ExitSaveDecision.Stay)), "Remaining failed exit retains session when staying");
        check(await ExitSaveController.ConfirmAsync(() => throw new IOException(), _ => Task.FromResult(ExitSaveDecision.Discard)), "Remaining exit discards only after explicit decision");
    }

    private static async Task SessionsAsync(Action<bool, string> check, string root)
    {
        var resources = new PropertyDraftSession();
        var controller = new PropertySaveController(resources);
        string icon = Path.Combine(root, "saving-draft.png");
        File.WriteAllText(icon, "icon");
        await resources.TrackAsync(() => Task.FromResult<string?>(icon));
        var a = ItemService.CreateItem("A", "remaining-a://launch", icon: icon);
        var gate = new TaskCompletionSource();
        int writes = 0;
        var saving = controller.SaveAsync(a, a, true, default, async (_, frozen, _) =>
        {
            check(frozen.Title == "A", "Remaining save captures immutable A before waiting");
            await gate.Task;
            check(File.Exists(frozen.IconPath), "Remaining A icon survives closing A while save prepares");
            writes++;
        });
        a.Title = "Changed after submission";
        check(!await controller.SaveAsync(a, a, true, default, (_, _, _) => { writes++; return Task.CompletedTask; }), "Remaining repeated save does not start a second transaction");
        resources.Advance();
        var b = ItemService.CreateItem("B", "remaining-b://launch");
        check(await controller.SaveAsync(b, b, true, default, (_, _, _) => Task.CompletedTask), "Remaining new session B can save while A waits");
        gate.SetResult();
        check(!await saving && writes == 1 && File.Exists(icon), "Remaining completed A cannot commit or close B and committed icon survives");
        var fail = new TaskCompletionSource();
        string failedIcon = Path.Combine(root, "failed-draft.png");
        File.WriteAllText(failedIcon, "icon");
        await resources.TrackAsync(() => Task.FromResult<string?>(failedIcon));
        b.IconPath = failedIcon;
        var failed = controller.SaveAsync(b, b, true, default, async (_, _, _) => { await fail.Task; throw new IOException(); });
        resources.Advance();
        fail.SetResult();
        try { await failed; } catch (IOException) { }
        check(!File.Exists(failedIcon) && !controller.IsSaving, "Remaining closed failed save releases its own icon and busy state");
        bool retryFailed = false;
        try { await controller.SaveAsync(b, b, true, default, (_, _, _) => throw new IOException()); } catch (IOException) { retryFailed = true; }
        check(retryFailed && await controller.SaveAsync(b, b, true, default, (_, _, _) => Task.CompletedTask), "Remaining save failure permits same-session retry");
        using var closed = new CancellationTokenSource();
        var completion = new TaskCompletionSource();
        var closing = controller.SaveAsync(b, b, false, closed.Token, async (_, _, _) => await completion.Task);
        closed.Cancel();
        completion.SetResult();
        check(!await closing && !controller.IsSaving, "Remaining window lifetime blocks completion UI changes");
        check(!await controller.SaveAsync(b, b, false, closed.Token, (_, _, _) => throw new Exception()), "Remaining closed lifetime cannot begin a save");
        File.Delete(icon);
    }

    private static async Task ScansAsync(Action<bool, string> check, string root)
    {
        check(PlatformScan.Identity("steam://run/570") == PlatformScan.Identity("steam://rungameid/570"), "Remaining Steam canonical identity ignores launch spelling");
        check(PlatformScan.Identity("mysteam://run/570") != PlatformScan.Identity("steam://run/570"), "Remaining platform identity requires the actual Steam protocol");
        check(PlatformScan.Identity("com.epicgames.launcher://apps/Ns%3AItem%3AApp?b=2&a=1") == PlatformScan.Identity("com.epicgames.launcher://apps/ns:item:app?action=launch"), "Remaining Epic identity ignores encoding query and case");
        check(PlatformScan.Identity("com.epicgames.launcher://apps/Ns%3AItem%3AApp") == PlatformScan.Identity("com.epicgames.launcher://apps/App"), "Remaining Epic identity supports short and catalog-qualified app IDs");
        var game = new ScannedGame { Title = "same title", ExePath = "steam://rungameid/570", PlatformBadge = "Steam" };
        var valid = ItemService.CreateItem("custom title", "steam://run/570", platform: "Steam");
        var other = ItemService.CreateItem("same title", "steam://run/571", platform: "Steam");
        var local = ItemService.CreateItem("local", Path.Combine(root, "missing-scope.exe"));
        check(ScanService.Classify([game], [other]).newGames.Count == 1 && ScanService.Classify([game], [valid]).existingGames.Count == 1, "Remaining classification uses ID rather than title");
        var result = await PlatformScan.RunAsync(["Steam"], scan: _ => Task.FromResult(new List<ScannedGame> { game }));
        check(ScanService.FindInvalidGames([valid, other, local, ItemService.CreateItem("Epic", "com.epicgames.launcher://apps/missing", platform: "Epic Games")], result).Single().ExePath == other.ExePath, "Remaining selected platform invalidation excludes local and unscanned platforms");
        var failed = await PlatformScan.RunAsync(["Steam"], scan: _ => throw new IOException("offline library"));
        check(failed.Failures.ContainsKey("Steam") && failed.Completed.Count == 0 && ScanService.FindInvalidGames([other], failed).Count == 0, "Remaining incomplete scan never proves absence");
        var empty = await PlatformScan.RunAsync(["Steam"], scan: _ => Task.FromResult(new List<ScannedGame>()));
        check(empty.Completed.Contains("Steam") && ScanService.FindInvalidGames([other], empty).Count == 1, "Remaining complete empty library differs from unavailable library");
        string library = Path.Combine(root, "steam-library");
        string apps = Path.Combine(library, "steamapps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "appmanifest_570.acf"), "\"AppState\" { \"appid\" \"570\" \"name\" \"Game\" \"installdir\" \"Game\" }");
        check(PlatformScan.ReadSteam([library]).Single().ExePath.EndsWith("570"), "Remaining strict manifest scan reads installed identity");
        bool rejected = false;
        try { PlatformScan.ReadSteam([library, Path.Combine(root, "disconnected-library")]); } catch (IOException) { rejected = true; }
        check(rejected, "Remaining disconnected library cannot return a complete partial list");
        File.WriteAllText(Path.Combine(apps, "appmanifest_571.acf"), "broken");
        rejected = false;
        try { PlatformScan.ReadSteam([library]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Remaining corrupt Steam manifest fails the platform scan");
        string epic = Path.Combine(root, "epic-manifests");
        Directory.CreateDirectory(epic);
        File.WriteAllText(Path.Combine(epic, "game.item"), "{\"AppName\":\"app\",\"InstallLocation\":\"C:\\\\fixture\"}");
        check(PlatformScan.ReadEpic(epic).Count == 1, "Remaining strict Epic manifest is parsed as JSON");
        File.WriteAllText(Path.Combine(epic, "broken.item"), "{");
        rejected = false;
        try { PlatformScan.ReadEpic(epic); } catch (System.Text.Json.JsonException) { rejected = true; }
        check(rejected, "Remaining corrupt Epic manifest fails the platform scan");
    }

    private static async Task DownloadsAsync(Action<bool, string> check)
    {
        foreach (bool idle in new[] { true, false })
        {
            using var client = new HttpClient(new BodyHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            using var output = new MemoryStream();
            bool canceled = false;
            try { await UpdateDownload.DownloadAsync(client, "http://fixture/package", output, null,
                idle ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(120), idle ? TimeSpan.FromMilliseconds(120) : TimeSpan.FromSeconds(5)); }
            catch (OperationCanceledException) { canceled = true; }
            check(canceled, idle ? "Remaining body stalls obey idle timeout after headers" : "Remaining whole-body deadline survives headers completion");
        }
        using var canceledToken = new CancellationTokenSource();
        using var http = new HttpClient(new BodyHandler());
        using var destination = new MemoryStream();
        var downloading = UpdateDownload.DownloadAsync(http, "http://fixture/package", destination, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), canceledToken.Token);
        canceledToken.Cancel();
        bool stopped = false;
        try { await downloading; } catch (OperationCanceledException) { stopped = true; }
        check(stopped, "Remaining caller cancellation interrupts a blocked body read");
    }

    private sealed class BodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockedBody()) });
    }

    private sealed class BlockedBody : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }
}
