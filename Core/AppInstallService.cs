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

                if (File.Exists(DesktopShortcutPath)) File.Delete(DesktopShortcutPath);
                if (File.Exists(StartMenuShortcutPath)) File.Delete(StartMenuShortcutPath);

                ShortcutResolver.CreateShortcut(exePath, DesktopShortcutPath, Description);

                string startMenuDir = Path.GetDirectoryName(StartMenuShortcutPath) ?? "";
                if (!Directory.Exists(startMenuDir)) Directory.CreateDirectory(startMenuDir);
                ShortcutResolver.CreateShortcut(exePath, StartMenuShortcutPath, Description);

                LogService.Write("App", "Install Complete");
            }
            catch (Exception ex) { LogService.Write("App", "AppInstallService.Install failed", ex); throw; }
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

                LogService.Write("App", "Uninstall Complete");
            }
            catch (Exception ex) { LogService.Write("App", "AppInstallService.Uninstall failed", ex); throw; }
        }
    }
}
