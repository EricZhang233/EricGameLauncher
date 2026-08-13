using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace EricGameLauncher;

public class ShortcutInfo
{
    public string? TargetPath { get; set; }
    public string? Arguments { get; set; }
    public string? IconPath { get; set; }
    public int IconIndex { get; set; }
    public bool IsUrl { get; set; }
    public string? ActualUrl { get; set; }
    public string? AUMID { get; set; }
    public GamePlatformInfo? Platform { get; set; }
}

public static class ShortcutResolver
{
    public static string? GetLnkTarget(string lnkPath)
    {
        using (LogService.StartOperation("Shell", "GetLnkTarget"))
        {
            LogService.Write("Shell", $"GetLnkTarget called lnkPath={lnkPath} exists={File.Exists(lnkPath)}");
            if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
                return null;

            var info = GetShortcutInfo(lnkPath);
            if (info != null && !string.IsNullOrEmpty(info.AUMID))
                return $"{LauncherConstants.UwpAppsFolderPrefix}{info.AUMID}";

            return info?.TargetPath;
        }
    }

    public static ShortcutInfo? GetShortcutInfo(string lnkPath)
    {
        using (LogService.StartOperation("Shell", "GetShortcutInfo"))
        {
            LogService.Write("Shell", $"GetShortcutInfo called lnkPath={lnkPath} exists={File.Exists(lnkPath)}");
            if (string.IsNullOrEmpty(lnkPath) || !File.Exists(lnkPath))
                return null;

            try
            {
                Type? wshType = Type.GetTypeFromProgID("WScript.Shell");
                if (wshType == null) return null;

                dynamic? wsh = Activator.CreateInstance(wshType);
                if (wsh == null) return null;

                try
                {
                    dynamic link = wsh.CreateShortcut(lnkPath);
                    if (link == null) return null;

                    var info = new ShortcutInfo
                    {
                        TargetPath = link.TargetPath as string,
                        Arguments = link.Arguments as string,
                        IconPath = link.IconLocation as string
                    };

                    try
                    {
                        Type? shellAppType = Type.GetTypeFromProgID("Shell.Application");
                        if (shellAppType != null)
                        {
                            dynamic? shellApp = Activator.CreateInstance(shellAppType);
                            if (shellApp != null)
                            {
                                string? dir = Path.GetDirectoryName(lnkPath);
                                string file = Path.GetFileName(lnkPath);
                                if (!string.IsNullOrEmpty(dir))
                                {
                                    var folder = shellApp.NameSpace(dir);
                                    var folderItem = folder?.ParseName(file);
                                    if (folderItem != null)
                                    {
                                        var aumidProp = folderItem.ExtendedProperty("System.AppUserModel.ID");
                                        if (aumidProp != null) info.AUMID = aumidProp.ToString();

                                        if (string.IsNullOrEmpty(info.TargetPath))
                                        {
                                            var parsingPath = folderItem.ExtendedProperty("System.Link.TargetParsingPath");
                                            if (parsingPath != null) info.TargetPath = parsingPath.ToString();
                                        }

                                        if (string.IsNullOrEmpty(info.AUMID) && !string.IsNullOrEmpty(info.TargetPath))
                                        {
                                            string tPath = info.TargetPath;
                                            if (tPath.StartsWith(LauncherConstants.UwpAppsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                                                tPath = tPath.Substring(LauncherConstants.UwpAppsFolderPrefix.Length);

                                            if (tPath.Contains("!") || (!tPath.Contains("\\") && !tPath.Contains("/")))
                                            {
                                                info.AUMID = tPath;
                                            }
                                        }
                                    }
                                }
                                if (Marshal.IsComObject(shellApp)) Marshal.ReleaseComObject(shellApp);
                            }
                        }
                    }
                    catch (Exception ex) { LogService.Write("App", "Shortcut resolution shellApp failed", ex); }

                    if (!string.IsNullOrEmpty(info.IconPath))
                    {
                        var parts = info.IconPath.Split(',');
                        if (parts.Length > 1 && int.TryParse(parts[1], out int index))
                        {
                            info.IconPath = parts[0];
                            info.IconIndex = index;
                        }
                    }

                    LogService.Write("Shell", $"GetShortcutInfo result TargetPath={info.TargetPath} Arguments={info.Arguments} IconPath={info.IconPath} IconIndex={info.IconIndex} AUMID={info.AUMID}");
                    return info;
                }
                finally
                {
                    if (Marshal.IsComObject(wsh))
                        Marshal.ReleaseComObject(wsh);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("App", "GetShortcutInfo failed", ex);
                return null;
            }
        }
    }

    public static ShortcutInfo? GetUrlFileInfo(string urlPath)
    {
        using (LogService.StartOperation("Shell", "GetUrlFileInfo"))
        {
            if (string.IsNullOrEmpty(urlPath) || !File.Exists(urlPath))
                return null;

            try
            {
                string[] lines = File.ReadAllLines(urlPath);
                string? targetUrl = null;
                string? iconFile = null;
                int iconIndex = 0;

                foreach (string line in lines)
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                        targetUrl = line.Substring(4).Trim();
                    else if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                        iconFile = line.Substring(9).Trim();
                    else if (line.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.Substring(10).Trim(), out iconIndex);
                }

                if (string.IsNullOrEmpty(targetUrl)) return null;

                LogService.Write("Shell", $"GetUrlFileInfo parsed targetUrl={targetUrl} iconFile={iconFile} iconIndex={iconIndex} lines={lines.Length}");

                return new ShortcutInfo
                {
                    TargetPath = targetUrl,
                    IsUrl = true,
                    ActualUrl = targetUrl,
                    IconPath = iconFile,
                    IconIndex = iconIndex
                };
            }
            catch (Exception ex)
            {
                LogService.Write("App", "GetUrlFileInfo failed", ex);
                return null;
            }
        }
    }

    public static void CreateShortcut(string targetPath, string shortcutPath, string description = "")
    {
        using (LogService.StartOperation("Shell", "CreateShortcut"))
        {
            LogService.Write("Shell", $"CreateShortcut targetPath={targetPath} shortcutPath={shortcutPath} descriptionLen={(description?.Length ?? 0)}");
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return;

                try
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                    shortcut.Description = description;
                    shortcut.Save();
                }
                finally
                {
                    if (Marshal.IsComObject(shell))
                        Marshal.ReleaseComObject(shell);
                }
            }
            catch (Exception ex)
            {
                LogService.Write("App", "CreateShortcut failed", ex);
            }
        }
    }
}

