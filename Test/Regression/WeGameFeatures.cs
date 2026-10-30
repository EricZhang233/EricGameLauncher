using EricGameLauncher;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class WeGameFeatures
{
    private const string IdCipher = "5E2709F7A57E2BE1C03FD3796620327275954833F6F60014";
    private const string RootCipher = "92C66C4D3014696B716025E074AF8C93345649FF164941149993ABB6E8075EE0A3C133F419D9B59F3980C88AD76426E4C315BD4A31CE414F91B8E09210149B4552C67B70154119D7";

    private static object Invoke(string name, params object[] args) =>
        typeof(WeGameHelper).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;

    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli, string root)
    {
        byte[] cipher = Convert.FromHexString(IdCipher);
        check(Encoding.UTF8.GetString((byte[])Invoke("Decrypt", cipher)) == "26,2001918", "WeGame QQTEA decodes actual client installation list vector");
        var roots = (string[])Invoke("ParseLibraryRoots", Encoding.UTF8.GetString((byte[])Invoke("Decrypt", Convert.FromHexString(RootCipher))));
        check(roots.SequenceEqual(new[] { @"C:\Users\eric\Storage\Program\Lib\WeGameApps" }), "WeGame actual path vector parses JSON and normalizes separators");
        foreach (var bad in new[] { cipher[..^1], new byte[8], cipher.Select((b, i) => i == cipher.Length - 1 ? (byte)(b ^ 1) : b).ToArray() })
        {
            bool rejected = false;
            try { Invoke("Decrypt", bad); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
            check(rejected, "WeGame rejects truncated or corrupt encrypted manifest");
        }
        check(((HashSet<ulong>)Invoke("ParseInstalledIds", "26,2001918,26\n")).Count == 2, "WeGame manifest IDs are deduplicated");
        check(((HashSet<ulong>)Invoke("ParseInstalledIds", "\n")).Count == 0, "WeGame empty installation list is valid");
        foreach (string bad in new[] { "26,,2001918", "0", "-1", "18446744073709551616" })
        {
            bool rejected = false;
            try { Invoke("ParseInstalledIds", bad); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
            check(rejected, "WeGame malformed ID list is not accepted as successful scan");
        }
        string fixture = Path.Combine(root, "wegame");
        Directory.CreateDirectory(fixture);
        string database = Path.Combine(fixture, "WeGameCfg.info");
        Sql(database, $"CREATE TABLE Setting(key TEXT PRIMARY KEY,data BLOB);INSERT INTO Setting VALUES('GameCfgMgr.InstalledGameList',X'{IdCipher}');INSERT INTO Setting VALUES('CfgMgr.UserSysSetting.InstallPathMap',X'{RootCipher}');CREATE TABLE AppLibrary(AppLibraryState BLOB);INSERT INTO AppLibrary VALUES(X'010203');");
        byte[] hash = SHA256.HashData(File.ReadAllBytes(database));
        var stamp = File.GetLastWriteTimeUtc(database);
        var manifest = ((HashSet<ulong> ids, string[] roots))Invoke("ReadManifest", database);
        check(manifest.ids.SetEquals(new ulong[] { 26, 2001918 }) && manifest.roots.SequenceEqual(roots), "WeGame production SQLite reader reads and decrypts both manifest fields");
        var googleRead = typeof(GooglePlayGamesHelper).GetMethod("ReadLibrary", BindingFlags.NonPublic | BindingFlags.Static)!;
        check(((byte[])googleRead.Invoke(null, [database])!).SequenceEqual(new byte[] { 1, 2, 3 }), "shared SQLite reader preserves Google library blob reading");
        check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))) && stamp == File.GetLastWriteTimeUtc(database), "library SQLite reads never rewrite client database");
        string library = Path.Combine(fixture, "Library");
        string MakeGame(string name, string identity, bool launcher = true)
        {
            string directory = Path.Combine(library, name);
            Directory.CreateDirectory(Path.Combine(directory, "rail_files"));
            File.WriteAllText(Path.Combine(directory, "rail_files", "rail_game_identify.json"), identity);
            if (launcher)
            {
                Directory.CreateDirectory(Path.Combine(directory, "WeGameLauncher"));
                File.WriteAllBytes(Path.Combine(directory, "WeGameLauncher", "launcher.exe"), []);
            }
            return directory;
        }
        string alpha = MakeGame("Alpha(26)", "{\"game_id\":26}");
        string beta = MakeGame("Beta(2001918)", "{\"game_id\":\"2001918\"}");
        MakeGame("Unlisted", "{\"game_id\":999998}");
        MakeGame("Invalid", "not json");
        MakeGame("MissingLauncher", "{\"game_id\":26}", false);
        var games = (List<ScannedGame>)Invoke("ScanLibraries", manifest.ids, new[] { library, library, Path.Combine(fixture, "absent") }, @"C:\Apps\WeGame\wegame.exe");
        check(games.Count == 2 && games.All(g => g.PlatformBadge == "WeGame"), "WeGame only includes installed IDs with identity and launcher, and deduplicates roots");
        check(games.Select(g => g.Title).Order().SequenceEqual(new[] { "Alpha", "Beta" }), "WeGame display names remove only the matching ID suffix");
        check(GamePlatformHelper.DetectPlatform(games[0].ExePath)?.PlatformName == "WeGame", "WeGame launcher is recognized by property platform badge");
        string alternate = Path.Combine(alpha, "AlphaClient.exe");
        File.WriteAllBytes(alternate, []);
        var existing = ItemService.CreateItem("Existing localized title", alternate);
        ItemService.AddItem(existing);
        try
        {
            var classified = ScanService.Classify(games, [existing]);
            check(classified.existingGames.Count == 1 && classified.newGames.Count == 1, "WeGame classification matches existing alternate executable in same game directory");
            check(ItemService.ImportGames(games) == 1, "WeGame import avoids duplicate game with a different executable");
            var imported = ConfigService.LoadItems().Single(i => i.ExePath == Path.Combine(beta, "WeGameLauncher", "launcher.exe"));
            check(imported.PlatformName == "WeGame" && imported.MgrPath == @"C:\Apps\WeGame\wegame.exe" && !imported.IsPlatform, "WeGame import persists game badge and manager without marking game as platform");
            ItemService.RemoveItem(imported.Id, null, true);
            string neighbor = alpha + "Other" + Path.DirectorySeparatorChar + "game.exe";
            check(!(bool)Invoke("MatchesLaunchPath", games.Single(g => g.Title == "Alpha"), neighbor), "WeGame duplicate matching respects directory boundary");
        }
        finally { ItemService.RemoveItem(existing.Id, null, true); }
        Sql(database, "DELETE FROM Setting WHERE key='CfgMgr.UserSysSetting.InstallPathMap';");
        bool missingRejected = false;
        try { Invoke("ReadManifest", database); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { missingRejected = true; }
        check(missingRejected, "WeGame missing library root field is a scan failure");
        var help = await cli(["scan", "--help"]);
        check(help.Code == 0 && help.Output.Contains("--wegame"), "CLI scan help advertises WeGame manifest scanning");
    }

    private static void Sql(string path, string sql)
    {
        IntPtr db = IntPtr.Zero, error = IntPtr.Zero;
        try
        {
            if (sqlite3_open_v2(path, out db, 6, IntPtr.Zero) != 0 || sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out error) != 0) throw new InvalidDataException();
        }
        finally { if (error != IntPtr.Zero) sqlite3_free(error); if (db != IntPtr.Zero) sqlite3_close(db); }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr context, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr memory);
}
