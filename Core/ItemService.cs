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


    public static void CopyContent(AppItem target, AppItem source)
    {
        target.Title = source.Title; target.ExePath = source.ExePath; target.IconPath = source.IconPath;
        target.IsAdmin = source.IsAdmin; target.MgrPath = source.MgrPath; target.IsMgrAdmin = source.IsMgrAdmin;
        target.AlternativeLaunchCommand = source.AlternativeLaunchCommand; target.UseAlternativeLaunch = source.UseAlternativeLaunch; target.IsAltAdmin = source.IsAltAdmin;
        target.AlongsideCommand = source.AlongsideCommand; target.RunAlongside = source.RunAlongside; target.IsAlongsideAdmin = source.IsAlongsideAdmin;
        target.Platform = source.Platform; target.Status = source.Status; target.DeletedAt = source.DeletedAt;
        target.CustomMenuItems = source.CustomMenuItems; target.SortOrder = source.SortOrder;
    }

    public static bool CheckDuplicate(string path, List<AppItem>? existingPool = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var normalized = PathHashHelper.NormalizePath(Environment.ExpandEnvironmentVariables(path.Trim()));
        var pool = existingPool ?? ConfigService.LoadItems().Concat(ConfigService.LoadRecycleBinItems()).ToList();
        return pool.Any(x => PathHashHelper.NormalizePath(Environment.ExpandEnvironmentVariables((x.ExePath ?? "").Trim())) == normalized);
    }

    private static void EnsureUnique(AppItem item, IEnumerable<AppItem> others)
    {
        if (CheckDuplicate(item.ExePath ?? "", others.ToList())) throw new InvalidOperationException(Text.Cli("ErrDuplicatePath"));
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
        ConfigService.MutateItems((items, recycled) => { EnsureUnique(item, items.Concat(recycled)); items.Add(item); });
    }

    public static AppItem? FindItem(string? id, string? title, List<AppItem>? pool = null)
    {
        pool ??= ConfigService.LoadItems();
        return !string.IsNullOrEmpty(id) ? pool.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            : !string.IsNullOrEmpty(title) ? pool.FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase)) : null;
    }

    public static AppItem? FindInRecycle(string? id, string? title) => FindItem(id, title, ConfigService.LoadRecycleBinItems());

    public static void EditItem(AppItem item, Action<AppItem> apply)
    {
        apply(item);
        PrepareIcon(item);
        ConfigService.MutateItems((items, recycled) =>
        {
            int index = items.FindIndex(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            EnsureUnique(item, items.Concat(recycled).Where(x => x.Id != item.Id));
            items[index] = item;
        });
    }

    public static void RemoveItem(string? id, string? title, bool permanent = false)
    {
        ConfigService.MutateItems((items, recycled) =>
        {
            var item = FindItem(id, title, items) ?? throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            items.Remove(item);
            if (!permanent) { item.Status = 1; item.DeletedAt = null; recycled.Add(item); }
        });
    }

    public static int RemoveItems(IEnumerable<string> ids)
    {
        int count = 0;
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ConfigService.MutateItems((items, recycled) =>
        {
            foreach (var item in items.Where(x => selected.Contains(x.Id)).ToArray())
            { items.Remove(item); item.Status = 1; item.DeletedAt = null; recycled.Add(item); count++; }
        });
        return count;
    }

    public static void RestoreItem(string? id, string? title)
    {
        ConfigService.MutateItems((items, recycled) =>
        {
            var item = FindItem(id, title, recycled) ?? throw new InvalidOperationException(Text.Cli("ErrNotInRecycle"));
            EnsureUnique(item, items);
            recycled.Remove(item); item.Status = 0; item.DeletedAt = null; items.Add(item);
        });
    }

    public static void RestoreAll() => ConfigService.MutateItems((items, recycled) =>
    {
        foreach (var item in recycled) { EnsureUnique(item, items); item.Status = 0; item.DeletedAt = null; items.Add(item); }
        recycled.Clear();
    });

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
        ConfigService.MutateItems((items, recycled) =>
        {
            foreach (var game in games)
            {
                if (CheckDuplicate(game.ExePath, items.Concat(recycled).ToList())) continue;
                items.Add(CreateItem(game.Title, game.ExePath, platform: game.PlatformBadge));
                count++;
            }
        });
        return count;
    }
}
