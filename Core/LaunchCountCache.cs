using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EricGameLauncher;

public static class LaunchCountCache
{
    private static readonly object Gate = new();
    private static string? _loadedPath;
    private static DateTime _stamp;
    private static long _length = -1;
    private static Dictionary<string, long> _counts = new(StringComparer.OrdinalIgnoreCase);
    public static event Action? Changed;

    public static string CacheFilePath => Path.Combine(ConfigService.SystemCachePath, "launch-counts-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(ConfigService.CurrentDataPath).ToUpperInvariant())))[..16] + ".json");

    public static void Populate(IEnumerable<AppItem> items)
    {
        lock (Gate)
        {
            Load(CacheFilePath, false);
            foreach (var item in items) item.LaunchCount = _counts.GetValueOrDefault(item.Id);
        }
    }

    public static long Increment(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;
        long count;
        lock (Gate)
        {
            string path = CacheFilePath;
            using var lease = ConfigPersistence.Enter(path);
            Load(path, true);
            long previous = _counts.GetValueOrDefault(id);
            count = previous == long.MaxValue ? previous : previous + 1;
            var next = new Dictionary<string, long>(_counts, StringComparer.OrdinalIgnoreCase) { [id] = count };
            AtomicFile.Write(path, JsonSerializer.Serialize(next));
            _counts = next;
            Remember(path);
            LogService.Write("Launch", $"Launch count cached id={id} count={count}");
        }
        Changed?.Invoke();
        return count;
    }

    private static void Load(string path, bool force)
    {
        var info = new FileInfo(path);
        if (!force && _loadedPath == path && _stamp == info.LastWriteTimeUtc && _length == (info.Exists ? info.Length : -1)) return;
        try
        {
            var loaded = info.Exists ? ReadFile(path) : new();
            if (loaded.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value < 0)) throw new InvalidDataException();
            _counts = new Dictionary<string, long>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogService.Write("Launch", "Launch count cache unavailable; reset disposable statistics", ex);
            _counts = new(StringComparer.OrdinalIgnoreCase);
        }
        _loadedPath = path;
        _stamp = info.LastWriteTimeUtc;
        _length = info.Exists ? info.Length : -1;
    }

    private static void Remember(string path)
    {
        var info = new FileInfo(path);
        _loadedPath = path;
        _stamp = info.LastWriteTimeUtc;
        _length = info.Exists ? info.Length : -1;
    }

    private static Dictionary<string, long> ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<Dictionary<string, long>>(stream) ?? throw new InvalidDataException();
    }
}