public static class ShortcutScanner
{
    public class FileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsFolder { get; set; }
        public List<FileItem> Children { get; set; } = [];
    }

    private static readonly string[] SupportedExtensions = [".lnk", ".url", ".exe"];

    public static List<FileItem> GetStartMenuItems()
    {
        using (LogService.StartOperation("Shortcut", "GetStartMenuItems"))
        {
            var items = new List<FileItem>();
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return items;

                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell == null) return items;

                var appsFolder = shell.NameSpace("shell:AppsFolder");
                if (appsFolder != null)
                {
                    foreach (var item in appsFolder.Items())
                    {
                        try
                        {
                            string name = item.Name;
                            string path = item.Path;

                            string finalPath = path;
                            bool isPhysical = File.Exists(path) || Directory.Exists(path);

                            if (!isPhysical)
                            {
                                try
                                {
                                    var targetProperty = item.ExtendedProperty("System.Link.TargetParsingPath");
                                    if (targetProperty != null)
                                    {
                                        string tPath = targetProperty.ToString() ?? "";
                                        if (!string.IsNullOrEmpty(tPath) && (File.Exists(tPath) || Directory.Exists(tPath)))
                                        {
                                            finalPath = tPath;
                                            isPhysical = true;
                                        }
                                    }

                                    if (!isPhysical && item.IsLink)
                                    {
                                        var link = item.GetLink;
                                        string? lPath = link?.Path;
                                        if (!string.IsNullOrEmpty(lPath) && (File.Exists(lPath) || Directory.Exists(lPath)))
                                        {
                                            finalPath = lPath;
                                            isPhysical = true;
                                        }
                                    }
                                }
                                catch (Exception ex) { LogService.Write("App", "StartMenu apps folder item parse failed", ex); }
                            }

                            if (!string.IsNullOrEmpty(name))
                            {
                                items.Add(new FileItem
                                {
                                    Name = name,
                                    FullPath = (!isPhysical && (path.Contains("!") || !path.Contains("\\"))) ? $"shell:AppsFolder\\{path}" : finalPath,
                                    IsFolder = false
                                });
                            }
                        }
                        catch (Exception ex) { LogService.Write("App", "StartMenu apps folder item parse inner failed", ex); }
                    }
                }

                if (Marshal.IsComObject(shell))
                    Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex) { LogService.Write("App", "GetStartMenuItems failed", ex); }

            LogService.Write("Shortcut", $"GetStartMenuItems foundItems={items.Count}");
            return items.OrderBy(x => x.Name).ToList();
        }
    }

    public static List<FileItem> GetDesktopItems()
    {
        using (LogService.StartOperation("Shortcut", "GetDesktopItems"))
        {
            var items = new List<FileItem>();
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            if (Directory.Exists(userDesktop))
                items.AddRange(ScanDirectory(userDesktop, recursive: true, maxDepth: 3));
            if (Directory.Exists(publicDesktop))
                items.AddRange(ScanDirectory(publicDesktop, recursive: true, maxDepth: 3));

            return MergeItems(items);
        }
    }

    private static List<FileItem> MergeItems(List<FileItem> items)
    {
        using (LogService.StartOperation("Shortcut", "MergeItems"))
        {
            var merged = new List<FileItem>();
        var folderMap = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
        var fileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                if (folderMap.TryGetValue(item.Name, out var existing))
                {
                    existing.Children.AddRange(item.Children);
                    existing.Children = MergeItems(existing.Children);
                }
                else
                {
                    item.Children = MergeItems(item.Children);
                    folderMap[item.Name] = item;
                    merged.Add(item);
                }
            }
            else
            {
                if (fileSet.Add(item.Name))
                    merged.Add(item);
            }
        }

            LogService.Write("Shortcut", $"MergeItems resultCount={merged.Count}");
            return merged.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name).ToList();
        }
    }

    private static List<FileItem> ScanDirectory(string path, bool recursive = true, int maxDepth = 99)
    {
        using (LogService.StartOperation("Shortcut", "ScanDirectory"))
        {
            var items = new List<FileItem>();
            if (maxDepth <= 0) return items;

            LogService.Write("Shortcut", $"ScanDirectory start path={path} recursive={recursive} maxDepth={maxDepth}");

            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (SupportedExtensions.Contains(ext))
                    {
                        items.Add(new FileItem { Name = Path.GetFileNameWithoutExtension(file), FullPath = file, IsFolder = false });
                    }
                }

                if (recursive && maxDepth > 1)
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        string dirName = Path.GetFileName(dir);
                        var children = ScanDirectory(dir, recursive: true, maxDepth: maxDepth - 1);
                        if (children.Count > 0)
                        {
                            items.Add(new FileItem
                            {
                                Name = dirName,
                                FullPath = dir,
                                IsFolder = true,
                                Children = children.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name).ToList()
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { LogService.Write("App", "ScanDirectory failed", ex); }
            LogService.Write("Shortcut", $"ScanDirectory foundItems={items.Count}");
            return items;
        }
    }
}

