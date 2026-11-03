using EricGameLauncher;
using System.IO.Compression;
using System.Text.Json;
using updater.main;

internal static class P1Features
{
    public static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        var item = ItemService.CreateItem("P1 original", "p1-fixture://launch");
        ItemService.AddItem(item);
        var baseline = ItemService.Snapshot(item);
        var draft = ItemService.Snapshot(item);
        draft.IsAdmin = true;
        var result = await cli(["edit", "--id", item.Id, "--title", "P1 external"]);
        await ItemService.SaveDraftAsync(baseline, draft);
        var saved = ItemService.FindItem(item.Id, null)!;
        check(result.Code == 0 && saved.Title == "P1 external" && saved.IsAdmin, "P1 stale GUI draft preserves independent CLI edit");
        baseline = ItemService.Snapshot(saved);
        draft = ItemService.Snapshot(saved);
        draft.Title = "GUI conflicting title";
        await cli(["edit", "--id", item.Id, "--title", "CLI conflicting title"]);
        bool conflict = false;
        try { await ItemService.SaveDraftAsync(baseline, draft); }
        catch (InvalidOperationException) { conflict = true; }
        check(conflict && ItemService.FindItem(item.Id, null)!.Title == "CLI conflicting title", "P1 same-field conflict rejects stale draft");
        await ItemService.EditItemAsync(item, x => x.Pin = true);
        check(ItemService.FindItem(item.Id, null)!.Title == "CLI conflicting title", "P1 async callback edits latest item");
        ItemService.EditItem(item, x => x.IsAdmin = false);
        check(ItemService.FindItem(item.Id, null)!.Pin, "P1 synchronous callback preserves latest fields");
        await ItemService.EditItemAsync(item, x =>
        {
            x.MgrPath = "p1-manager://launch";
            ItemService.EditItem(ItemService.FindItem(item.Id, null)!, current => current.Title = "Changed before async commit");
        });
        saved = ItemService.FindItem(item.Id, null)!;
        check(saved.Title == "Changed before async commit" && saved.MgrPath == "p1-manager://launch", "P1 async preparation retains intervening transaction");
        baseline = ItemService.Snapshot(item);
        draft = ItemService.Snapshot(item);
        ItemService.RemoveItem(item.Id, null, true);
        bool missing = false;
        try { await ItemService.SaveDraftAsync(baseline, draft); }
        catch (InvalidOperationException) { missing = true; }
        check(missing && ItemService.FindItem(item.Id, null) == null, "P1 draft cannot resurrect deleted item");

        foreach (int count in new[] { 0, 1, 10, 11, 25 })
        {
            var menus = Enumerable.Range(0, count).Select(i => new CustomMenuItem { Title = "Menu " + i, Command = "command " + i, IsAdmin = i % 2 == 0 }).ToList();
            var merged = ItemService.ReplaceVisibleMenus(menus, menus.Take(10), 10);
            check(JsonSerializer.Serialize(menus) == JsonSerializer.Serialize(merged), $"P1 {count} menus survive visible-row save");
            var menuItem = ItemService.CreateItem("Menu round trip", "p1-menu://" + count);
            menuItem.CustomMenuItems = menus;
            ItemService.AddItem(menuItem);
            var menuBaseline = ItemService.Snapshot(menuItem);
            var menuDraft = ItemService.Snapshot(menuItem);
            menuDraft.Title = "Changed title";
            menuDraft.CustomMenuItems = merged;
            await ItemService.SaveDraftAsync(menuBaseline, menuDraft);
            check(JsonSerializer.Serialize(ItemService.FindItem(menuItem.Id, null)!.CustomMenuItems) == JsonSerializer.Serialize(menus), $"P1 {count} persisted menus retain content and order");
            ItemService.RemoveItem(menuItem.Id, null, true);
        }

        var unusualMenus = new List<CustomMenuItem> { new() { Title = null, Command = "  command with spaces  ", IsAdmin = true }, new() { Command = "" } };
        var visibleMenus = unusualMenus.Select(x => new CustomMenuItem { Title = x.Title ?? "", Command = x.Command ?? "", IsAdmin = x.IsAdmin });
        check(JsonSerializer.Serialize(ItemService.ReplaceVisibleMenus(unusualMenus, visibleMenus, 10)) == JsonSerializer.Serialize(unusualMenus), "P1 unchanged menu rows preserve whitespace and empty historical entries");

        string install = Path.Combine(root, "p1-legacy-install");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "EricGameLauncher.exe"), "old");
        File.WriteAllText(Path.Combine(install, "unknown.dll"), "keep");
        using var bytes = new MemoryStream();
        using (var package = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(package.CreateEntry("EricGameLauncher.exe").Open())) writer.Write("new");
        bytes.Position = 0;
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        UpdateTransaction.Apply(install, archive);
        check(File.ReadAllText(Path.Combine(install, "unknown.dll")) == "keep" && File.ReadAllText(Path.Combine(install, "EricGameLauncher.exe")) == "new", "P1 legacy update preserves unknown files and replaces package members");
        check(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(install, UpdateTransaction.ManifestName)))!.SequenceEqual(new[] { "EricGameLauncher.exe" }), "P1 update records managed files");
        Directory.CreateDirectory(Path.Combine(install, "Data"));
        string dataFile = Path.Combine(install, "Data", "keep.txt");
        File.WriteAllText(dataFile, "user data");
        using var protectedBytes = new MemoryStream();
        using (var package = new ZipArchive(protectedBytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(package.CreateEntry("EricGameLauncher.exe").Open())) writer.Write("new");
            using (var writer = new StreamWriter(package.CreateEntry("nested/../Data/keep.txt").Open())) writer.Write("overwrite attempt");
        }
        protectedBytes.Position = 0;
        using (var protectedArchive = new ZipArchive(protectedBytes, ZipArchiveMode.Read)) UpdateTransaction.Apply(install, protectedArchive);
        check(File.ReadAllText(dataFile) == "user data", "P1 normalized package paths preserve Data");
        string manifestPath = Path.Combine(install, UpdateTransaction.ManifestName);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new[] { "nested/../Data/keep.txt" }));
        bool invalidManifest = false;
        using (var retry = new ZipArchive(new MemoryStream(bytes.ToArray()), ZipArchiveMode.Read))
        {
            try { UpdateTransaction.Apply(install, retry); }
            catch (InvalidDataException) { invalidManifest = true; }
        }
        check(invalidManifest && File.ReadAllText(dataFile) == "user data", "P1 normalized manifest paths cannot claim Data");
    }
}
