using EricGameLauncher;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class P3Features
{
    internal static async Task RunAsync(Action<bool, string> check, string root, string fixture)
    {
        await ShortcutAsync(check, root, fixture);
        await CommandsAsync(check, root, fixture);
        AdminOverrides(check);
        GoogleShortcuts(check);
    }

    private static async Task ShortcutAsync(Action<bool, string> check, string root, string fixture)
    {
        string workingDirectory = Path.Combine(root, "P3 shortcut working directory");
        Directory.CreateDirectory(workingDirectory);
        string capture = Path.Combine(root, "p3-shortcut-capture.json");
        string shortcut = Path.Combine(root, "P3 shortcut with arguments.lnk");
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(shortcut);
            try
            {
                link.TargetPath = fixture;
                link.Arguments = $"--capture \"{capture}\" \"two words\" \"a\\\"b\"";
                link.WorkingDirectory = workingDirectory;
                link.Save();
            }
            finally { Marshal.ReleaseComObject(link); }
        }
        finally { Marshal.ReleaseComObject(shell); }
        var resolved = ShortcutResolver.ResolveTargetPath(shortcut);
        check(resolved.ActualPath == shortcut && resolved.Info?.Arguments?.Contains("two words") == true, "P3 shortcut import retains original launch target and metadata");
        var item = ItemService.CreateItem("P3 shortcut", resolved.ActualPath);
        ItemService.AddItem(item);
        try
        {
            var saved = ItemService.FindItem(item.Id, null)!;
            check((await LaunchService.LaunchAsync(saved)).Success, "P3 persisted shortcut launches through normal item service");
            using var result = await ReadCaptureAsync(capture);
            check(result.RootElement.GetProperty("Arguments").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "two words", "a\"b" }), "P3 shortcut launch preserves quoted arguments");
            check(string.Equals(result.RootElement.GetProperty("WorkingDirectory").GetString(), workingDirectory, StringComparison.OrdinalIgnoreCase), "P3 shortcut launch preserves working directory");
        }
        finally { ItemService.RemoveItem(item.Id, null, true); }

        string platformShortcut = Path.Combine(root, "P3 platform game.lnk");
        shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(platformShortcut);
            try
            {
                link.TargetPath = Path.Combine(root, "Steam", "steam.exe");
                link.Arguments = "-applaunch 570";
                link.Save();
            }
            finally { Marshal.ReleaseComObject(link); }
        }
        finally { Marshal.ReleaseComObject(shell); }
        check(!PlatformRecognition.IsPlatform(platformShortcut), "P3 Steam game shortcut arguments prevent platform-manager classification");
    }

    private static async Task CommandsAsync(Action<bool, string> check, string root, string fixture)
    {
        check(ProcessRunner.SplitPath("cmd.exe /d /c exit 0") == ("cmd.exe", "/d /c exit 0"), "P3 bare command separates arguments");
        check(ProcessRunner.SplitPath("cmd.exe\t/d /c exit 0") == ("cmd.exe", "/d /c exit 0"), "P3 tab separates command arguments");
        check(ProcessRunner.SplitPath("missing-p3.exe --flag") == ("missing-p3.exe", "--flag"), "P3 missing bare target keeps a distinct argument list");
        check(!ProcessRunner.Run("missing-p3.exe --flag", false).Success, "P3 missing command reports launch failure");
        string spaced = Path.Combine(root, "P3 complete filename with spaces.exe");
        File.WriteAllText(spaced, "never executed");
        File.WriteAllText(Path.Combine(root, "P3"), "ambiguous shorter prefix");
        check(ProcessRunner.SplitPath(spaced) == (spaced, ""), "P3 complete existing filename takes priority over shorter prefixes");
        check(ProcessRunner.SplitPath(spaced + " --flag \"two words\"") == (spaced, "--flag \"two words\""), "P3 unquoted existing filename retains argument quoting");
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        string capture = Path.Combine(root, "p3-path-command.json");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(fixture) + ";" + originalPath);
            string command = Path.GetFileName(fixture) + $" --capture \"{capture}\" \"PATH command argument\"";
            check(ProcessRunner.Run(command, false).Success, "P3 PATH command launches with arguments");
            using var result = await ReadCaptureAsync(capture);
            check(result.RootElement.GetProperty("Arguments")[0].GetString() == "PATH command argument", "P3 PATH command receives quoted argument");
        }
        finally { Environment.SetEnvironmentVariable("PATH", originalPath); }
    }

    private static void AdminOverrides(Action<bool, string> check)
    {
        var item = ItemService.CreateItem("P3 admin policy", "main.exe");
        item.AlternativeLaunchCommand = "alt.exe";
        item.AlongsideCommand = "alongside.exe";
        item.IsAlongsideAdmin = false;
        check(LaunchService.GetLaunchTarget(item) == ("main.exe", false), "P3 stored main privilege is the default");
        check(LaunchService.GetLaunchTarget(item, adminOverride: true) == ("main.exe", true), "P3 explicit main elevation overrides saved privilege");
        check(LaunchService.GetLaunchTarget(item, true, true) == ("alt.exe", true), "P3 explicit elevation applies to selected alternative");
        check(LaunchService.GetAlongsideTarget(item, true) == ("alongside.exe", false) && !item.IsAdmin && !item.IsAltAdmin, "P3 temporary elevation preserves alongside privilege and item settings");
        item.IsAdmin = true;
        check(LaunchService.GetLaunchTarget(item) == ("main.exe", true) && LaunchService.GetLaunchTarget(item, adminOverride: false) == ("main.exe", false), "P3 default saved admin mode and explicit false are distinct");
        check(LaunchService.GetLaunchTarget(item, true) == ("alt.exe", false), "P3 alternative retains its independent saved privilege");
    }

    private static void GoogleShortcuts(Action<bool, string> check)
    {
        string[] targets =
        [
            "googleplaygames://launch/?ID=com.example.one&source=shortcut",
            "googleplaygames://launch/?extra=1&id=com%2Eexample.one",
            "googleplaygames://launch/?id=com.example.two",
            "googleplaygames://launch/?missing=one",
            "not a URI"
        ];
        List<ScannedGame> Games() =>
        [
            new() { Title = "One", ExePath = "googleplaygames://launch/?id=com.example.one" },
            new() { Title = "Two", ExePath = "googleplaygames://launch/?id=com.example.two" },
            new() { Title = "Three", ExePath = "googleplaygames://launch/?id=com.example.three" }
        ];
        var games = Games();
        GooglePlayGamesHelper.ApplyShortcutTargets(games, targets);
        check(targets.Take(2).Contains(games[0].ExePath) && games[1].ExePath == targets[2], "P3 Google shortcuts match mixed-case ID keys and decoded package IDs");
        check(games[2].ExePath == "googleplaygames://launch/?id=com.example.three", "P3 missing Google shortcut leaves installed launch target available");
        var reversed = Games();
        GooglePlayGamesHelper.ApplyShortcutTargets(reversed, targets.Reverse());
        check(games.Select(x => x.ExePath).SequenceEqual(reversed.Select(x => x.ExePath)), "P3 Google duplicate shortcut choice is independent of enumeration order");
        GooglePlayGamesHelper.ApplyShortcutTargets(games, targets);
        check(games.Select(x => x.ExePath).SequenceEqual(reversed.Select(x => x.ExePath)), "P3 Google matching can repeat after mixed-case target replacement");
    }

    private static async Task<JsonDocument> ReadCaptureAsync(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            try { return JsonDocument.Parse(File.ReadAllText(path)); }
            catch (IOException) { }
            catch (JsonException) { }
            await Task.Delay(25);
        }
        throw new TimeoutException("Launch fixture did not produce capture data");
    }
}
