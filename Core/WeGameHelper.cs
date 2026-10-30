using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace EricGameLauncher;

public static class WeGameHelper
{
    private static readonly uint[] Key = [0x21235321, 0x395f4025, 0x40412560, 0x402b2d24];
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static List<ScannedGame> GetInstalledGames()
    {
        string database = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", "WeGame", "WeGameCfg.info");
        if (!File.Exists(database)) return [];
        try
        {
            var (ids, roots) = ReadManifest(database);
            if (ids.Count == 0) return [];
            var games = ScanLibraries(ids, roots, FindManagerPath());
            LogService.Write("Scan", $"WeGame manifest installedIds={ids.Count} roots={roots.Length} discovered={games.Count}");
            return games;
        }
        catch (Exception ex)
        {
            LogService.Write("Scan", "WeGame library unavailable", ex);
            throw new InvalidDataException(Text.T("ScanWeGameFailed"), ex);
        }
    }

    internal static (HashSet<ulong> ids, string[] roots) ReadManifest(string database)
    {
        var rows = SqliteLibraryReader.ReadBlobs(database,
            "SELECT key, data FROM Setting WHERE key IN ('GameCfgMgr.InstalledGameList', 'CfgMgr.UserSysSetting.InstallPathMap')", 2, 4 * 1024 * 1024);
        if (!rows.TryGetValue("GameCfgMgr.InstalledGameList", out var list)) throw new InvalidDataException();
        var ids = ParseInstalledIds(Utf8.GetString(Decrypt(list)));
        if (ids.Count == 0) return (ids, []);
        if (!rows.TryGetValue("CfgMgr.UserSysSetting.InstallPathMap", out var paths)) throw new InvalidDataException();
        return (ids, ParseLibraryRoots(Utf8.GetString(Decrypt(paths))));
    }

