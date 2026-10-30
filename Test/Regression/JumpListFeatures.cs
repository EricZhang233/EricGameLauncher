using EricGameLauncher;
using System.Reflection;
using System.Text;

internal static class JumpListFeatures
{
    internal static void Run(Action<bool, string> check)
    {
        var googleGame = new AppItem { ExePath = "googleplaygames://launch/?id=com.supercell.test&lid=2&pid=1", Platform = "User" };
        check(googleGame.PlatformName == "Google Play Games", "GPG badge resolves protocol over generic User label");
        googleGame.Platform = "Custom";
        check(googleGame.PlatformName == "Custom", "explicit custom platform label is preserved");
        check(new AppItem { ExePath = @"C:\Games\game.exe", Platform = "User" }.PlatformName == "User", "local game keeps generic User badge");
        check(PlatformRecognition.IsPlatform(@"D:\Apps\Steam\steam.exe"), "platform detects Steam on custom drive");
        check(!PlatformRecognition.IsPlatform(@"D:\Games\Something\launcher.exe"), "generic launcher is not platform");
        check(PlatformRecognition.IsPlatform(@"D:\Apps\HoYoPlay\launcher.exe"), "generic launcher requires platform path");
        check(!PlatformRecognition.IsPlatform(@"D:\Steam\steam.exe -applaunch 123"), "game command is not platform");
        var old = new AppItemDto { Id = "migration-test", Actions = new() { Main = new() { Path = @"D:\Steam\steam.exe" } } };
        var inferred = old.ToViewModel(ConfigService.FixedCachePath);
        check(inferred.IsPlatform && inferred.Pin, "missing flags infer platform and default pin");
        old.IsPlatform = false; old.Pin = false;
        var explicitFalse = old.ToViewModel(ConfigService.FixedCachePath);
        check(!explicitFalse.IsPlatform && !explicitFalse.Pin, "explicit false flags survive inference");
        inferred.Pin = false;
        var saved = AppItemDto.FromViewModel(inferred).ToViewModel(ConfigService.FixedCachePath);
        check(saved.IsPlatform && !saved.Pin, "platform unpin persists");
        byte[] Field(int number, byte[] data)
        {
            using var output = new MemoryStream();
            void Varint(uint n) { while (n >= 128) { output.WriteByte((byte)((n & 127) | 128)); n >>= 7; } output.WriteByte((byte)n); }
            Varint((uint)(number << 3 | 2)); Varint((uint)data.Length); output.Write(data); return output.ToArray();
        }
        byte[] Text(int number, string value) => Field(number, Encoding.UTF8.GetBytes(value));
        byte[] Record(string id) => Field(2, [.. Text(1, id), .. Field(2, Field(4, [.. Text(1, id), .. Text(4, "Game")]))]);
        byte[] fixture = Field(12, Field(2, [.. Record("com.supercell.test"), .. Record("com.google.test"), .. Record("com.android.test")]));
        var parser = typeof(GooglePlayGamesHelper).GetMethod("ParseLibrary", BindingFlags.Static | BindingFlags.NonPublic)!;
        var games = (List<ScannedGame>)parser.Invoke(null, [fixture])!;
        check(games.Count == 1 && games[0].ExePath.EndsWith("com.supercell.test"), "GPG parses records and excludes system prefixes");
        bool rejected = false;
        try { parser.Invoke(null, [new byte[] { 0x62, 0x80 }]); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidDataException) { rejected = true; }
        check(rejected, "GPG rejects truncated library instead of reporting empty");
        var pinnedA = new AppItem { Id = "pinnedA", Pin = true };
        var pinnedB = new AppItem { Id = "pinnedB", Pin = true, LaunchCount = 200 };
        var platform = new AppItem { Id = "platform", IsPlatform = true, Pin = true };
        var hiddenPlatform = new AppItem { Id = "hidden", IsPlatform = true, Pin = false };
        var deleted = new AppItem { Id = "deleted", Pin = true, Status = 1 };
        var popular = new AppItem { Id = "popular", LaunchCount = 100 };
        var candidate = new AppItem { Id = "candidate" };
        var pool = new List<AppItem> { candidate, pinnedA, popular, platform, pinnedB, hiddenPlatform, deleted };
        var select = typeof(JumpListService).GetMethod("TaskGroups", BindingFlags.Static | BindingFlags.NonPublic)!;
        var groups = (List<List<AppItem>>)select.Invoke(null, [pool])!;
        check(groups.Count == 2 && groups[1].Take(2).Select(x => x.Id).SequenceEqual(new[] { "pinnedA", "pinnedB" }), "task pins lead frequent section in main view order");
        check(groups[0].Count == 1 && groups[0][0].Id == "platform", "platforms lead task sections and unpinned platforms stay hidden");
        check(groups[1].Skip(2).Select(x => x.Id).SequenceEqual(new[] { "popular", "candidate" }), "frequent tasks follow pins without a separator or duplicate items");
        candidate.Pin = true;
        groups = (List<List<AppItem>>)select.Invoke(null, [pool])!;
        check(groups[1][0].Id == "candidate" && groups[1].Count(x => x.Id == "candidate") == 1, "application pin moves task to frequent top without duplication");
        groups = (List<List<AppItem>>)select.Invoke(null, [new List<AppItem> { hiddenPlatform, deleted }])!;
        check(groups.Count == 0, "empty task sections produce no separator groups");
    }
}
