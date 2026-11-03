using EricGameLauncher;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using updater.main;

internal static class P2Features
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        await SettingsAsync(check, cli);
        await UpdateLeaseAsync(check, root);
    }

    private static async Task SettingsAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli)
    {
        string settingsPath = ConfigService.SettingsFilePath;
        string itemsPath = ConfigService.ItemsFilePath;
        string originalSettings = File.ReadAllText(settingsPath);
        string originalItems = File.ReadAllText(itemsPath);
        try
        {
            foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "", "31", "513", "32,5" })
            {
                string before = File.ReadAllText(settingsPath);
                var result = await cli(["settings", "--set", "iconSize=" + value, "--json"]);
                using var json = JsonDocument.Parse(result.Output);
                check(result.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean() && File.ReadAllText(settingsPath) == before, "P2 invalid icon size rejected without writing " + value);
            }
            foreach (string value in new[] { "32", "512" })
            {
                check((await cli(["settings", "--set", "iconSize=" + value, "--json"])).Code == 0, "P2 icon size boundary accepted " + value);
            }
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                check(ConfigService.SetSetting("iconSize", "32.5") == null && ConfigService.IconSize == 32.5, "P2 icon size parsing uses invariant decimal point");
            }
            finally { CultureInfo.CurrentCulture = culture; }

            foreach (string value in new[] { ".nan", ".inf", "-.inf", "31", "513" })
            {
                File.WriteAllText(settingsPath, "iconSize: " + value + "\n");
                ConfigService.Reload();
                check(ConfigService.IconSize == 118, "P2 invalid stored icon size falls back " + value);
                var result = await cli(["settings", "--list", "--json"]);
                using var json = JsonDocument.Parse(result.Output);
                check(result.Code == 0 && json.RootElement.GetProperty("iconSize").GetDouble() == 118, "P2 invalid stored size still permits JSON settings " + value);
            }

            File.WriteAllText(settingsPath, originalSettings);
            ConfigService.Reload();
            foreach (int version in new[] { 0, ConfigService.CurrentConfigVersion + 1 })
            {
                string oldItems = "version: " + version + "\nitems: []\nrecycleBinItems: []\n";
                File.WriteAllText(itemsPath, oldItems);
                ConfigService.Reload();
                string beforeTitle = ConfigService.AppTitle;
                string beforeSettings = File.ReadAllText(settingsPath);
                check(ConfigService.SetSetting("appTitle", "must not apply") == "ErrMigrationRequired" && ConfigService.AppTitle == beforeTitle, "P2 migration rejects changes before memory mutation " + version);
                check(!ConfigService.TrySaveAll(), "P2 migration blocks TrySaveAll " + version);
                var result = await cli(["settings", "--set", "appTitle=must not apply", "--json"]);
                using var json = JsonDocument.Parse(result.Output);
                check(result.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean() && File.ReadAllText(settingsPath) == beforeSettings && File.ReadAllText(itemsPath) == oldItems, "P2 migration CLI failure preserves both files " + version);
            }
            File.WriteAllText(itemsPath, originalItems);
            ConfigService.Reload();

            ConfigService.SaveGitHubToken("ghp_P2_INITIAL_FIXTURE");
            foreach (string token in new[] { "ghp_P2_CHANGED_FIXTURE", "" })
            {
                string previous = ConfigService.GitHubToken;
                string before = File.ReadAllText(settingsPath);
                bool failed = false;
                using (var locked = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { ConfigService.SaveGitHubToken(token); }
                    catch (IOException) { failed = true; }
                    check(failed && ConfigService.GitHubToken == previous && File.ReadAllText(settingsPath) == before, "P2 failed token save restores active value");
                }
                ConfigService.SaveGitHubToken(token);
                ConfigService.Reload();
                check(ConfigService.GitHubToken == token, "P2 unchanged token input retries and persists");
                var result = await cli(["settings", "--list", "--json"]);
                check(result.Code == 0 && !result.Output.Contains("ghp_P2_"), "P2 token retry query stays redacted");
            }
        }
        finally
        {
            File.WriteAllText(settingsPath, originalSettings);
            File.WriteAllText(itemsPath, originalItems);
            ConfigService.Reload();
        }
    }

    internal static void HoldUpdate(string install, string ready)
    {
        using var package = Package();
        UpdateTransaction.Apply(install, package, step =>
        {
            if (step != "commit") return;
            File.WriteAllText(ready, "ready");
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(ready + ".release"))
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Lease fixture timed out");
                Thread.Sleep(10);
            }
        });
    }

    private static ZipArchive Package()
    {
        var bytes = new MemoryStream();
        using (var package = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        using (var writer = new StreamWriter(package.CreateEntry("EricGameLauncher.exe").Open())) writer.Write("updated lease fixture");
        bytes.Position = 0;
        return new ZipArchive(bytes, ZipArchiveMode.Read);
    }

    private static async Task UpdateLeaseAsync(Action<bool, string> check, string root)
    {
        string driveRoot = Path.GetPathRoot(root)!;
        check(UpdateTransaction.Contained(driveRoot, "p2-contained-fixture") == Path.Combine(driveRoot, "p2-contained-fixture"), "P2 updater containment supports drive roots without touching them");
        foreach (bool terminate in new[] { false, true })
        {
            string install = Path.Combine(root, "p2-update-lease-" + terminate);
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "EricGameLauncher.exe"), "old lease fixture");
            string ready = Path.Combine(root, "p2-lease-ready-" + terminate);
            var start = ChildStartInfo();
            foreach (string argument in new[] { "--hold-update", install, ready }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            Task<string> output = child.StandardOutput.ReadToEndAsync();
            Task<string> error = child.StandardError.ReadToEndAsync();
            try
            {
                var timeout = Stopwatch.StartNew();
                while (!File.Exists(ready))
                {
                    if (child.HasExited) throw new InvalidOperationException("Lease owner failed to start " + await output + await error);
                    if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Lease owner startup timed out");
                    await Task.Delay(20);
                }
                string journalPath = Path.Combine(install, "._update_backup", "journal.json");
                string before = File.ReadAllText(journalPath);
                bool rejected = false;
                using (var package = Package())
                {
                    try { UpdateTransaction.Apply(install + Path.DirectorySeparatorChar, package); }
                    catch (IOException ex) when (ex.Message == "UPDATE_IN_PROGRESS") { rejected = true; }
                }
                check(rejected && File.ReadAllText(journalPath) == before, "P2 competing updater cannot alter active transaction " + terminate);
                if (terminate) child.Kill(true);
                else File.WriteAllText(ready + ".release", "release");
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                using var retry = Package();
                UpdateTransaction.Apply(install, retry);
                check(File.ReadAllText(Path.Combine(install, "EricGameLauncher.exe")) == "updated lease fixture" && !Directory.Exists(Path.Combine(install, "._update_backup")), "P2 updater takes over after owner exit and recovers " + terminate);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                await Task.WhenAll(output, error);
            }
        }
    }

    internal static ProcessStartInfo ChildStartInfo()
    {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string mainAssembly = typeof(ConfigService).Assembly.Location;
                foreach (string argument in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(mainAssembly, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(mainAssembly, ".deps.json") })
                    start.ArgumentList.Add(argument);
                start.ArgumentList.Add(typeof(P2Features).Assembly.Location);
            }
            return start;
    }
}