public static class Win32FileDialog
{
    public static string FilterExecutables => Text.T("FileDialog_FilterExecutables") + "\0*.exe;*.com;*.bat;*.cmd;*.lnk;*.url\0";
    public static string FilterImages => Text.T("FileDialog_FilterImages") + "\0*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp;*.svg\0";
    public static string FilterAll => Text.T("FileDialog_FilterAll") + "\0*.*\0";

    public static string FilterExecutablesAndImages => Text.T("FileDialog_FilterExecutablesAndImages") + "\0*.exe;*.com;*.bat;*.cmd;*.lnk;*.url;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp;*.svg\0";

    public static string DefaultFilter => BuildFilter(FilterExecutables, FilterImages, FilterAll);

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OpenFileName ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    public static string? ShowOpenFileDialog(IntPtr hwnd, string title = "")
    {
        if (string.IsNullOrEmpty(title))
        {
            title = Text.T("FileDialog_SelectFile");
        }
        return ShowOpenFileDialog(hwnd, title, DefaultFilter);
    }

    public static string? ShowOpenFileDialog(IntPtr hwnd, string title, string filter)
    {
        var ofn = new OpenFileName
        {
            lStructSize = Marshal.SizeOf(typeof(OpenFileName)),
            hwndOwner = hwnd,
            lpstrTitle = title,
            lpstrFilter = filter,
            lpstrFile = new string('\0', 520),
            nMaxFile = 520,
            lpstrFileTitle = new string('\0', 128),
            nMaxFileTitle = 64,
            nFilterIndex = 1,
            Flags = 0x00080000 | 0x00001000
        };

        try
        {
            if (GetOpenFileNameW(ref ofn))
                return ofn.lpstrFile.TrimEnd('\0');
        }
        catch (Exception ex) { LogService.Write("App", "ShowOpenFileDialog failed", ex); }
        return null;
    }

