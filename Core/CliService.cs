using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EricGameLauncher;

public static class CliService
{
    private static bool _jsonMode = false;
    private static string _lang = "Zh-CN";

    public static async Task<int> RunAsync(string[] rawArgs)
    {
        try
        {
            StartupArgs.Parse();
            ConfigService.Initialize();
            Text.Load(_lang);
            ServerConfigManager.LoadReadIds();

            var args = rawArgs.Skip(1).Where(a => !string.Equals(a, "-debug", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (args.Length == 0)
            {
                WriteHelp();
                return 0;
            }

            var command = args[0].ToLowerInvariant();
            var commandArgs = args.Skip(1).ToArray();

            if (command == "-help" || command == "--help" || command == "-h" || command == "/?" || command == "help")
            {
                WriteHelp();
                return 0;
            }

            if (commandArgs.Contains("--help") || commandArgs.Contains("-h"))
            {
                WriteCommandHelp(command);
                return 0;
            }

            _jsonMode = commandArgs.Contains("--json");

            var parsed = ParseOptions(commandArgs);

            return command switch
            {
                "list" => await CmdList(parsed),
                "launch" => await CmdLaunch(parsed),
                "platform" => await CmdLaunchPlatform(parsed),
                "add" => await CmdAdd(parsed),
                "edit" => await CmdEdit(parsed),
                "remove" => await CmdRemove(parsed),
                "restore" => await CmdRestore(parsed),
                "recycle" => await CmdRecycle(parsed),
                "scan" => await CmdScan(parsed),
                "search" => await CmdSearch(parsed),
                "sort" => await CmdSort(parsed),
                "settings" => await CmdSettings(parsed),
                "update" => await CmdUpdate(parsed),
                "announcements" => await CmdAnnouncements(parsed),
                "install" => await CmdInstall(parsed),
                "uninstall" => await CmdUninstall(parsed),
                "storage" => await CmdStorage(parsed),
                "skill" => CmdSkill(),
                "version" => CmdVersion(),
                _ => WriteHelp(string.Format(Text.Cli("ErrUnknownCommand"), command))
            };
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CliService.RunAsync failed", ex); } catch { }
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--json") continue;

            if (arg.StartsWith("--"))
            {
                var key = arg.TrimStart('-');
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    opts[key] = args[i + 1];
                    i++;
                }
                else
                {
                    opts[key] = "true";
                }
            }
            else if (arg.StartsWith("-") && arg.Length == 2 && arg != "--")
            {
                var key = arg.TrimStart('-');
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                {
                    opts[key] = args[i + 1];
                    i++;
                }
                else
                {
                    opts[key] = "true";
                }
            }
            else
            {
                if (!opts.ContainsKey("_positional"))
                    opts["_positional"] = arg;
                else
                    opts["_positional"] += " " + arg;
            }
        }
        return opts;
    }

    private static void WriteLine(string text = "", ConsoleColor? color = null)
    {
        try
        {
            if (color.HasValue) Console.ForegroundColor = color.Value;
            Console.WriteLine(text);
            if (color.HasValue) Console.ResetColor();
        }
        catch { }
    }

    private static void Write(string text, ConsoleColor? color = null)
    {
        try
        {
            if (color.HasValue) Console.ForegroundColor = color.Value;
            Console.Write(text);
            if (color.HasValue) Console.ResetColor();
        }
        catch { }
    }

    private static void WriteJson(object obj)
    {
        try
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            Console.WriteLine(json);
        }
        catch { }
    }

    private static int WriteHelp(string? error = null)
    {
        if (!string.IsNullOrEmpty(error))
        {
            WriteLine(error, ConsoleColor.Red);
            WriteLine();
        }
        WriteLine(Text.Cli("Help_Title"), ConsoleColor.Cyan);
        WriteLine(string.Format(Text.Cli("Help_VersionLine"), AppVersion.Version));
        WriteLine();
        WriteLine(Text.Cli("Help_Usage"), ConsoleColor.Yellow);
        WriteLine(Text.Cli("Help_Footer"));
        WriteLine(Text.Cli("Help_OptionSyntax"));
        WriteLine();
        WriteLine(Text.Cli("Help_CommandsHeader"));
        foreach (var cmd in Commands)
            WriteLine(Text.Cli(cmd.DescKey));
        WriteLine();
        WriteLine(Text.Cli("Help_GlobalOptionsHeader"));
        WriteLine(Text.Cli("Help_OptionDebug"));
        WriteLine(Text.Cli("Help_OptionHelp"));
        WriteLine(Text.Cli("Help_OptionJson"));
        WriteLine();
        return 0;
    }

    private static int WriteCommandHelp(string command)
    {
        string? helpText = command switch
        {
            "list" => Text.Cli("Help_List_Text"),
            "launch" => Text.Cli("Help_Launch_Text"),
            "platform" => Text.Cli("Help_Platform_Text"),
            "add" => Text.Cli("Help_Add_Text"),
            "edit" => Text.Cli("Help_Edit_Text"),
            "remove" => Text.Cli("Help_Remove_Text"),
            "restore" => Text.Cli("Help_Restore_Text"),
            "recycle" => Text.Cli("Help_Recycle_Text"),
            "scan" => Text.Cli("Help_Scan_Text"),
            "search" => Text.Cli("Help_Search_Text"),
            "settings" => Text.Cli("Help_Settings_Text"),
            "update" => Text.Cli("Help_Update_Text"),
            "sort" => Text.Cli("Help_Sort_Text"),
            "announcements" => Text.Cli("Help_Announcements_Text"),
            "install" => Text.Cli("Help_Install_Text"),
            "uninstall" => Text.Cli("Help_Uninstall_Text"),
            "storage" => Text.Cli("Help_Storage_Text"),
            "skill" => Text.Cli("Help_Skill_Text"),
            "version" => Text.Cli("Help_Version_Text"),
            _ => null
        };

        if (helpText != null)
        {
            WriteLine(helpText, ConsoleColor.Yellow);
        }
        else
        {
            WriteLine(string.Format(Text.Cli("ErrUnknownCommand"), command), ConsoleColor.Red);
            WriteLine(Text.Cli("Help_UnknownCmd"));
        }
        return 0;
    }

