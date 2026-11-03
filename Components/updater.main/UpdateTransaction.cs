using System.IO.Compression;
using System.Text.Json;

namespace updater.main;

public static class UpdateTransaction
{
    public const string ManifestName = "._update_manifest.json";
    public sealed class Journal
    {
        public List<string> BackedUp { get; set; } = [];
        public List<string> Installed { get; set; } = [];
        public bool Committed { get; set; }
    }

    public static void Apply(string installDirectory, ZipArchive archive, Action<string>? checkpoint = null)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        using var lease = AcquireLease(root);
        string staging = Path.Combine(root, "._update_staging");
        string backup = Path.Combine(root, "._update_backup");
        if (Directory.Exists(backup))
        {
            Recover(root, backup, null);
            Directory.Delete(backup, true);
        }
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var journal = new Journal();
        bool rollbackComplete = false;
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string relative = Path.GetRelativePath(staging, Contained(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                string top = relative.Split(Path.DirectorySeparatorChar)[0];
                if (top.Equals("Data", StringComparison.OrdinalIgnoreCase) || top.Equals("storage.mode", StringComparison.OrdinalIgnoreCase)) continue;
                if (top.StartsWith("._update_", StringComparison.OrdinalIgnoreCase) || top.StartsWith(".migration-", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("UPDATE_RESERVED_PATH");
                string destination = Contained(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
            if (!File.Exists(Path.Combine(staging, "EricGameLauncher.exe"))) throw new InvalidDataException("The package has no launcher executable.");
            var incoming = Directory.GetFiles(staging, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(staging, file)).ToArray();
            string manifest = Path.Combine(root, ManifestName);
            var previous = File.Exists(manifest)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? throw new InvalidDataException("UPDATE_INVALID_MANIFEST")
                : [];
            foreach (string relative in previous)
            {
                Contained(root, relative);
                string normalized = Path.GetRelativePath(root, Contained(root, relative));
                string top = normalized.Split(Path.DirectorySeparatorChar)[0];
                if (top.Equals("Data", StringComparison.OrdinalIgnoreCase) || top.Equals("storage.mode", StringComparison.OrdinalIgnoreCase)
                    || top.StartsWith("._update_", StringComparison.OrdinalIgnoreCase) || top.StartsWith(".migration-", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("UPDATE_INVALID_MANIFEST");
            }
            File.WriteAllText(Path.Combine(staging, ManifestName), JsonSerializer.Serialize(incoming));
            Directory.CreateDirectory(backup);
            Save(backup, journal);
            foreach (string relative in previous.Concat(incoming).Append(ManifestName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string file = Contained(root, relative);
                if (!File.Exists(file)) continue;
                journal.BackedUp.Add(relative); Save(backup, journal);
                string target = Contained(Path.Combine(backup, "files"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                checkpoint?.Invoke("backup:" + relative);
                File.Move(file, target);
            }
            foreach (string file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staging, file);
                journal.Installed.Add(relative); Save(backup, journal);
                checkpoint?.Invoke("install:" + relative);
                string target = Contained(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, true);
            }
            checkpoint?.Invoke("commit");
            journal.Committed = true; Save(backup, journal);
        }
        catch
        {
            if (Directory.Exists(backup))
            {
                Recover(root, backup, checkpoint);
                rollbackComplete = true;
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if ((journal.Committed || rollbackComplete) && Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    private static void Recover(string root, string backup, Action<string>? checkpoint)
    {
        string path = Path.Combine(backup, "journal.json");
        if (!File.Exists(path)) throw new IOException("An unfinished update backup must be recovered manually: " + backup);
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ?? throw new InvalidDataException(path);
        if (journal.Committed) return;
        foreach (string relative in journal.Installed.AsEnumerable().Reverse())
        {
            string target = Contained(root, relative);
            checkpoint?.Invoke("rollback-delete:" + relative);
            if (File.Exists(target)) File.Delete(target);
        }
        foreach (string relative in journal.BackedUp)
        {
            string source = Contained(Path.Combine(backup, "files"), relative);
            if (!File.Exists(source)) continue;
            checkpoint?.Invoke("rollback-restore:" + relative);
            string target = Contained(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
    }

    private static FileStream AcquireLease(string root)
    {
        try
        {
            return new FileStream(Path.Combine(root, "._update_lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
        {
            throw new IOException("UPDATE_IN_PROGRESS", ex);
        }
    }

    internal static string Contained(string root, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (path.Length <= root.Length || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(relative);
        return path;
    }

    private static void Save(string backup, Journal journal)
    {
        string path = Path.Combine(backup, "journal.json");
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, journal); stream.Flush(true); }
        File.Move(temp, path, true);
    }
}
