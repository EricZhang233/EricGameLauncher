using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace EricGameLauncher;

public static class GooglePlayGamesHelper
{
    public static List<ScannedGame> GetInstalledGames()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Play Games");
        using var registry = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Google\Play Games");
        if (registry?.GetValue("CustomInstallLocationUserAppDataFolder") is string custom && Directory.Exists(custom)) root = custom;
        else if (registry?.GetValue("UserLocalAppDataRoot") is string local) root = Path.Combine(local, "Google", "Play Games");
        string database = Path.Combine(root, "store.db");
        if (!File.Exists(database)) return [];
        try
        {
            byte[] key = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(root, "app_library_encryption_key")), null, DataProtectionScope.CurrentUser);
            byte[]? plain = null;
            try
            {
                byte[] blob = ReadLibrary(database);
                if (blob.Length < 28) throw new InvalidDataException();
                plain = new byte[blob.Length - 28];
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(12, plain.Length), blob.AsSpan(blob.Length - 16), plain);
                var games = ParseLibrary(plain);
                string shortcuts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\Google Play Games");
                if (Directory.Exists(shortcuts))
                {
                    foreach (string file in Directory.EnumerateFiles(shortcuts, "*.lnk"))
                    {
                        string? uri = ShortcutResolver.GetShortcutInfo(file)?.TargetPath;
                        if (uri == null || !uri.StartsWith("googleplaygames://launch/", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var game in games)
                        {
                            string package = new Uri(game.ExePath).Query.TrimStart('?').Split('&').First(x => x.StartsWith("id="))[3..];
                            string? id = new Uri(uri).Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("id=", StringComparison.OrdinalIgnoreCase))?[3..];
                            if (id != null && Uri.UnescapeDataString(id) == Uri.UnescapeDataString(package)) game.ExePath = uri;
                        }
                    }
                }
                LogService.Write("Scan", $"Google Play Games discovered={games.Count}");
                return games;
            }
            finally { CryptographicOperations.ZeroMemory(key); if (plain != null) CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) { LogService.Write("Scan", "Google Play Games library unavailable", ex); throw; }
    }

    internal static List<ScannedGame> ParseLibrary(byte[] plain)
    {
        var result = new List<ScannedGame>();
        foreach (var profile in Fields(plain).Where(x => x.Number == 12 && x.Data != null))
        foreach (var state in Fields(profile.Data!).Where(x => x.Number == 2 && x.Data != null))
        foreach (var record in Fields(state.Data!).Where(x => x.Number == 2 && x.Data != null))
        {
            var entry = Fields(record.Data!);
            string id = String(entry, 1);
            if (string.IsNullOrEmpty(id) || id.StartsWith("com.google", StringComparison.OrdinalIgnoreCase) || id.StartsWith("com.android", StringComparison.OrdinalIgnoreCase)) continue;
            var value = entry.FirstOrDefault(x => x.Number == 2).Data;
            if (value == null) continue;
            var details = Fields(value);
            var app = details.FirstOrDefault(x => x.Number == 4).Data;
            if (app == null) continue;
            string title = String(Fields(app), 4);
            var display = details.FirstOrDefault(x => x.Number == 5).Data;
            if (display != null && String(Fields(display), 1) is { Length: > 0 } full) title = full;
            if (result.Any(x => x.ExePath == "googleplaygames://launch/?id=" + Uri.EscapeDataString(id))) continue;
            result.Add(new ScannedGame { Title = string.IsNullOrWhiteSpace(title) ? id : title, ExePath = "googleplaygames://launch/?id=" + Uri.EscapeDataString(id), PlatformBadge = "Google Play Games" });
        }
        return result;
    }

    private readonly record struct Field(int Number, byte[]? Data);
    private static string String(List<Field> fields, int number) => fields.FirstOrDefault(x => x.Number == number).Data is byte[] data ? Encoding.UTF8.GetString(data) : "";
    private static List<Field> Fields(byte[] bytes)
    {
        int offset = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                if (offset >= bytes.Length) throw new InvalidDataException();
                byte next = bytes[offset++];
                if (shift == 63 && next > 1) throw new InvalidDataException();
                value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw new InvalidDataException();
        }
        var fields = new List<Field>();
        while (offset < bytes.Length)
        {
            ulong tag = Varint();
            int number = checked((int)(tag >> 3));
            if (number == 0) throw new InvalidDataException();
            int wire = (int)(tag & 7);
            if (wire == 0) { Varint(); fields.Add(new(number, null)); continue; }
            int length = wire switch { 1 => 8, 5 => 4, 2 => checked((int)Varint()), _ => throw new InvalidDataException() };
            if (length > bytes.Length - offset) throw new InvalidDataException();
            fields.Add(new(number, wire == 2 ? bytes.AsSpan(offset, length).ToArray() : null));
            offset += length;
        }
        return fields;
    }

    private static byte[] ReadLibrary(string path)
    {
        var rows = SqliteLibraryReader.ReadBlobs(path, "SELECT 'library', AppLibraryState FROM AppLibrary LIMIT 1", 1);
        return rows.TryGetValue("library", out var data) ? data : throw new InvalidDataException();
    }
}
