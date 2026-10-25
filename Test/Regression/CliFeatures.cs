using EricGameLauncher;
using System.Text.Json;

internal static class CliFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli)
    {
        foreach (string[] arguments in new[] { new[] { "add", "--path", "missing-title.exe" }, new[] { "add", "--title", "missing-path" }, new[] { "edit", "--id", "absent" }, new[] { "restore", "--id", "absent" }, new[] { "sort", "--move-up" }, new[] { "search" }, new[] { "recycle", "--mark" } })
        {
            var result = await cli([.. arguments, "--json"]);
            using var json = JsonDocument.Parse(result.Output);
            check(result.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean(), "CLI invalid arguments return JSON failure: " + string.Join(' ', arguments));
        }
        var add = await cli(["add", "--title", "CLI 完整流程", "--path", "C:\\EGL-cli-flow.exe", "--admin", "--mgr", "manager.exe", "--alt", "alternate.exe", "--alongside", "alongside.exe", "--json"]);
        var item = ConfigService.LoadItems().Single(x => x.Title == "CLI 完整流程");
        check(add.Code == 0 && item.IsAdmin && item.MgrPath == "manager.exe" && item.AlternativeLaunchCommand == "alternate.exe" && item.AlongsideCommand == "alongside.exe", "CLI add persists all launch arguments");
        var edit = await cli(["edit", "--id", item.Id, "--title", "CLI 已编辑", "--path", "C:\\EGL-cli-edited.exe", "--json"]);
        check(edit.Code == 0 && ItemService.FindItem(item.Id, null)?.Title == "CLI 已编辑", "CLI edit preserves identity and changes title");
        string cliIconExe = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var autoIcon = await cli(["add", "--title", "CLI 自动图标", "--path", cliIconExe, "--json"]);
        var autoItem = ConfigService.LoadItems().Single(x => x.Title == "CLI 自动图标");
        check(autoIcon.Code == 0 && !string.IsNullOrEmpty(autoItem.IconPath) && File.Exists(autoItem.IconPath), "CLI add extracts icon automatically");
        await cli(["remove", "--id", autoItem.Id, "--permanent", "--json"]);
        var search = await cli(["search", "已编辑", "--json"]);
        using (var json = JsonDocument.Parse(search.Output)) check(search.Code == 0 && json.RootElement.GetProperty("results").GetArrayLength() == 1, "CLI positional Unicode search returns item");
        var sort = await cli(["sort", "--list", "--json"]);
        using (var json = JsonDocument.Parse(sort.Output)) check(sort.Code == 0 && json.RootElement.GetProperty("items").GetArrayLength() == 1, "CLI sort list emits indexed items");
        check((await cli(["remove", "--id", item.Id, "--json"])).Code == 0 && ItemService.FindInRecycle(item.Id, null) != null, "CLI remove moves item to recycle");
        var recycled = ConfigService.LoadRecycleBinItems().Single(x => x.Id == item.Id);
        check(!string.IsNullOrEmpty(recycled.RecycleId), "CLI recycle entry exposes recycle id");
        var recycleList = await cli(["list", "--recycle", "--json"]);
        using (var json = JsonDocument.Parse(recycleList.Output))
            check(recycleList.Code == 0 && json.RootElement.GetProperty("recycleBinItems")[0].GetProperty("RecycleId").GetString() == recycled.RecycleId, "CLI list --recycle emits recycle id");
        check((await cli(["recycle", "--mark", recycled.RecycleId!, "--json"])).Code == 0 && ItemService.FindInRecycle(item.Id, null)?.Status == 2, "CLI mark accepts recycle id");
        check((await cli(["restore", "--recycle-id", recycled.RecycleId!, "--json"])).Code == 0 && ItemService.FindItem(item.Id, null)?.Status == 0, "CLI restore accepts recycle id");
        check((await cli(["remove", "--id", item.Id, "--json"])).Code == 0, "CLI remove again prepares recycle duplicate scenario");
        var readd = await cli(["add", "--title", "CLI 重新添加", "--path", "C:\\EGL-cli-edited.exe", "--json"]);
        check(readd.Code == 0 && ConfigService.LoadItems().Count(x => x.ExePath == "C:\\EGL-cli-edited.exe") == 1 && ConfigService.LoadRecycleBinItems().Count == 1, "CLI add allows a path that only exists in the recycle bin");
        var readdBusy = await cli(["add", "--title", "CLI 再添加", "--path", "C:\\EGL-cli-edited.exe", "--json"]);
        check(readdBusy.Code != 0, "CLI add still rejects a path already in the main list");
        check((await cli(["recycle", "--purge", "--json"])).Code == 0 && ConfigService.LoadRecycleBinItems().Count == 0, "CLI purge explicitly removes recycled entries");
        foreach (string command in new[] { "launch", "platform", "add", "edit", "remove", "restore", "recycle", "scan", "search", "sort", "settings", "update", "announcements", "install", "uninstall", "storage" })
        {
            var help = await cli([command, "--help"]);
            check(help.Code == 0 && !string.IsNullOrWhiteSpace(help.Output), "CLI command help is available: " + command);
        }
        var scan = await cli(["scan", "--steam", "--import", "--json"]);
        using (var json = JsonDocument.Parse(scan.Output))
            check(scan.Code == 0 && json.RootElement.TryGetProperty("imported", out var imported) && imported.GetInt32() >= 0 && json.RootElement.GetProperty("scannedGames").ValueKind == JsonValueKind.Array, "CLI scan import and JSON execute together without stdout progress pollution");

        var settingsList = await cli(["settings", "--list", "--json"]);
        using (var json = JsonDocument.Parse(settingsList.Output))
            check(settingsList.Code == 0 && json.RootElement.GetProperty("dataPath").GetString() == ConfigService.CurrentDataPath && json.RootElement.TryGetProperty("window", out _), "CLI settings list reports data path and window bounds");
        var settingsGet = await cli(["settings", "--get", "iconsize", "--json"]);
        using (var json = JsonDocument.Parse(settingsGet.Output))
            check(settingsGet.Code == 0 && json.RootElement.GetProperty("value").GetString() == ConfigService.IconSize.ToString(), "CLI settings get returns the live value");
        check((await cli(["settings", "--get", "unknownkey", "--json"])).Code != 0, "CLI settings get rejects an unknown key");

        string currentStorageMode = ConfigService.IsSystemMode ? "system" : "portable";
        var storageStatus = await cli(["storage", "--status", "--json"]);
        using (var json = JsonDocument.Parse(storageStatus.Output))
            check(storageStatus.Code == 0 && json.RootElement.GetProperty("storageMode").GetString() == currentStorageMode, "CLI storage status reports the active mode");
        string currentDataPath = ConfigService.CurrentDataPath;
        check((await cli(["storage", "--switch", currentStorageMode, "--json"])).Code == 0 && ConfigService.CurrentDataPath == currentDataPath, "CLI storage switch to the active mode keeps the data path");
        check((await cli(["storage", "--switch", "invalid", "--json"])).Code != 0, "CLI storage switch rejects an unknown mode");

        ConfigService.MutateItems((items, recycled) => { items.Clear(); recycled.Clear(); });
        check((await cli(["add", "--title", "CLI 排序甲", "--path", "C:\\EGL-sort-a.exe", "--json"])).Code == 0, "CLI sort fixture item A is added");
        check((await cli(["add", "--title", "CLI 排序乙", "--path", "C:\\EGL-sort-b.exe", "--json"])).Code == 0, "CLI sort fixture item B is added");
        check((await cli(["add", "--title", "CLI 排序丙", "--path", "C:\\EGL-sort-c.exe", "--json"])).Code == 0, "CLI sort fixture item C is added");
        var sortItems = ConfigService.LoadItems();
        var sortFirst = sortItems[0];
        var sortSecond = sortItems[1];
        var sortThird = sortItems[2];
        string SortOrder() => string.Join(",", ConfigService.LoadItems().Select(x => x.Title));
        check((await cli(["sort", "--id", sortFirst.Id, "--move-down", "--json"])).Code == 0 && SortOrder() == "CLI 排序乙,CLI 排序甲,CLI 排序丙", "CLI sort move down reorders and persists");
        check((await cli(["sort", "--id", sortThird.Id, "--move-up", "--json"])).Code == 0 && SortOrder() == "CLI 排序乙,CLI 排序丙,CLI 排序甲", "CLI sort move up reorders and persists");
        check((await cli(["sort", "--id", sortSecond.Id, "--swap-with", sortFirst.Id, "--json"])).Code == 0 && SortOrder() == "CLI 排序甲,CLI 排序丙,CLI 排序乙", "CLI sort swap exchanges two entries and persists");
        var listText = await cli(["list"]);
        check(listText.Code == 0 && listText.Output.Contains("CLI 排序甲"), "CLI list text output includes item titles");

        string cmdExe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        check((await cli(["launch", "--path", cmdExe + " /c exit 0", "--json"])).Code == 0, "CLI launch starts an existing executable with arguments");
        check((await cli(["launch", "--path", "C:\\EGL-missing-launch-target.exe", "--json"])).Code != 0, "CLI launch fails for a missing target");

        ConfigService.MutateItems((items, recycled) => { items.Clear(); recycled.Clear(); });
    }
}
