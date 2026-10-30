using System.Runtime.InteropServices;
using System.Windows.Shell;

namespace EricGameLauncher;

public static class JumpListService
{
    public const string AppId = "EricSoft.EricGameLauncher";
    private static readonly object Gate = new();
    private static List<AppItem>? _catalog;
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
        var thread = new Thread(() =>
        {
            lock (Gate)
            {
                try
                {
                    if (reloadItems) _catalog = ConfigService.LoadItems().Where(x => x.Status == 0).ToList();
                    if (_catalog == null) return;
                    var items = _catalog;
                    if (!reloadItems) LaunchCountCache.Populate(items);
                    var list = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };
                    foreach (var group in TaskGroups(items))
                    {
                        if (list.JumpItems.Count > 0) list.JumpItems.Add(new JumpTask());
                        foreach (var item in group) list.JumpItems.Add(Task(item));
                    }
                    list.JumpItemsRejected += (_, e) => LogService.Write("JumpList", $"Rejected {e.RejectedItems.Count} items");
                    list.Apply();
                    LogService.Write("JumpList", $"Updated tasks={list.JumpItems.Count}");
                }
                catch (Exception ex) { LogService.Write("JumpList", "Refresh failed", ex); }
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    public static void UpdateShortcutIds()
    {
        foreach (string path in new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "EricGameLauncher.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\EricGameLauncher.lnk") })
        {
            if (!File.Exists(path)) continue;
            object? link = null;
            IntPtr text = IntPtr.Zero;
            try
            {
                link = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
                var file = (System.Runtime.InteropServices.ComTypes.IPersistFile)link!;
                file.Load(path, 2);
                var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
                text = Marshal.StringToCoTaskMemUni(AppId);
                var value = new PropertyValue { Type = 31, Pointer = text };
                ((IPropertyStore)link!).SetValue(ref key, ref value);
                ((IPropertyStore)link).Commit();
                file.Save(path, true);
            }
            catch (Exception ex) { LogService.Write("JumpList", "Shortcut identity update failed", ex); }
            finally { if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text); if (link != null) Marshal.ReleaseComObject(link); }
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropertyValue { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropertyValue value);
        void SetValue(ref PropertyKey key, ref PropertyValue value);
        void Commit();
    }

    private static JumpTask Task(AppItem item)
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "EricGameLauncher.exe");
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
        return new JumpTask { Title = item.Title ?? item.Id, ApplicationPath = exe, Arguments = "--jump-launch " + item.Id,
            WorkingDirectory = AppContext.BaseDirectory, IconResourcePath = icon, IconResourceIndex = 0 };
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
        File.WriteAllBytes(target, output.ToArray());
        return target;
    }

}
