namespace EricGameLauncher;

internal static class CliArguments
{
    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "recycle-id", "title", "path", "icon", "platform", "isplatform", "pin", "mgr", "alt",
        "alongside", "mgr-admin", "alt-admin", "alt-enable", "alongside-admin", "alongside-enable",
        "custom-add", "custom-remove", "mark", "swap-with", "get", "set", "channel", "read", "switch"
    };
    private static readonly HashSet<string> Booleans = new(StringComparer.OrdinalIgnoreCase)
        { "admin", "isplatform", "pin", "mgr-admin", "alt-admin", "alt-enable", "alongside-admin", "alongside-enable" };

    internal static Dictionary<string, string> Parse(string command, string[] args, IReadOnlyCollection<string> allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (command != "search") throw Error(arg);
                result["_positional"] = result.TryGetValue("_positional", out string? previous) ? previous + " " + arg : arg;
                continue;
            }
            if (!seen.Add(arg) || (arg != "--json" && !allowed.Contains(arg, StringComparer.OrdinalIgnoreCase))) throw Error(arg);
            string key = arg[2..].ToLowerInvariant();
            if (key == "json") continue;
            bool needsValue = Values.Contains(key) && !((key is "alt" or "alongside") && command == "launch") || key == "admin" && command == "edit";
            string value = "true";
            if (needsValue)
            {
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw Error(arg);
                value = args[index];
                if (Booleans.Contains(key)) ParseBoolean(value);
            }
            result.Add(key, value);
        }
        string[] selectors = ["id", "title", "path", "recycle-id", "all"];
        if (command is "launch" or "platform" or "remove" or "restore")
        {
            var selected = selectors.Where(result.ContainsKey).ToArray();
            if (selected.Length != 1) throw Error(command);
            if (selected[0] != "all" && string.IsNullOrWhiteSpace(result[selected[0]])) throw Error(command);
        }
        string[] actions = command switch
        {
            "recycle" => ["list", "mark", "empty", "purge", "clean"],
            "sort" => ["list", "move-up", "move-down", "swap-with"],
            "settings" => ["list", "get", "set"],
            "update" => ["check", "install", "repair", "history"],
            "storage" => ["status", "switch"],
            "announcements" => ["list", "read"],
            _ => []
        };
        if (actions.Count(result.ContainsKey) > 1) throw Error(command);
        if (command == "uninstall" && result.ContainsKey("keep-data") && result.ContainsKey("remove-data")) throw Error(command);
        if (command == "add" && (!result.ContainsKey("path") || string.IsNullOrWhiteSpace(result["path"]))) throw Error("--path");
        if (command == "sort" && actions.Skip(1).Any(result.ContainsKey) && !result.ContainsKey("id")) throw Error("--id");
        if (command == "settings" && result.TryGetValue("set", out string? setting) && (!setting.Contains('=') || setting.StartsWith('='))) throw Error("--set");
        if (command == "storage" && result.TryGetValue("switch", out string? mode) && mode is not ("portable" or "system")) throw Error("--switch");
        if (command == "launch" && result.ContainsKey("alt") && result.ContainsKey("alongside")) throw Error(command);
        if (command == "launch" && result.ContainsKey("path") && (result.ContainsKey("alt") || result.ContainsKey("alongside"))) throw Error(command);
        if (command == "scan" && result.ContainsKey("import") && (result.ContainsKey("invalid") || result.ContainsKey("delete-invalid"))) throw Error(command);
        if (command == "edit")
        {
            if (!result.TryGetValue("id", out string? id) || string.IsNullOrWhiteSpace(id) || result.Count < 2) throw Error(command);
            if (result.ContainsKey("custom-clear") && (result.ContainsKey("custom-add") || result.ContainsKey("custom-remove"))) throw Error(command);
            if (result.TryGetValue("custom-remove", out string? number) && (!int.TryParse(number, out int parsed) || parsed < 0)) throw Error("--custom-remove");
            if (result.TryGetValue("custom-add", out string? menu))
            {
                var fields = menu.Split('|');
                if (fields.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(fields[1])) throw Error("--custom-add");
                if (fields.Length == 3) ParseBoolean(fields[2]);
            }
        }
        return result;
    }

    internal static bool ParseBoolean(string value) => value.ToLowerInvariant() switch
        { "true" or "1" => true, "false" or "0" => false, _ => throw Error(value) };
    private static ArgumentException Error(string option) => new(Text.Cli("ErrCliArgument") + " " + option);
}
