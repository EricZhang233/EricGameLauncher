using System.Diagnostics;
using System.IO;

namespace EricGameLauncher;

public static class AppInstallService
{
    private const string AppName = "EricGameLauncher";
    private const string Description = "Eric Game Launcher";

    private static string GetMainExePath()
    {
        return Path.Combine(AppPaths.InstallDirectory, $"{AppName}.exe");
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
        Installation.ShortcutService.Install(executable, destinations, Description, step =>
        {
            LogService.Write("App", $"Shortcut installation {step}");
            checkpoint?.Invoke(step);
        });
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
