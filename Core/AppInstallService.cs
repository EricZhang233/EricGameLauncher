using System.Diagnostics;
using System.IO;

namespace EricGameLauncher;

public static class AppInstallService
{
    private const string AppName = "EricGameLauncher";
    private const string Description = "Eric Game Launcher";

    private static string GetMainExePath()
    {
        var currentDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName) ?? ".";
        return Path.Combine(currentDir, $"{AppName}.exe");
    }

    private static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

    private static string StartMenuShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs", $"{AppName}.lnk");

    public static void Install()
    {
        using (LogService.StartOperation("App", "Install"))
        {
            try
            {
                LogService.Write("App", "Install Start");
                string exePath = GetMainExePath();

                InstallShortcuts(exePath, [DesktopShortcutPath, StartMenuShortcutPath]);

                LogService.Write("App", "Install Complete");
                JumpListService.UpdateShortcutIds();
            }
            catch (Exception ex) { LogService.Write("App", "AppInstallService.Install failed", ex); throw; }
        }
    }

    internal static void InstallShortcuts(string executable, IReadOnlyList<string> destinations, Action<string>? checkpoint = null)
    {
        var staged = new Dictionary<string, string>();
        var backups = new Dictionary<string, string>();
        var installed = new List<string>();
        bool recovered = false;
        try
        {
            foreach (string target in destinations)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".lnk";
                staged.Add(target, temporary);
                checkpoint?.Invoke("create:" + target);
                ShortcutResolver.CreateShortcut(executable, temporary, Description);
                if (File.Exists(target))
                {
                    string backup = temporary + ".bak";
                    File.Copy(target, backup);
                    backups.Add(target, backup);
                }
            }
            foreach (var entry in staged)
            {
                checkpoint?.Invoke("install:" + entry.Key);
                File.Move(entry.Value, entry.Key, true);
                installed.Add(entry.Key);
            }
            recovered = true;
        }
        catch
        {
            foreach (string target in installed.AsEnumerable().Reverse())
            {
                if (backups.TryGetValue(target, out string? backup)) File.Copy(backup, target, true);
                else File.Delete(target);
            }
            recovered = true;
            throw;
        }
        finally
        {
            foreach (string temporary in staged.Values) if (File.Exists(temporary)) File.Delete(temporary);
            if (recovered) foreach (string backup in backups.Values) if (File.Exists(backup)) File.Delete(backup);
        }
    }

    public static void Uninstall()
    {
        using (LogService.StartOperation("App", "Uninstall"))
        {
            try
            {
                LogService.Write("App", "Uninstall Start");

                if (File.Exists(DesktopShortcutPath)) File.Delete(DesktopShortcutPath);
                if (File.Exists(StartMenuShortcutPath)) File.Delete(StartMenuShortcutPath);

                QuickStartService.ClearAutoStartForUninstall();

                LogService.Write("App", "Uninstall Complete");
            }
            catch (Exception ex) { LogService.Write("App", "AppInstallService.Uninstall failed", ex); throw; }
        }
    }
}
