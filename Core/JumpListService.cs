using System.Runtime.InteropServices;

namespace EricGameLauncher;

public static class JumpListService
{
    public const string AppId = "EricSoft.EricGameLauncher";
    private static List<AppItem>? _catalog;
    private static readonly StaRefreshQueue Queue = new(RefreshSnapshot, ex =>
    {
        _catalog = null;
        LogService.Write("JumpList", "Refresh failed; next request will reload configuration", ex);
    });

    public static void FlushPending()
    {
        if (!Queue.Idle.Wait(TimeSpan.FromSeconds(5)))
            LogService.Write("JumpList", "Refresh did not finish within the exit deadline");
    }
    internal static List<List<AppItem>> TaskGroups(List<AppItem> items)
    {
        var active = items.Where(x => x.Status == 0).ToList();
        return new List<List<AppItem>> {
            active.Where(x => x.IsPlatform && x.Pin).ToList(),
            active.Where(x => !x.IsPlatform && x.Pin)
                .Concat(active.Where(x => !x.IsPlatform && !x.Pin).OrderByDescending(x => x.LaunchCount)).ToList()
        }.Where(x => x.Count > 0).ToList();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);

    public static void Initialize()
    {
        if (DebugPaths.IsDebug()) return;
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
        UpdateShortcutIds();
        ConfigService.DataChanged += Refresh;
        LaunchCountCache.Changed += RefreshCounts;
        Refresh();
    }

    public static void Refresh()
    {
        RefreshCore(true);
    }

    private static void RefreshCounts() => RefreshCore(false);

    private static void RefreshCore(bool reloadItems)
    {
        if (DebugPaths.IsDebug()) return;
        try { Queue.Request(reloadItems); }
        catch (Exception ex) { LogService.Write("JumpList", "Could not schedule refresh", ex); }
    }

    private static void RefreshSnapshot(bool reloadItems)
    {
        using var publication = EnterPublication();
        if (reloadItems || _catalog == null) _catalog = ConfigService.LoadItems().Where(x => x.Status == 0).ToList();
        var items = _catalog;
        LaunchCountCache.Populate(items);
        var tasks = new List<NativeJumpList.Task?>();
        foreach (var group in TaskGroups(items))
        {
            if (tasks.Count > 0) tasks.Add(null);
            foreach (var item in group) tasks.Add(Task(item));
        }
        if (!IconCacheMaintenance.ProtectJumpReferences(tasks.OfType<NativeJumpList.Task>().Select(x => x.IconResourcePath)))
            throw new DataFileException(Path.Combine(ConfigService.FixedCachePath, ".owned-icons.json"));
        var published = NativeJumpList.Publish(AppId, tasks);
        IconCacheMaintenance.JumpReferences(published.Select(x => x.IconResourcePath));
        LogService.Write("JumpList", $"Native tasks updated count={published.Count}");
    }

    internal static IDisposable EnterPublication(TimeSpan? timeout = null)
        => ConfigPersistence.Enter(Path.Combine(ConfigService.SystemCachePath, "jump-list-publication"), timeout);

    public static void UpdateShortcutIds()
    {
        foreach (string path in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EricGameLauncher.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\EricGameLauncher.lnk") })
        {
            if (!File.Exists(path)) continue;
            object? link = null;
            try
            {
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
                var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link!;
                file.Load(path, 2);
                NativeJumpList.SetAppId(link!, AppId);
                file.Save(path, true);
            }
            catch (Exception ex) { LogService.Write("JumpList", "Shortcut identity update failed", ex); }
            finally { if (link != null) Marshal.ReleaseComObject(link); }
        }
    }

    private static NativeJumpList.Task Task(AppItem item)
    {
        string exe = Path.Combine(AppPaths.InstallDirectory, "EricGameLauncher.exe");
        string icon = item.IconPath ?? "";
        if (File.Exists(icon) && !icon.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try { icon = CacheIcon(icon); }
            catch (Exception ex) { LogService.Write("JumpList", "Icon conversion failed", ex); }
        }
        if (!File.Exists(icon) || !Path.GetExtension(icon).Equals(".ico", StringComparison.OrdinalIgnoreCase))
        {
            var (target, _) = ProcessRunner.SplitPath(item.ExePath ?? "");
            icon = File.Exists(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : exe;
        }
        return new NativeJumpList.Task(item.Title ?? item.Id, exe, "--jump-launch " + item.Id, AppPaths.InstallDirectory, icon, 0);
    }

    private static string CacheIcon(string source)
    {
        string stamp = source + File.GetLastWriteTimeUtc(source).Ticks;
        string target = Path.Combine(ConfigService.FixedCachePath, "jl-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp)))[..24] + ".ico");
        if (File.Exists(target)) return target;
        using var image = System.Drawing.Image.FromFile(source);
        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var frames = sizes.Select(size =>
        {
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, size, size);
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            return memory.ToArray();
        }).ToArray();
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
        int offset = 6 + sizes.Length * 16;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
            writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
            writer.Write(frames[i].Length); writer.Write(offset); offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        Directory.CreateDirectory(ConfigService.FixedCachePath);
        IconCacheMaintenance.Register(target);
        File.WriteAllBytes(target, output.ToArray());
        return target;
    }

}