    private static async Task<int> CmdList(Dictionary<string, string> opts)
    {
        try
        {
            bool recycle = opts.ContainsKey("recycle");
            if (recycle)
            {
                var items = ConfigService.LoadRecycleBinItems();
                if (_jsonMode)
                {
                    WriteJson(new { recycleBinItems = items.Select(ItemToJson) });
                }
                else
                {
                    if (items.Count == 0)
                    {
                        WriteLine(Text.Cli("MsgRecycleEmpty"), ConsoleColor.DarkGray);
                        return 0;
                    }
                    WriteLine(string.Format(Text.Cli("MsgRecycleBinHeader"), items.Count), ConsoleColor.Cyan);
                    WriteLine(new string('-', 60));
                    foreach (var item in items)
                    {
                        WriteLine(string.Format(Text.Cli("FmtItemEntry"), item.Id, item.Title ?? Text.Cli("LblUntitled")));
                        WriteLine(string.Format(Text.Cli("FmtItemPath"), item.ExePath ?? Text.Cli("LblNA")));
                        if (item.Status == (int)AppItemStatus.PendingDeletion)
                            WriteLine(Text.Cli("LblStatusPending"), ConsoleColor.DarkYellow);
                        else
                            WriteLine(Text.Cli("LblStatusRecycled"));
                    }
                }
            }
            else
            {
                var items = ConfigService.LoadItems();
                if (_jsonMode)
                {
                    WriteJson(new { items = items.Select(ItemToJson) });
                }
                else
                {
                    if (items.Count == 0)
                    {
                        WriteLine(Text.Cli("MsgItemsEmpty"), ConsoleColor.DarkGray);
                        return 0;
                    }
                    WriteLine(string.Format(Text.Cli("MsgItemsHeader"), items.Count), ConsoleColor.Cyan);
                    WriteLine(new string('-', 60));
                    foreach (var item in items)
                    {
                        WriteLine(string.Format(Text.Cli("FmtItemEntry"), item.Id, item.Title ?? Text.Cli("LblUntitled")));
                        WriteLine(string.Format(Text.Cli("FmtItemPath"), item.ExePath ?? Text.Cli("LblNA")));
                        if (!string.IsNullOrEmpty(item.PlatformName))
                            WriteLine(string.Format(Text.Cli("FmtItemPlatform"), item.PlatformName));
                        if (item.IsAdmin)
                            WriteLine(Text.Cli("LblAdmin"), ConsoleColor.DarkYellow);
                    }
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrListItemsFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdList failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdLaunch(Dictionary<string, string> opts)
    {
        try
        {
            var id = opts.GetValueOrDefault("id");
            var title = opts.GetValueOrDefault("title");
            var path = opts.GetValueOrDefault("path");
            var admin = opts.ContainsKey("admin");
            var alt = opts.ContainsKey("alt");
            var alongside = opts.ContainsKey("alongside");

            if (!string.IsNullOrEmpty(path))
            {
                WriteLine(string.Format(Text.Cli("MsgLaunching"), path) + (admin ? Text.Cli("FmtAdminSuffix") : ""), ConsoleColor.Green);
                ProcessRunner.Run(path, admin);
                return 0;
            }

            var item = ItemService.FindItem(id, title);
            if (item == null)
            {
                WriteLine(Text.Cli("ErrItemNotFound"), ConsoleColor.Red);
                return 1;
            }

            if (string.IsNullOrEmpty(item.ExePath) && !(alt && !string.IsNullOrEmpty(item.AlternativeLaunchCommand)))
            {
                WriteLine(Text.Cli("ErrNoExePath"), ConsoleColor.Red);
                return 1;
            }

            WriteLine(string.Format(Text.Cli("MsgLaunching"), item.Title), ConsoleColor.Green);
            LaunchService.Launch(item, alt);
            if (alongside && !string.IsNullOrEmpty(item.AlongsideCommand))
                WriteLine(string.Format(Text.Cli("MsgAlongside"), item.AlongsideCommand), ConsoleColor.Green);

            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrLaunchingFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdLaunch failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdAdd(Dictionary<string, string> opts)
    {
        try
        {
            var title = opts.GetValueOrDefault("title");
            var path = opts.GetValueOrDefault("path");
            var admin = opts.ContainsKey("admin");
            var icon = opts.GetValueOrDefault("icon");
            var platform = opts.GetValueOrDefault("platform");
            var mgr = opts.GetValueOrDefault("mgr");
            var alt = opts.GetValueOrDefault("alt");
            var alongside = opts.GetValueOrDefault("alongside");

            if (string.IsNullOrEmpty(title)) { WriteLine(Text.Cli("ErrTitleRequired"), ConsoleColor.Red); return 1; }
            if (string.IsNullOrEmpty(path)) { WriteLine(Text.Cli("ErrPathRequired"), ConsoleColor.Red); return 1; }

            if (ItemService.CheckDuplicate(path))
            {
                WriteLine(Text.Cli("ErrDuplicatePath"), ConsoleColor.Yellow);
                return 1;
            }

            var item = ItemService.CreateItem(title, path, admin, icon, platform, mgr, alt, alongside);
            ItemService.AddItem(item);

            WriteLine(string.Format(Text.Cli("MsgAdded"), item.Id, title), ConsoleColor.Green);
            WriteLine(string.Format(Text.Cli("FmtItemPath"), path));
            if (admin) WriteLine(Text.Cli("LblAdminIndent"));
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrAddItemFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdAdd failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdRemove(Dictionary<string, string> opts)
    {
        try
        {
            var id = opts.GetValueOrDefault("id");
            var title = opts.GetValueOrDefault("title");
            var permanent = opts.ContainsKey("permanent");

            ItemService.RemoveItem(id, title, permanent);
            WriteLine(string.Format(Text.Cli("MsgRemoved"), (permanent ? Text.Cli("MsgPermanentlyDeleted") : Text.Cli("MsgMovedToRecycle")), (title ?? id)), ConsoleColor.Green);
            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteLine(Text.Cli("ErrNotFound"), ConsoleColor.Red);
            return 1;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrRemoveItemFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdRemove failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdRestore(Dictionary<string, string> opts)
    {
        try
        {
            var id = opts.GetValueOrDefault("id");
            var title = opts.GetValueOrDefault("title");
            var all = opts.ContainsKey("all");

            if (all)
            {
                var recycleItems = ConfigService.LoadRecycleBinItems();
                var count = recycleItems.Count;
                ItemService.RestoreAll();
                WriteLine(string.Format(Text.Cli("MsgRestoredCount"), count), ConsoleColor.Green);
                return 0;
            }

            ItemService.RestoreItem(id, title);
            WriteLine(string.Format(Text.Cli("MsgRestored"), title ?? id), ConsoleColor.Green);
            return 0;
        }
        catch (InvalidOperationException)
        {
            WriteLine(Text.Cli("ErrNotInRecycle"), ConsoleColor.Red);
            return 1;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrRestoreItemFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdRestore failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdRecycle(Dictionary<string, string> opts)
    {
        try
        {
            if (opts.ContainsKey("purge"))
            {
                var recycleItems = ConfigService.LoadRecycleBinItems();
                var count = recycleItems.Count;
                ItemService.EmptyRecycle();
                WriteLine(string.Format(Text.Cli("MsgRecycleEmptied"), count), ConsoleColor.Green);
                return 0;
            }

            if (opts.ContainsKey("mark"))
            {
                var markId = opts.GetValueOrDefault("mark");
                if (string.IsNullOrEmpty(markId) || markId == "true")
                {
                    WriteLine(Text.Cli("ErrMarkIdRequired"), ConsoleColor.Red);
                    return 1;
                }
                if (!ItemService.MarkPendingDeletion(markId))
                {
                    WriteLine(Text.Cli("ErrNotInRecycle"), ConsoleColor.Red);
                    return 1;
                }
                WriteLine(string.Format(Text.Cli("MsgMarkedPending"), markId), ConsoleColor.Green);
                return 0;
            }

            if (opts.ContainsKey("empty"))
            {
                var count = ItemService.MarkAllPendingDeletion();
                WriteLine(string.Format(Text.Cli("MsgMarkedAllPending"), count), ConsoleColor.Green);
                return 0;
            }

            if (opts.ContainsKey("clean"))
            {
                ItemService.AutoCleanExpired();
                WriteLine(Text.Cli("MsgCleanExpired"), ConsoleColor.Green);
                return 0;
            }

            return await CmdList(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["recycle"] = "true" });
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdRecycle failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdScan(Dictionary<string, string> opts)
    {
        try
        {
            bool steam = opts.ContainsKey("steam");
            bool epic = opts.ContainsKey("epic");
            bool xbox = opts.ContainsKey("xbox");
            bool all = opts.ContainsKey("all") || (!steam && !epic && !xbox);
            bool import = opts.ContainsKey("import");
            bool classify = opts.ContainsKey("classify");
            bool invalid = opts.ContainsKey("invalid");
            bool deleteInvalid = opts.ContainsKey("delete-invalid");

            var scannedGames = new List<ScannedGame>();

            if (all || steam)
            {
                WriteLine(Text.Cli("ScanSteam"), ConsoleColor.Cyan);
                try
                {
                    var steamGames = ScanService.ScanSteam();
                    scannedGames.AddRange(steamGames);
                    WriteLine(string.Format(Text.Cli("MsgFoundCount"), steamGames.Count, "Steam"));
                }
                catch (Exception ex)
                {
                    WriteLine(string.Format(Text.Cli("MsgScanFailed"), "Steam", ex.Message), ConsoleColor.DarkYellow);
                    try { LogService.Write("CLI", "Steam scan failed", ex); } catch { }
                }
            }

            if (all || epic)
            {
                WriteLine(Text.Cli("ScanEpic"), ConsoleColor.Cyan);
                try
                {
                    var epicGames = ScanService.ScanEpic();
                    scannedGames.AddRange(epicGames);
                    WriteLine(string.Format(Text.Cli("MsgFoundCount"), epicGames.Count, "Epic"));
                }
                catch (Exception ex)
                {
                    WriteLine(string.Format(Text.Cli("MsgScanFailed"), "Epic", ex.Message), ConsoleColor.DarkYellow);
                    try { LogService.Write("CLI", "Epic scan failed", ex); } catch { }
                }
            }

            if (all || xbox)
            {
                WriteLine(Text.Cli("ScanXbox"), ConsoleColor.Cyan);
                try
                {
                    var uwpGames = await ScanService.ScanXboxAsync();
                    scannedGames.AddRange(uwpGames);
                    WriteLine(string.Format(Text.Cli("MsgFoundCount"), uwpGames.Count, "Xbox/UWP"));
                }
                catch (Exception ex)
                {
                    WriteLine(string.Format(Text.Cli("MsgScanFailed"), "Xbox", ex.Message), ConsoleColor.DarkYellow);
                    try { LogService.Write("CLI", "Xbox scan failed", ex); } catch { }
                }
            }

            if (classify || invalid || deleteInvalid)
            {
                var allItems = ConfigService.LoadItems();
                var (newGames, existingGames) = ScanService.Classify(scannedGames, allItems);
                var invalidGames = ScanService.FindInvalidGames(
                    allItems, scannedGames,
                    ScanService.CanValidateSteam, ScanService.CanValidateEpic);

                if (deleteInvalid)
                {
                    var count = ScanService.DeleteInvalidGames(invalidGames);
                    WriteLine(string.Format(Text.Cli("MsgRemovedInvalid"), count), ConsoleColor.Green);
                    return 0;
                }

                if (_jsonMode)
                {
                    WriteJson(new
                    {
                        newGames = newGames.Select(g => new { g.Title, g.ExePath, g.PlatformBadge }),
                        existingGames = existingGames.Select(g => new { g.Title, g.ExePath, g.PlatformBadge }),
                        invalidGames = invalid || classify
                            ? invalidGames.Select(g => new { g.Title, g.ExePath, g.PlatformBadge, g.ItemId })
                            : null
                    });
                    return 0;
                }

                if (invalid)
                {
                    if (invalidGames.Count == 0)
                    {
                        WriteLine(Text.Cli("MsgNoInvalid"), ConsoleColor.DarkGray);
                        return 0;
                    }
                    WriteLine(string.Format(Text.Cli("MsgInvalidGamesHeader"), invalidGames.Count), ConsoleColor.Red);
                    WriteLine(new string('-', 60));
                    foreach (var game in invalidGames)
                    {
                        WriteLine(string.Format(Text.Cli("FmtScanGameEntry"), game.PlatformBadge, game.Title));
                        WriteLine(string.Format(Text.Cli("FmtScanGamePath"), game.ExePath));
                        WriteLine(string.Format(Text.Cli("FmtScanGameItemId"), game.ItemId));
                    }
                    return 0;
                }

                WriteLine();
                WriteLine(Text.Cli("MsgScanResults"), ConsoleColor.Cyan);
                WriteLine(new string('-', 60));
                WriteLine(string.Format(Text.Cli("MsgNewGames"), newGames.Count), ConsoleColor.Green);
                WriteLine(string.Format(Text.Cli("MsgExistingGames"), existingGames.Count), ConsoleColor.DarkGray);
                WriteLine(string.Format(Text.Cli("MsgInvalidGames"), invalidGames.Count), ConsoleColor.Red);

                if (newGames.Count > 0)
                {
                    WriteLine();
                    WriteLine(Text.Cli("MsgNewGamesHeader"), ConsoleColor.Green);
                    foreach (var game in newGames)
                        WriteLine(string.Format(Text.Cli("FmtScanGameEntry"), game.PlatformBadge, game.Title));
                }

                if (invalidGames.Count > 0)
                {
                    WriteLine();
                    WriteLine(Text.Cli("MsgInvalidGamesHeader"), ConsoleColor.Red);
                    foreach (var game in invalidGames)
                        WriteLine(string.Format(Text.Cli("FmtScanInvalidGameEntry"), game.PlatformBadge, game.Title, game.ItemId));
                }

                WriteLine();
                WriteLine(Text.Cli("MsgScanHint"));

                if (import && newGames.Count > 0)
                {
                    var addedCount = ItemService.ImportGames(newGames);
                    WriteLine(string.Format(Text.Cli("MsgImported"), addedCount), ConsoleColor.Green);
                }

                return 0;
            }

            if (_jsonMode)
            {
                WriteJson(new { scannedGames = scannedGames.Select(g => new { g.Title, g.ExePath, g.PlatformBadge }) });
                return 0;
            }

            if (scannedGames.Count == 0)
            {
                WriteLine(Text.Cli("MsgNoGames"), ConsoleColor.DarkGray);
                return 0;
            }

            WriteLine();
            WriteLine(string.Format(Text.Cli("MsgFoundGames"), scannedGames.Count), ConsoleColor.Green);
            WriteLine(new string('-', 60));
            foreach (var game in scannedGames)
            {
                WriteLine(string.Format(Text.Cli("FmtScanGameEntry"), game.PlatformBadge, game.Title));
                WriteLine(string.Format(Text.Cli("FmtScanGamePath"), game.ExePath));
            }

            if (import)
            {
                WriteLine();
                WriteLine(Text.Cli("MsgImporting"), ConsoleColor.Cyan);
                var addedCount = ItemService.ImportGames(scannedGames);
                if (addedCount > 0)
                    WriteLine(string.Format(Text.Cli("MsgImported"), addedCount), ConsoleColor.Green);
                else
                    WriteLine(Text.Cli("MsgNoNewGames"), ConsoleColor.DarkGray);
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrScanningFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdScan failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdSearch(Dictionary<string, string> opts)
    {
        try
        {
            var query = opts.GetValueOrDefault("_positional") ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                WriteLine(Text.Cli("MsgSearchUsage"), ConsoleColor.Yellow);
                return 1;
            }

            var items = ConfigService.LoadItems();
            var results = ItemService.Search(items, query);

            if (_jsonMode)
            {
                WriteJson(new { query, results = results.Select(ItemToJson) });
                return 0;
            }

            if (results.Count == 0)
            {
                WriteLine(string.Format(Text.Cli("MsgNoSearchResults"), query), ConsoleColor.DarkGray);
                return 0;
            }

            WriteLine(string.Format(Text.Cli("MsgSearchResults"), query, results.Count), ConsoleColor.Cyan);
            WriteLine(new string('-', 60));
            foreach (var item in results)
            {
                WriteLine(string.Format(Text.Cli("FmtItemEntry"), item.Id, item.Title ?? Text.Cli("LblUntitled")));
                WriteLine(string.Format(Text.Cli("FmtItemPath"), item.ExePath ?? Text.Cli("LblNA")));
                if (!string.IsNullOrEmpty(item.PlatformName))
                    WriteLine(string.Format(Text.Cli("FmtItemPlatform"), item.PlatformName));
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrSearchingFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdSearch failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdSettings(Dictionary<string, string> opts)
    {
        try
        {
            bool list = opts.ContainsKey("list");
            var get = opts.GetValueOrDefault("get");
            var set = opts.GetValueOrDefault("set");

            if (list || (!list && string.IsNullOrEmpty(get) && string.IsNullOrEmpty(set)))
            {
                var settingsObj = new
                {
                    launchMode = ConfigService.LaunchMode,
                    closeAfterLaunch = ConfigService.CloseAfterLaunch,
                    iconSize = ConfigService.IconSize,
                    updateChannel = ConfigService.UpdateChannel,
                    githubToken = string.IsNullOrEmpty(ConfigService.GitHubToken) ? Text.Cli("LblNotSet") : Text.Cli("LblConfigured"),
                    appIconPath = ConfigService.AppIconPath,
                    appTitle = ConfigService.AppTitle,
                    storageMode = ConfigService.IsSystemMode ? "system" : "portable",
                    dataPath = ConfigService.CurrentDataPath,
                    cachePath = ConfigService.SystemCachePath,
                    window = new
                    {
                        x = ConfigService.GetWindowBounds().X,
                        y = ConfigService.GetWindowBounds().Y,
                        width = ConfigService.GetWindowBounds().Width,
                        height = ConfigService.GetWindowBounds().Height
                    }
                };

                if (_jsonMode)
                {
                    WriteJson(settingsObj);
                }
                else
                {
                    WriteLine(Text.Cli("LblSettings"), ConsoleColor.Cyan);
                    WriteLine(new string('-', 40));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "launchMode", ConfigService.LaunchMode));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "closeAfterLaunch", ConfigService.CloseAfterLaunch));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "iconSize", ConfigService.IconSize));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "updateChannel", ConfigService.UpdateChannel));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "githubToken", (string.IsNullOrEmpty(ConfigService.GitHubToken) ? Text.Cli("LblNotSet") : Text.Cli("LblConfigured"))));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "appIconPath", ConfigService.AppIconPath));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "appTitle", ConfigService.AppTitle));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "storageMode", (ConfigService.IsSystemMode ? "system" : "portable")));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "dataPath", ConfigService.CurrentDataPath));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "cachePath", ConfigService.SystemCachePath));
                    var wb = ConfigService.GetWindowBounds();
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "windowX", wb.X));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "windowY", wb.Y));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "windowWidth", wb.Width));
                    WriteLine(string.Format(Text.Cli("FmtSettingsLine"), "windowHeight", wb.Height));
                }
                return 0;
            }

            if (!string.IsNullOrEmpty(set))
            {
                var parts = set.Split('=', 2);
                if (parts.Length != 2)
                {
                    WriteLine(Text.Cli("ErrInvalidFormat"), ConsoleColor.Red);
                    return 1;
                }
                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("lang", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != "Zh-CN" && value != "EN")
                    {
                        WriteLine(Text.Cli("ErrLang"), ConsoleColor.Red);
                        return 1;
                    }
                    _lang = value;
                    Text.Load(_lang);
                }
                else if (key.Equals("storagemode", StringComparison.OrdinalIgnoreCase))
                {
                    if (value != "system" && value != "portable")
                    {
                        WriteLine(Text.Cli("ErrStorageSwitch"), ConsoleColor.Red);
                        return 1;
                    }
                    await ConfigService.SwitchStorageModeAsync(value == "system");
                }
                else
                {
                    var errKey = ConfigService.SetSetting(key, value);
                    if (errKey != null)
                    {
                        if (errKey == "ErrUnknownSetting")
                            WriteLine(string.Format(Text.Cli(errKey), key), ConsoleColor.Red);
                        else
                            WriteLine(Text.Cli(errKey), ConsoleColor.Red);
                        return 1;
                    }
                }

                WriteLine(string.Format(Text.Cli("MsgSetSetting"), key, value), ConsoleColor.Green);
                try { LogService.Write("CLI", $"Set setting {key}={value}"); } catch { }
                return 0;
            }

            if (!string.IsNullOrEmpty(get))
            {
                var key = get.ToLowerInvariant();
                string? result = key switch
                {
                    "launchmode" => ConfigService.LaunchMode,
                    "closeafterlaunch" => ConfigService.CloseAfterLaunch.ToString(),
                    "iconsize" => ConfigService.IconSize.ToString(),
                    "updatechannel" => ConfigService.UpdateChannel,
                    "githubtoken" => string.IsNullOrEmpty(ConfigService.GitHubToken) ? Text.Cli("LblNotSet") : Text.Cli("LblConfigured"),
                    "appiconpath" => ConfigService.AppIconPath,
                    "apptitle" => ConfigService.AppTitle,
                    "storagemode" => ConfigService.IsSystemMode ? "system" : "portable",
                    "datapath" => ConfigService.CurrentDataPath,
                    "cachepath" => ConfigService.SystemCachePath,
                    "windowx" => ConfigService.GetWindowBounds().X.ToString(),
                    "windowy" => ConfigService.GetWindowBounds().Y.ToString(),
                    "windowwidth" => ConfigService.GetWindowBounds().Width.ToString(),
                    "windowheight" => ConfigService.GetWindowBounds().Height.ToString(),
                    _ => null
                };

                if (result == null)
                {
                    WriteLine(string.Format(Text.Cli("ErrUnknownSetting"), key), ConsoleColor.Red);
                    return 1;
                }

                WriteLine(result);
                return 0;
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdSettings failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdUpdate(Dictionary<string, string> opts)
    {
        try
        {
            var channel = opts.GetValueOrDefault("channel") ?? "stable";
            if (channel != "stable" && channel != "latest")
            {
                WriteLine(Text.Cli("ErrChannel"), ConsoleColor.Red);
                return 1;
            }

            if (opts.ContainsKey("install"))
                return await CmdUpdateInstall(channel);

            if (opts.ContainsKey("repair"))
                return await CmdUpdateRepair(channel);

            WriteLine(string.Format(Text.Cli("MsgCheckingUpdate"), channel), ConsoleColor.Cyan);

            var release = await UpdateService.CheckForUpdateAsync(channel);

            if (_jsonMode)
            {
                WriteJson(new
                {
                    currentVersion = AppVersion.Version,
                    channel,
                    hasUpdate = release != null,
                    release = release == null ? null : new
                    {
                        release.tag_name,
                        release.name,
                        release.prerelease,
                        release.html_url,
                        release.body
                    }
                });
                return 0;
            }

            if (release == null)
            {
                WriteLine(string.Format(Text.Cli("MsgNoUpdates"), AppVersion.Version), ConsoleColor.Green);
            }
            else
            {
                WriteLine(Text.Cli("MsgUpdateAvailable"), ConsoleColor.Green);
                WriteLine(string.Format(Text.Cli("LblUpdateCurrent"), AppVersion.Version));
                WriteLine(string.Format(Text.Cli("LblUpdateLatest"), release.tag_name, release.name));
                WriteLine(string.Format(Text.Cli("LblUpdateUrl"), release.html_url));
                if (release.prerelease)
                    WriteLine(Text.Cli("LblUpdatePrerelease"), ConsoleColor.DarkYellow);
                if (!string.IsNullOrEmpty(release.body))
                {
                    WriteLine();
                    WriteLine(Text.Cli("LblReleaseNotes"));
                    WriteLine(new string('-', 40));
                    WriteLine(release.body);
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrUpdateFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdUpdate failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdUpdateInstall(string channel)
    {
        WriteLine(string.Format(Text.Cli("MsgCheckingUpdate"), channel), ConsoleColor.Cyan);
        var release = await UpdateService.CheckForUpdateAsync(channel);
        if (release == null)
        {
            WriteLine(string.Format(Text.Cli("MsgNoUpdates"), AppVersion.Version), ConsoleColor.Green);
            return 0;
        }

        string downloadUrl = release.assets?.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.browser_download_url ?? "";
        if (string.IsNullOrEmpty(downloadUrl))
        {
            WriteLine(Text.Cli("ErrNoDownloadUrl"), ConsoleColor.Red);
            return 1;
        }

        WriteLine(string.Format(Text.Cli("MsgUpgradeStart"), release.tag_name), ConsoleColor.Green);
        await StartUpdaterWithProgressAsync(downloadUrl);
        WriteLine(Text.Cli("MsgUpgradeReady"), ConsoleColor.Green);
        return 0;
    }

    private static async Task<int> CmdUpdateRepair(string channel)
    {
        WriteLine(string.Format(Text.Cli("MsgCheckingUpdate"), channel), ConsoleColor.Cyan);
        var release = await UpdateService.GetReleaseAsync(channel);
        if (release == null)
        {
            WriteLine(Text.Cli("ErrNoRelease"), ConsoleColor.Red);
            return 1;
        }

        string downloadUrl = release.assets?.FirstOrDefault(a => a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.browser_download_url ?? "";
        if (string.IsNullOrEmpty(downloadUrl))
        {
            WriteLine(Text.Cli("ErrNoDownloadUrl"), ConsoleColor.Red);
            return 1;
        }

        WriteLine(string.Format(Text.Cli("MsgRepairStart"), release.tag_name), ConsoleColor.Green);
        await StartUpdaterWithProgressAsync(downloadUrl);
        WriteLine(Text.Cli("MsgUpgradeReady"), ConsoleColor.Green);
        return 0;
    }

    private static async Task StartUpdaterWithProgressAsync(string downloadUrl)
    {
        await UpdateService.StartUpdaterAndWaitAsync(downloadUrl, msg =>
        {
            if (msg.StartsWith("DOWNLOAD "))
            {
                var pct = msg.Split(' ')[1];
                WriteLine(string.Format(Text.Cli("MsgUpgradeProgress"), pct));
            }
        });
    }

    private static int CmdVersion()
    {
        WriteLine(string.Format(Text.Cli("MsgVersion"), AppVersion.Version), ConsoleColor.Cyan);
        WriteLine(Text.Cli("MsgCliMode"));
        try { LogService.Write("CLI", $"Version displayed: {AppVersion.Version}"); } catch { }
        return 0;
    }

    private static int CmdSkill()
    {
        try
        {
            using var stream = typeof(CliService).Assembly.GetManifestResourceStream("EricGameLauncher.CliSkill.md");
            if (stream == null)
            {
                WriteLine(Text.Cli("ErrSkillMissing"), ConsoleColor.Red);
                return 1;
            }
            using var reader = new StreamReader(stream);
            WriteLine(reader.ReadToEnd());
            try { LogService.Write("CLI", "Skill guide printed"); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdSkill failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdEdit(Dictionary<string, string> opts)
    {
        try
        {
            var id = opts.GetValueOrDefault("id");
            if (string.IsNullOrEmpty(id)) { WriteLine(Text.Cli("ErrIdRequired"), ConsoleColor.Red); return 1; }

            var items = ConfigService.LoadItems();
            var item = ItemService.FindItem(id, null, items);
            if (item == null) { WriteLine(Text.Cli("ErrNotFound"), ConsoleColor.Red); return 1; }

            bool modified = false;
            ItemService.EditItem(item, i =>
            {
                if (opts.TryGetValue("title", out var v)) { i.Title = v; modified = true; }
                if (opts.TryGetValue("path", out v)) { i.ExePath = v; modified = true; }
                if (opts.TryGetValue("admin", out v)) { i.IsAdmin = ParseBool(v); modified = true; }
                if (opts.TryGetValue("icon", out v)) { i.IconPath = v; modified = true; }
                if (opts.TryGetValue("platform", out v)) { i.Platform = v; modified = true; }
                if (opts.TryGetValue("mgr", out v)) { i.MgrPath = v; modified = true; }
                if (opts.TryGetValue("mgr-admin", out v)) { i.IsMgrAdmin = ParseBool(v); modified = true; }
                if (opts.TryGetValue("alt", out v)) { i.AlternativeLaunchCommand = v; modified = true; }
                if (opts.TryGetValue("alt-admin", out v)) { i.IsAltAdmin = ParseBool(v); modified = true; }
                if (opts.TryGetValue("alt-enable", out v)) { i.UseAlternativeLaunch = ParseBool(v); modified = true; }
                if (opts.TryGetValue("alongside", out v)) { i.AlongsideCommand = v; modified = true; }
                if (opts.TryGetValue("alongside-admin", out v)) { i.IsAlongsideAdmin = ParseBool(v); modified = true; }
                if (opts.TryGetValue("alongside-enable", out v)) { i.RunAlongside = ParseBool(v); modified = true; }
                if (opts.TryGetValue("custom-add", out v))
                {
                    var parts = v.Split('|');
                    if (parts.Length >= 2)
                    {
                        i.CustomMenuItems ??= new List<CustomMenuItem>();
                        var cm = new CustomMenuItem { Title = parts[0], Command = parts[1] };
                        if (parts.Length >= 3) cm.IsAdmin = ParseBool(parts[2]);
                        i.CustomMenuItems.Add(cm);
                        modified = true;
                    }
                }
                if (opts.ContainsKey("custom-clear")) { i.CustomMenuItems?.Clear(); modified = true; }
                if (opts.TryGetValue("custom-remove", out v) && int.TryParse(v, out var idx))
                {
                    if (i.CustomMenuItems != null && idx >= 0 && idx < i.CustomMenuItems.Count)
                    { i.CustomMenuItems.RemoveAt(idx); modified = true; }
                }
            });

            if (!modified) { WriteLine(Text.Cli("ErrNoChanges"), ConsoleColor.Yellow); return 1; }

            WriteLine(string.Format(Text.Cli("MsgUpdated"), item.Id, item.Title), ConsoleColor.Green);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrEditItemFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdEdit failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdSort(Dictionary<string, string> opts)
    {
        try
        {
            var items = ConfigService.LoadItems();

            if (opts.ContainsKey("list"))
            {
                if (_jsonMode)
                {
                    WriteJson(new { items = items.Select((item, idx) => new { index = idx, item.Id, item.Title, item.SortOrder }) });
                }
                else
                {
                    WriteLine(string.Format(Text.Cli("MsgSortList"), items.Count), ConsoleColor.Cyan);
                    WriteLine(new string('-', 60));
                    for (int idx = 0; idx < items.Count; idx++)
                        WriteLine(string.Format(Text.Cli("FmtSortListItem"), idx, items[idx].Title ?? Text.Cli("LblUntitled"), items[idx].Id, items[idx].SortOrder));
                }
                return 0;
            }

            var id = opts.GetValueOrDefault("id");
            if (string.IsNullOrEmpty(id)) { WriteLine(Text.Cli("ErrIdRequiredSort"), ConsoleColor.Red); return 1; }

            var item = items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null) { WriteLine(Text.Cli("ErrNotFound"), ConsoleColor.Red); return 1; }

            if (opts.ContainsKey("move-up"))
            {
                var newIdx = ItemService.MoveUp(items, id);
                if (newIdx < 0) { WriteLine(Text.Cli("ErrAtTop"), ConsoleColor.Yellow); return 0; }
                WriteLine(string.Format(Text.Cli("MsgMovedUp"), item.Title, newIdx), ConsoleColor.Green);
            }
            else if (opts.ContainsKey("move-down"))
            {
                var newIdx = ItemService.MoveDown(items, id);
                if (newIdx < 0 || newIdx >= items.Count - 1) { WriteLine(Text.Cli("ErrAtBottom"), ConsoleColor.Yellow); return 0; }
                WriteLine(string.Format(Text.Cli("MsgMovedDown"), item.Title, newIdx), ConsoleColor.Green);
            }
            else if (opts.TryGetValue("swap-with", out var swapId))
            {
                if (!ItemService.Swap(items, id, swapId)) { WriteLine(Text.Cli("ErrSwapNotFound"), ConsoleColor.Red); return 1; }
                var other = items.First(i => string.Equals(i.Id, swapId, StringComparison.OrdinalIgnoreCase));
                WriteLine(string.Format(Text.Cli("MsgSwapped"), item.Title, other.Title), ConsoleColor.Green);
            }
            else
            {
                WriteLine(Text.Cli("ErrSpecifyMove"), ConsoleColor.Yellow);
                return 1;
            }

            ItemService.SaveOrder(items);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrSortingFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdSort failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdAnnouncements(Dictionary<string, string> opts)
    {
        try
        {
            await ServerConfigManager.FetchConfigAsync();
            var readId = opts.GetValueOrDefault("read");

            if (!string.IsNullOrEmpty(readId))
            {
                ServerConfigManager.MarkAsRead(readId);
                WriteLine(string.Format(Text.Cli("ErrMarkedReadFmt"), readId), ConsoleColor.Green);
                try { LogService.Write("CLI", $"Marked announcement read id={readId}"); } catch { }
                return 0;
            }

            var announcements = ServerConfigManager.GetActiveAnnouncements();

            if (_jsonMode)
            {
                WriteJson(new
                {
                    announcements = announcements.Select(a => new
                    {
                        a.Id,
                        title = a.GetDisplayTitle(),
                        body = a.GetDisplayBody(),
                        a.Position,
                        a.Time,
                        isRead = ServerConfigManager.IsRead(a.Id)
                    })
                });
                return 0;
            }

            if (announcements.Count == 0)
            {
                WriteLine(Text.Cli("MsgNoAnnouncements"), ConsoleColor.DarkGray);
                return 0;
            }

            WriteLine(string.Format(Text.Cli("MsgAnnouncementsHeader"), announcements.Count), ConsoleColor.Cyan);
            WriteLine(new string('-', 60));
            foreach (var a in announcements)
            {
                var readMark = ServerConfigManager.IsRead(a.Id) ? Text.Cli("LblReadMark") : Text.Cli("LblNewMark");
                var color = ServerConfigManager.IsRead(a.Id) ? (ConsoleColor?)null : ConsoleColor.Green;
                WriteLine(string.Format(Text.Cli("FmtAnnouncementEntry"), readMark, a.Id, a.GetDisplayTitle()), color);
                WriteLine(string.Format(Text.Cli("FmtAnnouncementBody"), a.GetDisplayBody()));
                WriteLine();
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdAnnouncements failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdInstall(Dictionary<string, string> opts)
    {
        try
        {
            AppInstallService.Install();
            WriteLine(Text.Cli("MsgShortcutsCreated"), ConsoleColor.Green);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrInstallFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdInstall failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdUninstall(Dictionary<string, string> opts)
    {
        try
        {
            AppInstallService.Uninstall();
            WriteLine(Text.Cli("MsgShortcutsRemoved"), ConsoleColor.Green);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrUninstallFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdUninstall failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdLaunchPlatform(Dictionary<string, string> opts)
    {
        try
        {
            var id = opts.GetValueOrDefault("id");
            var title = opts.GetValueOrDefault("title");

            var item = ItemService.FindItem(id, title);
            if (item == null) { WriteLine(Text.Cli("ErrNotFound"), ConsoleColor.Red); return 1; }

            var mgr = LaunchService.GetManagerTarget(item);
            if (!mgr.HasValue) { WriteLine(Text.Cli("ErrNoManager"), ConsoleColor.Yellow); return 1; }

            WriteLine(string.Format(Text.Cli("MsgLaunchingMgr"), mgr.Value.path), ConsoleColor.Green);
            LaunchService.LaunchManager(item);
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdLaunchPlatform failed", ex); } catch { }
            return 1;
        }
    }

    private static async Task<int> CmdStorage(Dictionary<string, string> opts)
    {
        try
        {
            if (opts.TryGetValue("switch", out var mode))
            {
                if (mode != "system" && mode != "portable")
                {
                    WriteLine(Text.Cli("ErrStorageMode"), ConsoleColor.Red);
                    return 1;
                }
                bool useSystem = mode == "system";
                if (useSystem == ConfigService.IsSystemMode)
                {
                    WriteLine(string.Format(Text.Cli("MsgAlreadyMode"), mode), ConsoleColor.Yellow);
                    return 0;
                }
                await ConfigService.SwitchStorageModeAsync(useSystem);
                WriteLine(string.Format(Text.Cli("MsgSwitchedTo"), mode), ConsoleColor.Green);
                WriteLine(string.Format(Text.Cli("MsgStorageDataPath"), ConfigService.CurrentDataPath));
                try { LogService.Write("CLI", $"Storage switched to {mode}"); } catch { }
            }
            else
            {
                var currentMode = ConfigService.IsSystemMode ? "system" : "portable";
                if (_jsonMode)
                {
                    WriteJson(new { storageMode = currentMode, dataPath = ConfigService.CurrentDataPath, cachePath = ConfigService.SystemCachePath });
                }
                else
                {
                    WriteLine(string.Format(Text.Cli("MsgStorageStatus"), currentMode), ConsoleColor.Cyan);
                    WriteLine(string.Format(Text.Cli("MsgStorageDataPath"), ConfigService.CurrentDataPath));
                    WriteLine(string.Format(Text.Cli("MsgStorageCachePath"), ConfigService.SystemCachePath));
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteLine(string.Format(Text.Cli("ErrFmt"), ex.Message), ConsoleColor.Red);
            try { LogService.Write("CLI", "CmdStorage failed", ex); } catch { }
            return 1;
        }
    }

    private static bool ParseBool(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value == "1") return true;
        if (bool.TryParse(value, out var result)) return result;
        return false;
    }

    private static object ItemToJson(AppItem item)
    {
        return new
        {
            item.Id,
            item.Title,
            item.ExePath,
            item.IsAdmin,
            item.Platform,
            item.PlatformName,
            item.IconPath,
            item.MgrPath,
            item.AlternativeLaunchCommand,
            item.UseAlternativeLaunch,
            item.AlongsideCommand,
            item.RunAlongside,
            status = item.Status switch
            {
                0 => "normal",
                1 => "recycled",
                2 => "pending_deletion",
                _ => "unknown"
            },
            customMenuItems = item.CustomMenuItems?.Select(m => new { m.Title, m.Command, m.IsAdmin })
        };
    }

    private sealed record CommandDef(string Name, string[] Options, string DescKey, string HelpKey);

    private static readonly CommandDef[] Commands =
    [
        new("list",           ["--recycle", "--json"],                 "Help_CmdListDesc",           "Help_List_Text"),
        new("launch",         ["--id", "--title", "--path", "--admin", "--alt", "--alongside"],
                                                                       "Help_CmdLaunchDesc",         "Help_Launch_Text"),
        new("platform",       ["--id", "--title"],                     "Help_CmdPlatformDesc",       "Help_Platform_Text"),
        new("add",            ["--title", "--path", "--admin", "--icon", "--platform", "--mgr", "--alt", "--alongside"],
                                                                       "Help_CmdAddDesc",            "Help_Add_Text"),
        new("edit",           ["--id", "--title", "--path", "--admin", "--icon", "--platform", "--mgr", "--mgr-admin", "--alt", "--alt-admin", "--alt-enable", "--alongside", "--alongside-admin", "--alongside-enable", "--custom-add", "--custom-remove", "--custom-clear"],
                                                                       "Help_CmdEditDesc",           "Help_Edit_Text"),
        new("remove",         ["--id", "--title", "--permanent"],      "Help_CmdRemoveDesc",         "Help_Remove_Text"),
        new("restore",        ["--id", "--title", "--all"],            "Help_CmdRestoreDesc",        "Help_Restore_Text"),
        new("recycle",        ["--list", "--mark", "--empty", "--purge", "--clean", "--json"],
                                                                       "Help_CmdRecycleDesc",        "Help_Recycle_Text"),
        new("scan",           ["--steam", "--epic", "--xbox", "--all", "--classify", "--invalid", "--delete-invalid", "--import", "--json"],
                                                                       "Help_CmdScanDesc",           "Help_Scan_Text"),
        new("search",         ["--json"],                              "Help_CmdSearchDesc",         "Help_Search_Text"),
        new("sort",           ["--list", "--id", "--move-up", "--move-down", "--swap-with", "--json"],
                                                                       "Help_CmdSortDesc",           "Help_Sort_Text"),
        new("settings",       ["--list", "--get", "--set", "--json"],  "Help_CmdSettingsDesc",       "Help_Settings_Text"),
        new("update",         ["--check", "--install", "--repair", "--channel", "--json"],      "Help_CmdUpdateDesc",         "Help_Update_Text"),
        new("announcements",  ["--list", "--read", "--json"],          "Help_CmdAnnouncementsDesc",  "Help_Announcements_Text"),
        new("install",        [],                                       "Help_CmdInstallDesc",        "Help_Install_Text"),
        new("uninstall",      [],                                       "Help_CmdUninstallDesc",      "Help_Uninstall_Text"),
        new("storage",        ["--status", "--switch", "--json"],      "Help_CmdStorageDesc",        "Help_Storage_Text"),
        new("skill",          [],                                       "Help_CmdSkillDesc",          "Help_Skill_Text"),
        new("version",        [],                                       "Help_CmdVersionDesc",        "Help_Version_Text"),
        new("help",           [],                                       "Help_CmdHelpDesc",           ""),
    ];

}