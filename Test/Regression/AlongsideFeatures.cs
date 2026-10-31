using EricGameLauncher;

internal static class AlongsideFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root, string fixture)
    {
        string Command(string marker) => $"\"{fixture}\" \"{marker}\"";
        int Completed(string marker) => Directory.GetFiles(root, Path.GetFileName(marker) + ".*").Count(path => File.ReadAllText(path) == "completed");
        async Task WaitForCompletion(string marker)
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try { if (Completed(marker) > 0) return; }
                catch (IOException) { }
                await Task.Delay(50);
            }
        }
        var item = ItemService.CreateItem("Alongside regression", Command(Path.Combine(root, "initial-main")));
        ItemService.AddItem(item);
        try
        {
            foreach (bool enabled in new[] { false, true })
            foreach (bool hasCommand in new[] { false, true })
            foreach (bool flag in new[] { false, true })
            {
                string mainMarker = Path.Combine(root, $"main-{enabled}-{hasCommand}-{flag}");
                string alongsideMarker = Path.Combine(root, $"alongside-{enabled}-{hasCommand}-{flag}");
                ItemService.EditItem(item, x =>
                {
                    x.ExePath = Command(mainMarker);
                    x.RunAlongside = enabled;
                    x.AlongsideCommand = hasCommand ? Command(alongsideMarker) : "";
                });
                var before = ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount;
                var result = await cli(flag ? ["launch", "--id", item.Id, "--alongside"] : ["launch", "--id", item.Id]);
                bool expected = hasCommand && (enabled || flag);
                await WaitForCompletion(mainMarker);
                if (expected) await WaitForCompletion(alongsideMarker);
                await Task.Delay(500);
                int observed = Completed(alongsideMarker);
                check(result.Code == 0 && Completed(mainMarker) == 1 && observed == (expected ? 1 : 0), $"alongside executes once only when requested: saved={enabled} command={hasCommand} flag={flag}; expected={(expected ? 1 : 0)} observed={observed} code={result.Code}");
                var saved = ConfigService.LoadItems().Single(x => x.Id == item.Id);
                check(saved.RunAlongside == enabled && saved.LaunchCount == before + 1, "launch leaves saved alongside switch unchanged and counts main once");
            }
            ItemService.EditItem(item, x => x.AlongsideCommand = Path.Combine(root, "missing-alongside.exe"));
            var count = ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount;
            check((await cli(["launch", "--id", item.Id, "--alongside"])).Code != 0, "alongside failure propagates to CLI");
            check(ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount == count + 1, "successful main counts even when alongside fails");
            string failedMarker = Path.Combine(root, "failed-main-alongside");
            ItemService.EditItem(item, x =>
            {
                x.ExePath = Path.Combine(root, "missing-main.exe");
                x.AlongsideCommand = Command(failedMarker);
            });
            count = ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount;
            check((await cli(["launch", "--id", item.Id, "--alongside"])).Code != 0 && Completed(failedMarker) == 0, "main failure prevents alongside execution");
            check(ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount == count, "main failure does not count");
        }
        finally { ItemService.RemoveItem(item.Id, null, true); }
    }
}
