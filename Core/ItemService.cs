namespace EricGameLauncher;

public sealed class ItemConflictException(string key) : InvalidOperationException(key)
{
    public string Key { get; } = key;
}

public static class ItemService
{
    public static AppItem CreateItem(string title, string path, bool admin = false, string? icon = null, string? platform = null, string? mgr = null, string? alt = null, string? alongside = null)
    {
        var item = new AppItem { Title = title };
        item.ExePath = path;
        item.IsAdmin = admin;
        if (!string.IsNullOrEmpty(icon)) item.IconPath = icon;
        if (!string.IsNullOrEmpty(platform)) item.Platform = platform;
        if (!string.IsNullOrEmpty(mgr)) item.MgrPath = mgr;
        if (!string.IsNullOrEmpty(alt)) item.AlternativeLaunchCommand = alt;
        if (!string.IsNullOrEmpty(alongside)) item.AlongsideCommand = alongside;
        return item;
    }


    public static void CopyContent(AppItem target, AppItem source)
    {
        target.Title = source.Title; target.ExePath = source.ExePath; target.IconPath = source.IconPath;
        target.IsAdmin = source.IsAdmin; target.MgrPath = source.MgrPath; target.IsMgrAdmin = source.IsMgrAdmin;
        target.AlternativeLaunchCommand = source.AlternativeLaunchCommand; target.UseAlternativeLaunch = source.UseAlternativeLaunch; target.IsAltAdmin = source.IsAltAdmin;
        target.AlongsideCommand = source.AlongsideCommand; target.RunAlongside = source.RunAlongside; target.IsAlongsideAdmin = source.IsAlongsideAdmin;
        target.Platform = source.Platform; target.Status = source.Status; target.DeletedAt = source.DeletedAt;
        target.CustomMenuItems = source.CustomMenuItems; target.SortOrder = source.SortOrder; target.RecycleId = source.RecycleId;
    }

    public static bool CheckDuplicate(string path, List<AppItem>? existingPool = null)
    {
        string key = PathHashHelper.PathKey(path);
        if (key.Length == 0) return false;
        var pool = existingPool ?? ConfigService.LoadItems();
        return pool.Any(x => PathHashHelper.PathKey(x.ExePath) == key);
    }

    private static void EnsureUnique(AppItem item, IEnumerable<AppItem> others)
    {
        if (CheckDuplicate(item.ExePath ?? "", others.ToList())) throw new InvalidOperationException(Text.Cli("ErrDuplicatePath"));
    }

    private static string NewRecycleId(IEnumerable<AppItem> recycled)
    {
        var used = recycled.Select(x => x.RecycleId ?? "").Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string id;
        do { id = PathHashHelper.NewRandomId(); } while (used.Contains(id));
        return id;
    }

    private static bool HasPathConflict(AppItem item, IEnumerable<AppItem> items)
    {
        string key = PathHashHelper.PathKey(item.ExePath);
        return key.Length > 0 && items.Any(x => PathHashHelper.PathKey(x.ExePath) == key);
    }

