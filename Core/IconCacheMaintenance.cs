using System.Diagnostics;
using System.Text.Json;

namespace EricGameLauncher;

internal static class IconCacheMaintenance
{
    internal sealed record Owner(int ProcessId, long Started);
    internal sealed class Draft
    {
        public Draft() { }
        public Owner Owner { get; set; } = CurrentOwner;
        public HashSet<string> Files { get; set; } = [];
    }
    internal sealed class State
    {
        public State() { }
        public Dictionary<string, List<Owner>> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Draft> Drafts { get; set; } = [];
        public List<string> JumpReferences { get; set; } = [];
    }
    private static readonly Owner CurrentOwner = new(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks);

    internal static void Register(string path) => Change(state =>
    {
        string? name = OwnedName(path);
        if (name == null) return;
        if (!state.Files.TryGetValue(name, out var owners)) state.Files[name] = owners = [];
        if (!owners.Contains(CurrentOwner)) owners.Add(CurrentOwner);
    });

    internal static void ProtectDraft(string lease, string? path) => Change(state =>
    {
        string? name = OwnedName(path);
        if (name == null) return;
        if (!state.Drafts.TryGetValue(lease, out var draft)) state.Drafts[lease] = draft = new();
        draft.Files.Add(name);
    });

    internal static void CloseDraft(string lease) => Change(state => state.Drafts.Remove(lease), true);

    internal static void Referenced(IEnumerable<string?> paths) => Change(state =>
    {
        foreach (string? path in paths)
        {
            string? name = OwnedName(path);
            if (name != null && state.Files.TryGetValue(name, out var owners)) owners.RemoveAll(x => x == CurrentOwner);
        }
    }, true);

    internal static void JumpReferences(IEnumerable<string> paths) => Change(state =>
    {
        state.JumpReferences = paths.Select(OwnedName).OfType<string>().ToList();
        foreach (string name in state.JumpReferences)
            if (state.Files.TryGetValue(name, out var owners)) owners.RemoveAll(x => x == CurrentOwner);
    }, true);

    internal static bool ProtectJumpReferences(IEnumerable<string> paths) => Change(state =>
    {
        state.JumpReferences = state.JumpReferences.Concat(paths.Select(OwnedName).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    });

    internal static void Sweep() => Change(_ => { }, true);

    private static string? OwnedName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = Path.GetFullPath(path);
        return string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(ConfigService.FixedCachePath)), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(full) : null;
    }

    private static bool Alive(Owner owner)
    {
        if (owner.ProcessId == 0) return false;
        try { using var process = Process.GetProcessById(owner.ProcessId); return process.StartTime.ToUniversalTime().Ticks == owner.Started; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch { return true; }
    }

    private static bool Change(Action<State> change, bool sweep = false)
    {
        try
        {
            lock (ConfigPersistence.Gate)
            {
                if (string.IsNullOrEmpty(ConfigService.CurrentDataPath)) return false;
                using var lease = ConfigPersistence.Enter(ConfigService.CurrentDataPath);
                Directory.CreateDirectory(ConfigService.FixedCachePath);
                string ledger = Path.Combine(ConfigService.FixedCachePath, ".owned-icons.json");
                var state = File.Exists(ledger) ? JsonSerializer.Deserialize<State>(File.ReadAllText(ledger)) ?? throw new InvalidDataException(ledger) : new State();
                state.Files = new Dictionary<string, List<Owner>>(state.Files, StringComparer.OrdinalIgnoreCase);
                change(state);
                if (sweep)
                {
                    var items = ConfigPersistence.Read<ItemsData>(ConfigService.ItemsFilePath);
                    if (items.Version != ConfigService.CurrentConfigVersion) throw new InvalidDataException(ConfigService.ItemsFilePath);
                    var keep = items.Items.Concat(items.RecycleBinItems)
                        .Where(x => !string.IsNullOrEmpty(x.Icon))
                        .Select(x => OwnedName(Path.IsPathRooted(x.Icon!) ? x.Icon! : Path.Combine(ConfigService.FixedCachePath, Path.GetFileName(x.Icon!))))
                        .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
                    keep.UnionWith(state.JumpReferences);
                    foreach (var entry in state.Drafts.ToArray())
                    {
                        if (Alive(entry.Value.Owner)) keep.UnionWith(entry.Value.Files);
                        else state.Drafts.Remove(entry.Key);
                    }
                    foreach (var entry in state.Files.ToArray())
                    {
                        entry.Value.RemoveAll(x => !Alive(x));
                        if (Path.GetFileName(entry.Key) != entry.Key || keep.Contains(entry.Key) || entry.Value.Count > 0) continue;
                        string path = Path.Combine(ConfigService.FixedCachePath, entry.Key);
                        try { if (File.Exists(path)) File.Delete(path); state.Files.Remove(entry.Key); }
                        catch (IOException ex) { LogService.Write("Cache", "Owned icon remains in use", ex); }
                    }
                }
                AtomicFile.Write(ledger, JsonSerializer.Serialize(state));
                return true;
            }
        }
        catch (Exception ex) { LogService.Write("Cache", "Icon maintenance deferred", ex); return false; }
    }
}