    public static string BuildFilter(params string[] parts)
    {
        return string.Concat(parts) + "\0";
    }
}

public static class WindowActivator
{
    private const uint SW_RESTORE = 9;
    private const uint SW_SHOW = 5;
    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public static void AllowAnyForegroundWindow()
    {
        try
        {
            AllowSetForegroundWindow(ASFW_ANY);
        }
        catch (Exception ex) { LogService.Write("App", "WindowActivator AllowAnyForegroundWindow failed", ex); }
    }

    public static void Activate(IntPtr hWnd)
    {
        using (LogService.StartOperation("App", "WindowActivator_Activate"))
        {
            try
            {
                if (hWnd == IntPtr.Zero)
                {
                    LogService.Write("App", "WindowActivator Activate skipped hwnd=0");
                    return;
                }

                if (IsIconic(hWnd))
                    ShowWindow(hWnd, (int)SW_RESTORE);
                else
                    ShowWindow(hWnd, (int)SW_SHOW);

                bool ok = SetForegroundWindow(hWnd);
                LogService.Write("App", $"WindowActivator Activate hwnd={hWnd} setForeground={ok}");
            }
            catch (Exception ex)
            {
                LogService.Write("App", "WindowActivator Activate failed", ex);
            }
        }
    }
}

public static class SystemGuard
{
    private const uint MB_ICONERROR = 0x10;
    private const uint MinBuild = 26100;

    [StructLayout(LayoutKind.Sequential)]
    private struct OsVersionInfoEx
    {
        public uint dwOSVersionInfoSize;
        public uint dwMajorVersion;
        public uint dwMinorVersion;
        public uint dwBuildNumber;
        public uint dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion(ref OsVersionInfoEx lpVersionInformation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    public static void EnsureSupported()
    {
        try
        {
            var info = new OsVersionInfoEx { dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(OsVersionInfoEx)) };
            if (RtlGetVersion(ref info) == 0 && info.dwBuildNumber >= MinBuild)
                return;

            MessageBoxW(IntPtr.Zero,
                "You are running an unsupported version of Windows. EricGameLauncher requires Windows 11 24H2 (Build 26100) or later.",
                "EricGameLauncher",
                MB_ICONERROR);
        }
        catch { }
        Environment.Exit(0);
    }
}