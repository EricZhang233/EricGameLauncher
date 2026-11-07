using System.Text.Json;

namespace EricGameLauncher;

internal static class StorageMigration
{
    internal static string ModeFile => Path.Combine(AppPaths.InstallDirectory, "storage.mode");
    internal sealed class Journal
    {
        public Journal() { }
        public List<string> Written { get; set; } = [];
        public List<string> Originals { get; set; } = [];
        public bool Committed { get; set; }
        public string Mode { get; set; } = "";
    }

    internal static void Transfer(string source, string destination, bool system, Action<string>? checkpoint = null, string? modeFile = null)
    {
        using var first = ConfigPersistence.Enter(string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) < 0 ? source : destination);
        using var second = ConfigPersistence.Enter(string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) < 0 ? destination : source);
        Directory.CreateDirectory(destination);
        string mode = modeFile ?? ModeFile;
        foreach (string pending in Directory.GetDirectories(destination, ".migration-backup-*"))
        {
            string journalPath = Path.Combine(pending, "journal.json");
            if (!File.Exists(journalPath)) continue;
            var previous = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException(journalPath);
            if (previous.Committed) AtomicFile.Write(mode, previous.Mode);
            else Rollback(destination, pending, previous, checkpoint);
            Directory.Delete(pending, true);
        }
        string backup = Path.Combine(destination, ".migration-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var journal = new Journal { Mode = system ? "system" : "portable" };
        bool clean = false;
        Save(backup, journal);
        try
        {
            var names = new List<string> { "settings.yaml", "items.yaml" };
            string icons = Path.Combine(source, "ico");
            if (Directory.Exists(icons)) names.AddRange(Directory.GetFiles(icons).Select(x => Path.Combine("ico", Path.GetFileName(x))));
            foreach (string name in names)
            {
                checkpoint?.Invoke("copy:" + name);
                string file = Path.Combine(source, name);
                if (!File.Exists(file)) continue;
                if (name == "settings.yaml") ConfigPersistence.Read<AppSettings>(file);
                if (name == "items.yaml") ConfigPersistence.Read<ItemsData>(file);
                string target = Path.Combine(destination, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    string saved = Path.Combine(backup, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved);
                    journal.Originals.Add(name);
                }
                journal.Written.Add(name);
                Save(backup, journal);
                if (name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)) AtomicFile.Write(target, File.ReadAllText(file));
                else File.Copy(file, target, true);
            }
            checkpoint?.Invoke("commit");
            journal.Committed = true;
            Save(backup, journal);
            AtomicFile.Write(mode, journal.Mode);
            clean = true;
            LogService.Write("Config", "Storage transfer committed");
        }
        catch
        {
            journal.Committed = false;
            Save(backup, journal);
            Rollback(destination, backup, journal, checkpoint);
            clean = true;
            throw;
        }
        finally { if (clean) Directory.Delete(backup, true); }
    }

    private static void Save(string backup, Journal journal) => AtomicFile.Write(Path.Combine(backup, "journal.json"), JsonSerializer.Serialize(journal));

    private static void Rollback(string destination, string backup, Journal journal, Action<string>? checkpoint)
    {
        foreach (string name in journal.Written.AsEnumerable().Reverse())
        {
            bool allowed = name is "settings.yaml" or "items.yaml" || (Path.GetDirectoryName(name) == "ico" && Path.GetFileName(name) != "");
            if (!allowed) throw new InvalidDataException(name);
            checkpoint?.Invoke("rollback:" + name);
            string target = Path.Combine(destination, name);
            if (journal.Originals.Contains(name)) File.Copy(Path.Combine(backup, name), target, true);
            else if (File.Exists(target)) File.Delete(target);
        }
    }
}
