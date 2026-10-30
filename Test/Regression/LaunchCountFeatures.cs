using EricGameLauncher;
using System.Text.Json;

internal static class LaunchCountFeatures
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var item = ItemService.CreateItem("Count cache fixture", @"C:\EGL-cache-fixture-missing.exe");
        ItemService.AddItem(item);
        string original = File.ReadAllText(ConfigService.ItemsFilePath);
        DateTime stamp = File.GetLastWriteTimeUtc(ConfigService.ItemsFilePath);
        try
        {
            check(!original.Contains("launchCount:"), "project configuration contains no launch statistics");
            await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => LaunchCountCache.Increment(item.Id))));
            LaunchCountCache.Populate([item]);
            check(item.LaunchCount == 24, "concurrent count updates are not lost");
            var persisted = JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(LaunchCountCache.CacheFilePath))!;
            check(persisted[item.Id] == 24, "statistics cache stores stable item ID and count");
            check(File.ReadAllText(ConfigService.ItemsFilePath) == original && File.GetLastWriteTimeUtc(ConfigService.ItemsFilePath) == stamp, "count increments never rewrite project configuration");
            check(ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount == 24, "loading items hydrates statistics from cache");
            ItemService.EditItem(item, x => x.Title = "Edited count fixture");
            check(ConfigService.LoadItems().Single(x => x.Id == item.Id).LaunchCount == 24 && !File.ReadAllText(ConfigService.ItemsFilePath).Contains("launchCount:"), "editing properties leaves counts in independent cache");
            File.Delete(LaunchCountCache.CacheFilePath);
            LaunchCountCache.Populate([item]);
            check(item.LaunchCount == 0, "deleting disposable cache resets counts without changing project configuration");
            AtomicFile.Write(LaunchCountCache.CacheFilePath, "invalid-json");
            check(LaunchCountCache.Increment(item.Id) == 1, "invalid statistics cache is safely rebuilt");
        }
        finally { ItemService.RemoveItem(item.Id, null, true); }
    }
}
