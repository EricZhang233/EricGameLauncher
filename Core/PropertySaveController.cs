namespace EricGameLauncher;

internal sealed class PropertySaveController(PropertyDraftSession resources)
{
    private readonly HashSet<int> _saving = [];
    internal bool IsSaving => _saving.Contains(resources.Generation);

    internal async Task<bool> SaveAsync(AppItem baseline, AppItem draft, bool isNew, CancellationToken lifetime,
        Func<AppItem, AppItem, bool, Task>? persist = null)
    {
        int session = resources.Generation;
        if (lifetime.IsCancellationRequested || !_saving.Add(session)) return false;
        try
        {
            var frozen = ItemService.Snapshot(draft);
            var original = ItemService.Snapshot(baseline);
            LogService.Write("UI", $"Property save session={session} id={frozen.Id}");
            await resources.SaveAsync(frozen.IconPath, async () =>
            {
                lifetime.ThrowIfCancellationRequested();
                if (persist != null) await persist(original, frozen, isNew);
                else if (isNew) await ItemService.AddItemAsync(frozen, lifetime);
                else await ItemService.SaveDraftAsync(original, frozen, lifetime);
            });
            return !lifetime.IsCancellationRequested && resources.Generation == session;
        }
        finally { _saving.Remove(session); }
    }
}
