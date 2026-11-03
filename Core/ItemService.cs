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
        item.IsPlatform = PlatformRecognition.IsPlatform(path);
        item.Pin = item.IsPlatform;
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
        target.IsPlatform = source.IsPlatform; target.Pin = source.Pin;
        target.IsAdmin = source.IsAdmin; target.MgrPath = source.MgrPath; target.IsMgrAdmin = source.IsMgrAdmin;
        target.AlternativeLaunchCommand = source.AlternativeLaunchCommand; target.UseAlternativeLaunch = source.UseAlternativeLaunch; target.IsAltAdmin = source.IsAltAdmin;
        target.AlongsideCommand = source.AlongsideCommand; target.RunAlongside = source.RunAlongside; target.IsAlongsideAdmin = source.IsAlongsideAdmin;
        target.Platform = source.Platform; target.Status = source.Status; target.DeletedAt = source.DeletedAt;
        target.CustomMenuItems = source.CustomMenuItems.Select(x => new CustomMenuItem { Title = x.Title, Command = x.Command, IsAdmin = x.IsAdmin }).ToList(); target.SortOrder = source.SortOrder; target.RecycleId = source.RecycleId;
    }

    public static AppItem Snapshot(AppItem source)
    {
        var copy = new AppItem { Id = source.Id };
        CopyContent(copy, source);
        return copy;
    }

    public static List<CustomMenuItem> ReplaceVisibleMenus(IReadOnlyList<CustomMenuItem> original, IEnumerable<CustomMenuItem> visible, int visibleCount)
    {
        var result = new List<CustomMenuItem>();
        int index = 0;
        foreach (var edited in visible.Take(visibleCount))
        {
            var before = index < original.Count ? original[index] : null;
            if (before != null && (before.Title ?? "") == (edited.Title ?? "") && (before.Command ?? "") == (edited.Command ?? "") && before.IsAdmin == edited.IsAdmin)
                result.Add(new CustomMenuItem { Title = before.Title, Command = before.Command, IsAdmin = before.IsAdmin });
            else if (!string.IsNullOrWhiteSpace(edited.Command))
                result.Add(new CustomMenuItem { Title = edited.Title?.Trim() ?? "", Command = edited.Command.Trim(), IsAdmin = edited.IsAdmin });
            index++;
        }
        result.AddRange(original.Skip(visibleCount).Select(x => new CustomMenuItem { Title = x.Title, Command = x.Command, IsAdmin = x.IsAdmin }));
        return result;
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
        IconCacheMaintenance.Register(destination);
        File.Copy(source, destination);
        item.IconPath = destination;
    }

    public static void AddItem(AppItem item, CancellationToken cancellationToken = default)
    {
        string? originalIcon = item.IconPath;
        PrepareIcon(item);
        try
        {
        ConfigService.MutateItems((items, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.RecycleId = null;
            EnsureUnique(item, items);
            items.Add(item);
        });
        }
        catch
        {
            string? imported = item.IconPath;
            item.IconPath = originalIcon;
            if (imported != originalIcon) IconCacheMaintenance.Referenced([imported]);
            throw;
        }
    }

    private static readonly string[] IconSourceExtensions = [".exe", ".com", ".lnk", ".url", ".bat", ".cmd", ".msi", ".ps1", ".appref-ms"];

    public static bool SupportsIconExtraction(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string target = path.Trim();
        if (target.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        string extension = Path.GetExtension(ProcessRunner.FileTarget(target)) ?? "";
        return extension.Length > 0 && IconSourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static string? SuggestTitle(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string target = path.Trim();
        if (target.Contains("://", StringComparison.Ordinal)) return null;
        if (target.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return null;
        string? name = Path.GetFileNameWithoutExtension(ProcessRunner.FileTarget(target));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static async Task EnsureIconAsync(AppItem item)
    {
        if (!string.IsNullOrEmpty(item.IconPath)) return;
        if (!SupportsIconExtraction(item.ExePath)) return;
        try
        {
            string source = item.ExePath!.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase) ? item.ExePath : ProcessRunner.FileTarget(item.ExePath) ?? "";
            string? iconPath = await IconHelper.GetIconPathAsync(source, item.Id);
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)) item.IconPath = iconPath;
        }
        catch (Exception ex) { LogService.Write("Item", "EnsureIconAsync failed", ex); }
    }

    public static async Task AddItemAsync(AppItem item, CancellationToken cancellationToken = default)
    {
        await PreparePlatformAsync(item, null);
        await EnsureIconAsync(item);
        cancellationToken.ThrowIfCancellationRequested();
        AddItem(item, cancellationToken);
    }

    public static async Task EditItemAsync(AppItem item, Action<AppItem> apply)
    {
        var baseline = FindItem(item.Id, null) ?? throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
        var draft = Snapshot(baseline);
        apply(draft);
        await SaveDraftAsync(baseline, draft);
        CopyContent(item, draft);
    }

    public static async Task SaveDraftAsync(AppItem baseline, AppItem draft, CancellationToken cancellationToken = default)
    {
        var destination = draft;
        draft = Snapshot(draft);
        await PreparePlatformAsync(draft, baseline);
        await EnsureIconAsync(draft);
        cancellationToken.ThrowIfCancellationRequested();
        string? originalIcon = draft.IconPath;
        PrepareIcon(draft);
        AppItem? saved = null;
        try
        {
        ConfigService.MutateItems((items, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = items.FirstOrDefault(x => x.Id.Equals(baseline.Id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            var merged = MergeDraft(baseline, draft, latest);
            EnsureUnique(merged, items.Where(x => x.Id != latest.Id));
            CopyContent(latest, merged);
            saved = merged;
        });
        CopyContent(destination, saved!);
        }
        catch
        {
            string? imported = draft.IconPath;
            draft.IconPath = originalIcon;
            if (imported != originalIcon) IconCacheMaintenance.Referenced([imported]);
            throw;
        }
    }

    private static async Task PreparePlatformAsync(AppItem draft, AppItem? baseline)
    {
        string[] detected = ["", "User", "Steam", "Epic Games", "Xbox", "Google Play Games", "WeGame"];
        bool pathChanged = baseline == null || baseline.ExePath != draft.ExePath;
        bool automatic = detected.Contains(draft.Platform ?? "", StringComparer.OrdinalIgnoreCase);
        if (!pathChanged || !automatic || (baseline != null && baseline.Platform != draft.Platform)) return;
        string command = draft.ExePath ?? "";
        string? file = ProcessRunner.FileTarget(command);
        if (file?.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) == true) command = ShortcutResolver.GetLnkTarget(file) ?? command;
        draft.Platform = (await GamePlatformHelper.DetectPlatformAsync(command))?.PlatformName ?? "User";
    }

    private static AppItem MergeDraft(AppItem baseline, AppItem draft, AppItem latest)
    {
        var merged = Snapshot(latest);
        T Merge<T>(T before, T edited, T current, string field)
        {
            if (EqualityComparer<T>.Default.Equals(before, edited)) return current;
            if (!EqualityComparer<T>.Default.Equals(before, current) && !EqualityComparer<T>.Default.Equals(edited, current))
            {
                LogService.Write("Item", $"Edit conflict id={baseline.Id} field={field}");
                throw new InvalidOperationException(Text.T("Property_EditConflict"));
            }
            return edited;
        }
        merged.Title = Merge(baseline.Title, draft.Title, latest.Title, nameof(AppItem.Title));
        merged.ExePath = Merge(baseline.ExePath, draft.ExePath, latest.ExePath, nameof(AppItem.ExePath));
        merged.IconPath = Merge(baseline.IconPath, draft.IconPath, latest.IconPath, nameof(AppItem.IconPath));
        merged.IsPlatform = Merge(baseline.IsPlatform, draft.IsPlatform, latest.IsPlatform, nameof(AppItem.IsPlatform));
        merged.Pin = Merge(baseline.Pin, draft.Pin, latest.Pin, nameof(AppItem.Pin));
        merged.IsAdmin = Merge(baseline.IsAdmin, draft.IsAdmin, latest.IsAdmin, nameof(AppItem.IsAdmin));
        merged.MgrPath = Merge(baseline.MgrPath, draft.MgrPath, latest.MgrPath, nameof(AppItem.MgrPath));
        merged.IsMgrAdmin = Merge(baseline.IsMgrAdmin, draft.IsMgrAdmin, latest.IsMgrAdmin, nameof(AppItem.IsMgrAdmin));
        merged.AlternativeLaunchCommand = Merge(baseline.AlternativeLaunchCommand, draft.AlternativeLaunchCommand, latest.AlternativeLaunchCommand, nameof(AppItem.AlternativeLaunchCommand));
        merged.UseAlternativeLaunch = Merge(baseline.UseAlternativeLaunch, draft.UseAlternativeLaunch, latest.UseAlternativeLaunch, nameof(AppItem.UseAlternativeLaunch));
        merged.IsAltAdmin = Merge(baseline.IsAltAdmin, draft.IsAltAdmin, latest.IsAltAdmin, nameof(AppItem.IsAltAdmin));
        merged.AlongsideCommand = Merge(baseline.AlongsideCommand, draft.AlongsideCommand, latest.AlongsideCommand, nameof(AppItem.AlongsideCommand));
        merged.RunAlongside = Merge(baseline.RunAlongside, draft.RunAlongside, latest.RunAlongside, nameof(AppItem.RunAlongside));
        merged.IsAlongsideAdmin = Merge(baseline.IsAlongsideAdmin, draft.IsAlongsideAdmin, latest.IsAlongsideAdmin, nameof(AppItem.IsAlongsideAdmin));
        merged.Platform = Merge(baseline.Platform, draft.Platform, latest.Platform, nameof(AppItem.Platform));
        string Menus(AppItem item) => System.Text.Json.JsonSerializer.Serialize(item.CustomMenuItems);
        string menus = Merge(Menus(baseline), Menus(draft), Menus(latest), nameof(AppItem.CustomMenuItems));
        merged.CustomMenuItems = System.Text.Json.JsonSerializer.Deserialize<List<CustomMenuItem>>(menus)!;
        return merged;
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
        AppItem? saved = null;
        ConfigService.MutateItems((items, _) =>
        {
            int index = items.FindIndex(x => x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new InvalidOperationException(Text.Cli("ErrItemNotFound"));
            var edited = Snapshot(items[index]);
            apply(edited);
            PrepareIcon(edited);
            EnsureUnique(edited, items.Where(x => !x.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)));
            items[index] = edited;
            saved = edited;
        });
        CopyContent(item, saved!);
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

    public static int RemoveItems(IEnumerable<string> ids, Func<AppItem, bool>? predicate = null)
    {
        int count = 0;
        var selected = ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ConfigService.MutateItems((items, recycled) =>
        {
            foreach (var item in items.Where(x => selected.Contains(x.Id)).ToArray())
            {
                if (predicate != null && !predicate(item)) continue;
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

    public static int ImportGames(List<ScannedGame> games) => ImportGamesDetailed(games).Changed;

    public static ScanActionResult ImportGamesDetailed(List<ScannedGame> games)
    {
        int count = 0;
        var completed = new List<string>();
        ConfigService.MutateItems((items, _) =>
        {
            var keys = new HashSet<string>(items.Select(x => PathHashHelper.PathKey(x.ExePath)), StringComparer.Ordinal);
            foreach (var game in games)
            {
                string key = PathHashHelper.PathKey(game.ExePath);
                if (key.Length == 0) continue;
                completed.Add(game.ExePath);
                if (!keys.Add(key)) continue;
                if (items.Any(item => WeGameHelper.MatchesLaunchPath(game, item.ExePath))) continue;
                items.Add(CreateItem(game.Title, game.ExePath, platform: game.PlatformBadge, mgr: game.ManagerPath));
                count++;
            }
        });
        return new(count, completed);
    }
}
