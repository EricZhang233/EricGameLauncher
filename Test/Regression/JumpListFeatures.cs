using EricGameLauncher;
using System.Reflection;
using System.Runtime.InteropServices;
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
        NativeLinks(check);
    }

    private static void NativeLinks(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                string executable = Path.Combine(AppContext.BaseDirectory, "EricGameLauncher.exe");
                string working = Path.GetDirectoryName(executable)!;
                var first = new NativeJumpList.Task("游戏平台测试", executable, "--jump-launch first", working, executable, 0);
                var invalid = first with { ApplicationPath = "" };
                var second = first with { Title = "Second", Arguments = "--jump-launch second" };
                using var collection = NativeJumpList.CreateCollection([null, first, null, invalid, null, second, null]);
                collection.Array.GetCount(out uint count);
                check(count == 3 && collection.Tasks.Count == 2, "native task collection rejects invalid links and normalizes separators");
                Guid linkId = typeof(NativeJumpList.IShellLink).GUID;
                collection.Array.GetAt(0, ref linkId, out object native);
                try
                {
                    var link = (NativeJumpList.IShellLink)native;
                    var path = new StringBuilder(2048);
                    link.GetPath(path, path.Capacity, IntPtr.Zero, 4);
                    check(string.Equals(path.ToString(), executable, StringComparison.OrdinalIgnoreCase), "native task targets the launcher executable");
                    var arguments = new StringBuilder(2048);
                    link.GetArguments(arguments, arguments.Capacity);
                    check(arguments.ToString() == first.Arguments, "native task retains the shared jump-launch command");
                    var directory = new StringBuilder(2048);
                    link.GetWorkingDirectory(directory, directory.Capacity);
                    check(string.Equals(directory.ToString(), working, StringComparison.OrdinalIgnoreCase), "native task retains the installation working directory");
                    var icon = new StringBuilder(2048);
                    link.GetIconLocation(icon, icon.Capacity, out int index);
                    check(string.Equals(icon.ToString(), executable, StringComparison.OrdinalIgnoreCase) && index == 0, "native task retains its icon location and index");
                    var key = NativeJumpList.TitleKey;
                    ((NativeJumpList.IPropertyStore)native).GetValue(ref key, out var title);
                    try { check(title.Type == 31 && Marshal.PtrToStringUni(title.Pointer) == first.Title, "native shell title preserves Unicode"); }
                    finally { Marshal.ThrowExceptionForHR(NativeJumpList.PropVariantClear(ref title)); }
                    NativeJumpList.SetAppId(native, JumpListService.AppId);
                    key = new NativeJumpList.PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
                    ((NativeJumpList.IPropertyStore)native).GetValue(ref key, out var appId);
                    try { check(Marshal.PtrToStringUni(appId.Pointer) == JumpListService.AppId, "native links retain the application identity"); }
                    finally { Marshal.ThrowExceptionForHR(NativeJumpList.PropVariantClear(ref appId)); }
                }
                finally { Marshal.ReleaseComObject(native); }
                collection.Array.GetAt(1, ref linkId, out native);
                try
                {
                    var key = NativeJumpList.SeparatorKey;
                    ((NativeJumpList.IPropertyStore)native).GetValue(ref key, out var separator);
                    try { check(separator.Type == 11 && separator.Boolean == -1, "native separator uses the shell destination-list property"); }
                    finally { Marshal.ThrowExceptionForHR(NativeJumpList.PropVariantClear(ref separator)); }
                }
                finally { Marshal.ReleaseComObject(native); }
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Native jump-list fixture timed out.");
        if (failure != null) throw new InvalidOperationException("Native jump-list fixture failed.", failure);
    }
}
