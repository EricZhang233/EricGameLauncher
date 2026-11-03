namespace EricGameLauncher;

internal sealed class PropertyDraftSession
{
    private readonly object _gate = new();
    private readonly HashSet<string> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _savingIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deferred = new(StringComparer.OrdinalIgnoreCase);
    private int _generation;
    private int _request;
    private readonly string _lease = Guid.NewGuid().ToString("N");
    internal void Protect(string? path) => IconCacheMaintenance.ProtectDraft(_lease, path);
    internal void InvalidateRequest() { lock (_gate) _request++; }
    internal int Generation { get { lock (_gate) return _generation; } }

    internal void Advance()
    {
        lock (_gate)
        {
            _generation++;
            _request++;
            foreach (string path in _icons)
                if (_savingIcons.ContainsKey(path)) _deferred.Add(path); else Delete(path);
            _icons.Clear();
            IconCacheMaintenance.CloseDraft(_lease);
        }
    }

    internal void Commit(string? icon)
    {
        if (icon != null) lock (_gate) _icons.Remove(icon);
    }

    internal async Task SaveAsync(string? icon, Func<Task> save)
    {
        string lease = Guid.NewGuid().ToString("N");
        if (icon != null) lock (_gate) { _savingIcons[icon] = _savingIcons.GetValueOrDefault(icon) + 1; IconCacheMaintenance.ProtectDraft(lease, icon); }
        bool committed = false;
        try { await save(); committed = true; }
        finally
        {
            lock (_gate)
            {
                if (icon != null)
                {
                    if (--_savingIcons[icon] == 0) _savingIcons.Remove(icon);
                    if (committed) _icons.Remove(icon);
                    if (committed) _deferred.Remove(icon);
                    else if (!_savingIcons.ContainsKey(icon) && _deferred.Remove(icon)) Delete(icon);
                }
                IconCacheMaintenance.CloseDraft(lease);
            }
        }
    }

    internal async Task<string?> TrackAsync(Func<Task<string?>> extract)
    {
        int generation;
        int request;
        lock (_gate) { generation = _generation; request = ++_request; }
        string? path = await extract();
        if (path == null) return null;
        lock (_gate)
        {
            if (generation != _generation || request != _request) { Delete(path); return null; }
            _icons.Add(path);
            Protect(path);
            return path;
        }
    }

    private static void Delete(string path) { try { File.Delete(path); IconCacheMaintenance.Referenced([path]); } catch (Exception ex) { LogService.Write("UI", "Draft icon cleanup failed", ex); } }
}