    internal static HashSet<ulong> ParseInstalledIds(string text)
    {
        var ids = new HashSet<ulong>();
        if (string.IsNullOrWhiteSpace(text)) return ids;
        foreach (string value in text.Trim().Split(','))
        {
            if (!ulong.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) || id == 0) throw new InvalidDataException();
            ids.Add(id);
        }
        return ids;
    }

    internal static string[] ParseLibraryRoots(string text)
    {
        var roots = JsonSerializer.Deserialize<string[]>(text) ?? throw new InvalidDataException();
        return roots.Select(root =>
        {
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) throw new InvalidDataException();
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static List<ScannedGame> ScanLibraries(HashSet<ulong> ids, string[] roots, string? manager)
    {
        var result = new List<ScannedGame>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<ulong>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                LogService.Write("Scan", $"WeGame library directory unavailable path={root}");
                continue;
            }
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                string identity = Path.Combine(directory, "rail_files", "rail_game_identify.json");
                if (!File.Exists(identity)) continue;
                try
                {
                    var info = new FileInfo(identity);
                    if (info.Length > 64 * 1024) throw new InvalidDataException();
                    using var json = JsonDocument.Parse(File.ReadAllText(identity));
                    var value = json.RootElement.GetProperty("game_id");
                    ulong id = value.ValueKind == JsonValueKind.String
                        ? ulong.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture)
                        : value.GetUInt64();
                    if (!ids.Contains(id)) continue;
                    string launcher = Path.Combine(directory, "WeGameLauncher", "launcher.exe");
                    if (!File.Exists(launcher) || !seen.Add(PathHashHelper.PathKey(launcher))) continue;
                    string title = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
                    string suffix = "(" + id.ToString(CultureInfo.InvariantCulture) + ")";
                    if (title.EndsWith(suffix, StringComparison.Ordinal)) title = title[..^suffix.Length].Trim();
                    result.Add(new ScannedGame
                    {
                        Title = string.IsNullOrWhiteSpace(title) ? id.ToString(CultureInfo.InvariantCulture) : title,
                        ExePath = launcher,
                        PlatformBadge = "WeGame",
                        ManagerPath = manager
                    });
                    found.Add(id);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
                {
                    LogService.Write("Scan", $"WeGame candidate skipped path={directory}", ex);
                }
            }
        }
        if (found.Count != ids.Count) LogService.Write("Scan", $"WeGame manifest contains unresolved installed IDs count={ids.Count - found.Count}");
        return result;
    }

    internal static bool MatchesLaunchPath(ScannedGame game, string? command)
    {
        if (game.PlatformBadge != "WeGame" || string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            string? launcherDirectory = Path.GetDirectoryName(game.ExePath);
            if (!string.Equals(Path.GetFileName(launcherDirectory), "WeGameLauncher", StringComparison.OrdinalIgnoreCase)) return false;
            string? root = Path.GetDirectoryName(launcherDirectory);
            if (root == null) return false;
            var (target, _) = ProcessRunner.SplitPath(Environment.ExpandEnvironmentVariables(command));
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string? resolved = ShortcutResolver.GetLnkTarget(target);
                if (string.IsNullOrEmpty(resolved)) return false;
                (target, _) = ProcessRunner.SplitPath(resolved);
            }
            if (!Path.IsPathFullyQualified(target)) return false;
            return Path.GetFullPath(target).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { LogService.Write("Scan", "WeGame existing launch path could not be matched", ex); return false; }
    }

    internal static bool IsGameLauncher(string command)
    {
        try
        {
            var (target, _) = ProcessRunner.SplitPath(Environment.ExpandEnvironmentVariables(command));
            if (!Path.IsPathFullyQualified(target) || !string.Equals(Path.GetFileName(target), "launcher.exe", StringComparison.OrdinalIgnoreCase)) return false;
            string? parent = Path.GetDirectoryName(target);
            if (!string.Equals(Path.GetFileName(parent), "WeGameLauncher", StringComparison.OrdinalIgnoreCase)) return false;
            string? root = Path.GetDirectoryName(parent);
            return root != null && File.Exists(Path.Combine(root, "rail_files", "rail_game_identify.json"));
        }
        catch (Exception ex) { LogService.Write("Platform", "WeGame launcher recognition failed", ex); return false; }
    }

    internal static string? FindManagerPath()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                using var entry = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WeGame");
                if (entry?.GetValue("UninstallString") is not string uninstall) continue;
                var (exe, _) = ProcessRunner.SplitPath(uninstall);
                string? directory = Path.GetDirectoryName(exe);
                if (directory == null) continue;
                string manager = Path.Combine(directory, "wegame.exe");
                if (File.Exists(manager)) return manager;
            }
            catch (Exception ex) { LogService.Write("Scan", "WeGame manager registration unavailable", ex); }
        }
        foreach (string root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        foreach (string relative in new[] { "WeGame", @"Tencent\WeGame" })
        {
            string manager = Path.Combine(root, relative, "wegame.exe");
            if (File.Exists(manager)) return manager;
        }
        return null;
    }

    internal static byte[] Decrypt(byte[] cipher)
    {
        if (cipher.Length < 16 || cipher.Length % 8 != 0) throw new InvalidDataException();
        var padded = new byte[cipher.Length];
        Span<byte> previousMix = stackalloc byte[8];
        Span<byte> previousCipher = stackalloc byte[8];
        Span<byte> mix = stackalloc byte[8];
        previousMix.Clear();
        previousCipher.Clear();
        for (int offset = 0; offset < cipher.Length; offset += 8)
        {
            for (int i = 0; i < 8; i++) mix[i] = (byte)(cipher[offset + i] ^ previousMix[i]);
            uint left = BinaryPrimitives.ReadUInt32BigEndian(mix), right = BinaryPrimitives.ReadUInt32BigEndian(mix[4..]);
            unchecked
            {
                uint sum = 0xe3779b90;
                for (int round = 0; round < 16; round++)
                {
                    right -= ((left << 4) + Key[2]) ^ (left + sum) ^ ((left >> 5) + Key[3]);
                    left -= ((right << 4) + Key[0]) ^ (right + sum) ^ ((right >> 5) + Key[1]);
                    sum -= 0x9e3779b9;
                }
            }
            BinaryPrimitives.WriteUInt32BigEndian(mix, left);
            BinaryPrimitives.WriteUInt32BigEndian(mix[4..], right);
            for (int i = 0; i < 8; i++) padded[offset + i] = (byte)(mix[i] ^ previousCipher[i]);
            mix.CopyTo(previousMix);
            cipher.AsSpan(offset, 8).CopyTo(previousCipher);
        }
        int start = (padded[0] & 7) + 3;
        int length = padded.Length - start - 7;
        if (length < 0 || padded.AsSpan(padded.Length - 7).ContainsAnyExcept((byte)0)) throw new InvalidDataException();
        return padded.AsSpan(start, length).ToArray();
    }
}
