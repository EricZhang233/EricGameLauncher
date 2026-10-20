namespace EricGameLauncher;

internal static class StorageMigration
{
    internal static string ModeFile => Path.Combine(AppContext.BaseDirectory, "storage.mode");

    internal static void Transfer(string source, string destination, bool system, Action<string>? checkpoint = null, string? modeFile = null)
    {
        using var first = ConfigPersistence.Enter(string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) < 0 ? source : destination);
        using var second = ConfigPersistence.Enter(string.Compare(source, destination, StringComparison.OrdinalIgnoreCase) < 0 ? destination : source);
        Directory.CreateDirectory(destination);
        string backup = Path.Combine(destination, ".migration-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var written = new List<string>();
        var originals = new List<string>();
        bool committed = false;
        try
        {
            foreach (string name in new[] { "settings.yaml", "items.yaml" })
            {
                checkpoint?.Invoke("copy:" + name);
                string file = Path.Combine(source, name);
                if (!File.Exists(file)) continue;
                if (name == "settings.yaml") ConfigPersistence.Read<AppSettings>(file);
                else ConfigPersistence.Read<ItemsData>(file);
                string target = Path.Combine(destination, name);
                if (File.Exists(target)) { File.Copy(target, Path.Combine(backup, name)); originals.Add(name); }
                written.Add(name);
                AtomicFile.Write(target, File.ReadAllText(file));
            }
            string icons = Path.Combine(source, "ico");
            if (Directory.Exists(icons))
            {
                Directory.CreateDirectory(Path.Combine(destination, "ico"));
                foreach (string icon in Directory.GetFiles(icons))
                {
                    string relative = Path.Combine("ico", Path.GetFileName(icon));
                    string target = Path.Combine(destination, relative);
                    if (File.Exists(target))
                    {
                        Directory.CreateDirectory(Path.Combine(backup, "ico"));
                        File.Copy(target, Path.Combine(backup, relative)); originals.Add(relative);
                    }
                    written.Add(relative);
                    File.Copy(icon, target, true);
                }
            }
            checkpoint?.Invoke("commit");
            AtomicFile.Write(modeFile ?? ModeFile, system ? "system" : "portable");
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                foreach (string relative in written.AsEnumerable().Reverse())
                {
                    string target = Path.Combine(destination, relative);
                    if (originals.Contains(relative)) File.Copy(Path.Combine(backup, relative), target, true);
                    else if (File.Exists(target)) File.Delete(target);
                }
            }
        }
    }
}
