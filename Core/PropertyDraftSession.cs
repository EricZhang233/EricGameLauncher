namespace EricGameLauncher;

internal sealed class PropertyDraftSession
{
    private readonly object _gate = new();
    private readonly HashSet<string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    internal int Generation { get { lock (_gate) return _generation; } }

    internal void Advance()
    {
        lock (_gate)
        {
            _generation++;
            foreach (string path in _icons) Delete(path);
            _icons.Clear();
        }
    }

    internal void Commit(string? icon)
    {
        if (icon != null) lock (_gate) _icons.Remove(icon);
    }

    internal async Task<string?> TrackAsync(Func<Task<string?>> extract)
    {
        int generation = Generation;
        string? path = await extract();
        if (path == null) return null;
        lock (_gate)
        {
            if (generation != _generation) { Delete(path); return null; }
            _icons.Add(path);
            return path;
        }
    }

    private static void Delete(string path) { try { File.Delete(path); } catch (Exception ex) { LogService.Write("UI", "Draft icon cleanup failed", ex); } }
}