    private static void PrepareIcon(AppItem item)
    {
        if (string.IsNullOrEmpty(item.IconPath)) return;
        string source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(item.IconPath));
        string cache = Path.GetFullPath(ConfigService.FixedCachePath) + Path.DirectorySeparatorChar;
        if (source.StartsWith(cache, StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(source)) throw new FileNotFoundException(Text.T("Icon_FileMissing"), source);
        Directory.CreateDirectory(ConfigService.FixedCachePath);
        string destination = Path.Combine(ConfigService.FixedCachePath, item.Id + "-" + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
        File.Copy(source, destination);
        item.IconPath = destination;
    }

    public static void AddItem(AppItem item)
    {
        PrepareIcon(item);
        ConfigService.MutateItems((items, _) =>
        {
            item.RecycleId = null;
            EnsureUnique(item, items);
            items.Add(item);
        });
    }

    public static AppItem? FindItem(string? id, string? title, List<AppItem>? pool = null)
    {
        pool ??= ConfigService.LoadItems();
        if (!string.IsNullOrEmpty(id))
        {
            var item = pool.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                       ?? pool.FirstOrDefault(x => string.Equals(x.RecycleId, id, StringComparison.OrdinalIgnoreCase));
            if (item != null) return item;
        }
        return !string.IsNullOrEmpty(title) ? pool.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase)) : null;
    }

    public static AppItem? FindInRecycle(string? id, string? title) => FindItem(id, title, ConfigService.LoadRecycleBinItems());

    public static void EditItem(AppItem item, Action<AppItem> apply)
    {
        apply(item);
        PrepareIcon(item);
        ConfigService.MutateItems((items, _) =>
        {
            int index = items.FindIndex(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            EnsureUnique(item, items.Where(x => !x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)));
            items[index] = item;
        });
    }

    public static void RemoveItem(string? id, string? title, bool permanent = false)
    {
        ConfigService.MutateItems((items, recycled) =>
        {
            var item = FindItem(id, title, items) ?? throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            items.Remove(item);
            if (permanent) return;
            item.Status = 1;
            item.DeletedAt = null;
            item.RecycleId = NewRecycleId(recycled);
            recycled.Add(item);
            LogService.Write("Item", $"RemoveItem recycled id={item.Id} recycleId={item.RecycleId} title={item.Title}");
        });
    }

    public static int RemoveItems(IEnumerable<string> ids)
    {
        int count = 0;
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ConfigService.MutateItems((items, recycled) =>
        {
            foreach (var item in items.Where(x => selected.Contains(x.Id)).ToArray())
            {
                items.Remove(item);
                item.Status = 1;
                item.DeletedAt = null;
                item.RecycleId = NewRecycleId(recycled);
                recycled.Add(item);
                count++;
            }
        });
        return count;
    }

    public static void RestoreItem(string? id, string? title)
    {
        ConfigService.MutateItems((items, recycled) =>
        {
            var item = FindItem(id, title, recycled) ?? throw new InvalidOperationException(Text.Cli("ErrNotInRecycle"));
            if (HasPathConflict(item, items)) throw new ItemConflictException("ErrRestoreDuplicatePath");
            if (items.Any(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))) throw new ItemConflictException("ErrRestoreIdConflict");
            recycled.Remove(item);
            item.Status = 0;
            item.DeletedAt = null;
            item.RecycleId = null;
            items.Add(item);
            LogService.Write("Item", $"RestoreItem restored id={item.Id} title={item.Title}");
        });
    }

    public static (int restored, int skipped) RestoreAll()
    {
        int restored = 0, skipped = 0;
        ConfigService.MutateItems((items, recycled) =>
        {
            foreach (var item in recycled.ToArray())
            {
                if (HasPathConflict(item, items) || items.Any(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    LogService.Write("Item", $"RestoreAll skipped id={item.Id} recycleId={item.RecycleId} title={item.Title}");
                    continue;
                }
                recycled.Remove(item);
                item.Status = 0;
                item.DeletedAt = null;
                item.RecycleId = null;
                items.Add(item);
                restored++;
            }
        });
        return (restored, skipped);
    }

    public static void EmptyRecycle() => ConfigService.MutateItems((_, recycled) => recycled.Clear());

    public static bool MarkPendingDeletion(string id)
    {
        bool found = false;
        ConfigService.MutateItems((_, recycled) =>
        {
            var item = FindItem(id, null, recycled);
            if (item == null) return;
            found = true;
            if (item.Status != 2) { item.Status = 2; item.DeletedAt = DateTimeOffset.UtcNow; }
        });
        return found;
    }

    public static int MarkAllPendingDeletion()
    {
        int count = 0;
        ConfigService.MutateItems((_, recycled) =>
        {
            foreach (var item in recycled.Where(x => x.Status == 1))
            { item.Status = 2; item.DeletedAt = DateTimeOffset.UtcNow; count++; }
        });
        return count;
    }

    public static void AutoCleanExpired() => ConfigService.MutateItems((_, recycled) =>
        recycled.RemoveAll(x => x.Status == 2 && x.DeletedAt.HasValue && DateTimeOffset.UtcNow - x.DeletedAt.Value >= TimeSpan.FromHours(72)));

    public static (List<AppItem> normalItems, List<AppItem> recycleItems, bool changed) NormalizeState(List<AppItem> items, List<AppItem> recycleItems)
    {
        var normalItems = items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
        var normalizedRecycle = new List<AppItem>();
        bool changed = false;
        int normalizeCount = 0;

        var recycleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in recycleItems)
        {
            if (item.Status == (int)AppItemStatus.Normal)
            {
                item.Status = (int)AppItemStatus.Recycled;
                item.DeletedAt = null;
                changed = true;
                normalizeCount++;
            }
            if (string.IsNullOrEmpty(item.RecycleId) || !recycleKeys.Add(item.RecycleId))
            {
                item.RecycleId = NewRecycleId(normalizedRecycle);
                recycleKeys.Add(item.RecycleId);
                changed = true;
                normalizeCount++;
            }
            normalizedRecycle.Add(item);
        }

        var misplaced = items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
        if (misplaced.Count > 0)
        {
            foreach (var item in misplaced)
            {
                item.RecycleId = NewRecycleId(normalizedRecycle);
                normalizedRecycle.Add(item);
                normalizeCount++;
            }
            changed = true;
        }

        if (changed)
        {
            LogService.Write("Item", $"NormalizeState NormalizedItems count={normalizeCount}");
            ConfigService.SaveItems(normalItems, normalizedRecycle, false);
        }

        return (normalItems, normalizedRecycle, changed);
    }


    public static bool HasChanged(List<AppItem> oldItems, List<AppItem> newItems, List<AppItem> oldRecycle, List<AppItem> newRecycle)
        => !oldItems.Select(x => x.GetContentFingerprint()).SequenceEqual(newItems.Select(x => x.GetContentFingerprint()))
        || !oldRecycle.Select(x => x.GetContentFingerprint()).SequenceEqual(newRecycle.Select(x => x.GetContentFingerprint()));

    public static List<AppItem> Search(IEnumerable<AppItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<AppItem>();

        return items.Where(i =>
            (i.Title != null && i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (i.ExePath != null && i.ExePath.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (i.TitlePinyin != null && i.TitlePinyin.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
            (i.TitlePinyinInitial != null && i.TitlePinyinInitial.Contains(query, StringComparison.OrdinalIgnoreCase))
        ).ToList();
    }


    public static int MoveUp(List<AppItem> items, string id)
    {
        int index = items.FindIndex(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (index <= 0) return -1;
        (items[index], items[index - 1]) = (items[index - 1], items[index]);
        return index - 1;
    }

    public static int MoveDown(List<AppItem> items, string id)
    {
        int index = items.FindIndex(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= items.Count - 1) return -1;
        (items[index], items[index + 1]) = (items[index + 1], items[index]);
        return index + 1;
    }

    public static bool Swap(List<AppItem> items, string id1, string id2)
    {
        int first = items.FindIndex(x => x.Id.Equals(id1, StringComparison.OrdinalIgnoreCase));
        int second = items.FindIndex(x => x.Id.Equals(id2, StringComparison.OrdinalIgnoreCase));
        if (first < 0 || second < 0) return false;
        (items[first], items[second]) = (items[second], items[first]);
        return true;
    }

    public static List<AppItem> ReorderVisibleItems(List<AppItem> items, IReadOnlyList<string> orderedIds)
    {
        var byId = items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reordered = orderedIds.Where(id => byId.ContainsKey(id) && seen.Add(id))
            .Select(id => byId[id]).ToArray();
        int index = 0;
        return items.Select(item => seen.Contains(item.Id) ? reordered[index++] : item).ToList();
    }


    public static void SaveVisibleOrder(IReadOnlyList<string> orderedIds) => ConfigService.MutateItems((items, _) =>
    {
        var ordered = ReorderVisibleItems(items, orderedIds);
        items.Clear(); items.AddRange(ordered);
    });

    public static void SaveOrder(List<AppItem> items) => SaveVisibleOrder(items.Select(x => x.Id).ToArray());

    public static int ImportGames(List<ScannedGame> games)
    {
        int count = 0;
        ConfigService.MutateItems((items, _) =>
        {
            var keys = new HashSet<string>(items.Select(x => PathHashHelper.PathKey(x.ExePath)), StringComparer.Ordinal);
            foreach (var game in games)
            {
                string key = PathHashHelper.PathKey(game.ExePath);
                if (key.Length == 0 || !keys.Add(key)) continue;
                items.Add(CreateItem(game.Title, game.ExePath, platform: game.PlatformBadge));
                count++;
            }
        });
        return count;
    }
}
