namespace EricGameLauncher;

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

    public static bool CheckDuplicate(string path, List<AppItem>? existingPool = null)
    {
        var items = existingPool ?? ConfigService.LoadItems();
        var hash = PathHashHelper.GetPathHash(path);
        return items.Any(i => string.Equals(i.Id, hash, StringComparison.OrdinalIgnoreCase));
    }

    public static void AddItem(AppItem item)
    {
        using (LogService.StartOperation("Item", "Add"))
        {
            var items = ConfigService.LoadItems();
            var recycleItems = ConfigService.LoadRecycleBinItems();
            items.Add(item);
            ConfigService.SaveItems(items, recycleItems);
            LogService.Write("Item", $"Added id={item.Id} title={item.Title}");
        }
    }

    public static AppItem? FindItem(string? id, string? title, List<AppItem>? pool = null)
    {
        pool ??= ConfigService.LoadItems();
        if (!string.IsNullOrEmpty(id))
            return pool.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(title))
            return pool.FirstOrDefault(i => string.Equals(i.Title, title, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public static AppItem? FindInRecycle(string? id, string? title)
    {
        var recycleItems = ConfigService.LoadRecycleBinItems();
        return FindItem(id, title, recycleItems);
    }

    public static void EditItem(AppItem item, Action<AppItem> apply)
    {
        using (LogService.StartOperation("Item", "Edit"))
        {
            apply(item);
            var items = ConfigService.LoadItems();
            var index = items.FindIndex(i => string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                items[index] = item;
            else
                items.Add(item);
            ConfigService.SaveItems(items, ConfigService.LoadRecycleBinItems());
            LogService.Write("Item", $"Edited id={item.Id}");
        }
    }

    public static void RemoveItem(string? id, string? title, bool permanent = false)
    {
        using (LogService.StartOperation("Item", "Remove"))
        {
            var items = ConfigService.LoadItems();
            var item = FindItem(id, title, items);

            if (item == null)
            {
                LogService.Write("Item", $"RemoveItem not found id={id} title={title}");
                throw new InvalidOperationException("Item not found");
            }

            if (permanent)
            {
                items.Remove(item);
                ConfigService.SaveItems(items, ConfigService.LoadRecycleBinItems());
                LogService.Write("Item", $"Permanently removed id={item.Id}");
            }
            else
            {
                item.Status = (int)AppItemStatus.Recycled;
                var recycleItems = ConfigService.LoadRecycleBinItems();
                recycleItems.Add(item);
                items.Remove(item);
                ConfigService.SaveItems(items, recycleItems);
                LogService.Write("Item", $"Moved to recycle id={item.Id}");
            }
        }
    }

    public static int RemoveItems(IEnumerable<string> ids)
    {
        using (LogService.StartOperation("Item", "RemoveItems"))
        {
            var items = ConfigService.LoadItems();
            var recycleItems = ConfigService.LoadRecycleBinItems();
            int removed = 0;

            foreach (var id in ids)
            {
                var item = FindItem(id, null, items);
                if (item == null) continue;
                item.Status = (int)AppItemStatus.Recycled;
                recycleItems.Add(item);
                items.Remove(item);
                removed++;
            }

            if (removed > 0)
                ConfigService.SaveItems(items, recycleItems);

            LogService.Write("Item", $"Removed {removed} items");
            return removed;
        }
    }

    public static void RestoreItem(string? id, string? title)
    {
        using (LogService.StartOperation("Item", "Restore"))
        {
            var recycleItems = ConfigService.LoadRecycleBinItems();
            var items = ConfigService.LoadItems();
            var item = FindItem(id, title, recycleItems);

            if (item == null)
            {
                LogService.Write("Item", $"RestoreItem not found id={id} title={title}");
                throw new InvalidOperationException("Item not found in recycle bin");
            }

            item.Status = (int)AppItemStatus.Normal;
            recycleItems.Remove(item);
            items.Add(item);
            ConfigService.SaveItems(items, recycleItems);
            LogService.Write("Item", $"Restored id={item.Id}");
        }
    }

    public static void RestoreAll()
    {
        using (LogService.StartOperation("Item", "RestoreAll"))
        {
            var recycleItems = ConfigService.LoadRecycleBinItems();
            var items = ConfigService.LoadItems();
            int restoredCount = recycleItems.Count;

            foreach (var item in recycleItems)
                item.Status = (int)AppItemStatus.Normal;

            items.AddRange(recycleItems);
            recycleItems.Clear();
            ConfigService.SaveItems(items, recycleItems);
            LogService.Write("Item", $"Restored all ({restoredCount})");
        }
    }

    public static void EmptyRecycle()
    {
        using (LogService.StartOperation("Item", "EmptyRecycle"))
        {
            var items = ConfigService.LoadItems();
            var recycleItems = ConfigService.LoadRecycleBinItems();
            recycleItems.Clear();
            ConfigService.SaveItems(items, recycleItems);
            LogService.Write("Item", "Recycle emptied");
        }
    }

    public static bool MarkPendingDeletion(string id)
    {
        using (LogService.StartOperation("Item", "MarkPendingDeletion"))
        {
            var recycleItems = ConfigService.LoadRecycleBinItems();
            var item = recycleItems.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                LogService.Write("Item", $"MarkPendingDeletion not found id={id}");
                return false;
            }
            item.Status = (int)AppItemStatus.PendingDeletion;
            item.DeletedAt = DateTimeOffset.UtcNow;
            ConfigService.SaveItems(ConfigService.LoadItems(), recycleItems);
            LogService.Write("Item", $"Marked pending deletion id={id}");
            return true;
        }
    }

    public static int MarkAllPendingDeletion()
    {
        using (LogService.StartOperation("Item", "MarkAllPendingDeletion"))
        {
            var recycleItems = ConfigService.LoadRecycleBinItems();
            var now = DateTimeOffset.UtcNow;
            var count = 0;
            foreach (var item in recycleItems.Where(i => i.Status == (int)AppItemStatus.Recycled))
            {
                item.Status = (int)AppItemStatus.PendingDeletion;
                item.DeletedAt = now;
                count++;
            }
            if (count > 0)
            {
                ConfigService.SaveItems(ConfigService.LoadItems(), recycleItems);
                LogService.Write("Item", $"Marked {count} items pending deletion");
            }
            return count;
        }
    }

    public static void AutoCleanExpired()
    {
        using (LogService.StartOperation("Item", "AutoCleanExpired"))
        {
            var recycleItems = ConfigService.LoadRecycleBinItems();
            var now = DateTimeOffset.UtcNow;
            var toRemove = recycleItems.Where(i =>
                i.Status == (int)AppItemStatus.PendingDeletion && i.DeletedAt.HasValue &&
                (now - i.DeletedAt.Value).TotalHours >= 72).ToList();

            if (toRemove.Count == 0) return;

            foreach (var item in toRemove) recycleItems.Remove(item);
            ConfigService.SaveItems(ConfigService.LoadItems(), recycleItems);
            LogService.Write("Item", $"AutoCleaned {toRemove.Count} expired items");
        }
    }

    public static (List<AppItem> normalItems, List<AppItem> recycleItems, bool changed) NormalizeState(List<AppItem> items, List<AppItem> recycleItems)
    {
        var normalItems = items.Where(x => x.Status == (int)AppItemStatus.Normal).ToList();
        var normalizedRecycle = new List<AppItem>();
        bool changed = false;
        int normalizeCount = 0;

        foreach (var item in recycleItems)
        {
            if (item.Status == (int)AppItemStatus.Normal)
            {
                item.Status = (int)AppItemStatus.Recycled;
                item.DeletedAt = null;
                changed = true;
                normalizeCount++;
            }
            normalizedRecycle.Add(item);
        }

        var misplaced = items.Where(x => x.Status != (int)AppItemStatus.Normal).ToList();
        if (misplaced.Count > 0)
        {
            var recycleIds = new HashSet<string>(normalizedRecycle.Select(x => x.Id));
            foreach (var item in misplaced)
            {
                if (recycleIds.Add(item.Id))
                {
                    normalizedRecycle.Add(item);
                    normalizeCount++;
                }
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

    public static bool HasChanged(List<AppItem> oldItems, List<AppItem> newItems,
        List<AppItem> oldRecycle, List<AppItem> newRecycle)
    {
        if (oldItems.Count != newItems.Count || oldRecycle.Count != newRecycle.Count)
            return true;

        var oldById = oldItems.ToDictionary(x => x.Id);
        foreach (var newItem in newItems)
        {
            if (!oldById.TryGetValue(newItem.Id, out var oldItem)) return true;
            if (oldItem.GetContentFingerprint() != newItem.GetContentFingerprint()) return true;
        }

        var oldRecycleById = oldRecycle.ToDictionary(x => x.Id);
        foreach (var newItem in newRecycle)
        {
            if (!oldRecycleById.TryGetValue(newItem.Id, out var oldItem)) return true;
            if (oldItem.GetContentFingerprint() != newItem.GetContentFingerprint()) return true;
        }

        return false;
    }

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
        var idx = items.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (idx <= 0) return idx;
        (items[idx], items[idx - 1]) = (items[idx - 1], items[idx]);
        return idx - 1;
    }

    public static int MoveDown(List<AppItem> items, string id)
    {
        var idx = items.FindIndex(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (idx < 0 || idx >= items.Count - 1) return idx;
        (items[idx], items[idx + 1]) = (items[idx + 1], items[idx]);
        return idx + 1;
    }

    public static bool Swap(List<AppItem> items, string id1, string id2)
    {
        var idx1 = items.FindIndex(i => string.Equals(i.Id, id1, StringComparison.OrdinalIgnoreCase));
        var idx2 = items.FindIndex(i => string.Equals(i.Id, id2, StringComparison.OrdinalIgnoreCase));
        if (idx1 < 0 || idx2 < 0) return false;
        (items[idx1], items[idx2]) = (items[idx2], items[idx1]);
        return true;
    }

    public static void SaveOrder(List<AppItem> items)
    {
        for (int i = 0; i < items.Count; i++)
            items[i].SortOrder = i;
        using (LogService.StartOperation("Sort", "SaveOrder"))
        {
            ConfigService.SaveItems(items, ConfigService.LoadRecycleBinItems());
            LogService.Write("Sort", $"Saved order for {items.Count} items");
        }
    }

    public static int ImportGames(List<ScannedGame> games)
    {
        using (LogService.StartOperation("Import", "ImportGames"))
        {
            var items = ConfigService.LoadItems();
            var addedCount = 0;

            foreach (var game in games)
            {
                var hash = PathHashHelper.GetPathHash(game.ExePath);
                if (items.Any(i => string.Equals(i.Id, hash, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var item = new AppItem { Title = game.Title };
                item.ExePath = game.ExePath;
                item.Platform = game.PlatformBadge;
                items.Add(item);
                addedCount++;
            }

            if (addedCount > 0)
            {
                ConfigService.SaveItems(items, ConfigService.LoadRecycleBinItems());
                LogService.Write("Import", $"Imported {addedCount} games");
            }

            return addedCount;
        }
    }
}
