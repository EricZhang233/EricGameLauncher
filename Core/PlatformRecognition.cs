namespace EricGameLauncher;

public static class PlatformRecognition
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "EpicGamesLauncher.exe", "XboxPcApp.exe", "WeGame.exe", "EADesktop.exe",
        "Battle.net.exe", "GalaxyClient.exe", "UbisoftConnect.exe", "RiotClientServices.exe",
        "Heroic.exe", "Playnite.DesktopApp.exe", "Playnite.FullscreenApp.exe"
    };

    public static bool IsPlatform(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        try
        {
            if (command.StartsWith("shell:AppsFolder\\Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase)) return true;
            var (path, args) = ProcessRunner.SplitPath(Environment.ExpandEnvironmentVariables(command));
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                command = ShortcutResolver.GetLnkTarget(path);
                if (string.IsNullOrEmpty(command)) return false;
                (path, args) = ProcessRunner.SplitPath(command);
            }
            if (args.Contains("--launch-product", StringComparison.OrdinalIgnoreCase) || args.Contains("-applaunch", StringComparison.OrdinalIgnoreCase) || args.Contains("/gameId", StringComparison.OrdinalIgnoreCase)) return false;
            if (Names.Contains(Path.GetFileName(path))) return true;
            path = Path.GetFullPath(path).Replace('/', '\\');
            string name = Path.GetFileName(path);
            if (name.Equals("launcher.exe", StringComparison.OrdinalIgnoreCase))
                return new[] { "\\Rockstar Games\\Launcher\\", "\\HoYoPlay\\", "\\米哈游启动器\\" }.Any(x => path.Contains(x, StringComparison.OrdinalIgnoreCase));
            return (name.Equals("bootstrapper.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("client.exe", StringComparison.OrdinalIgnoreCase)) && path.Contains("\\Google\\Play Games\\", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) { LogService.Write("Platform", "Platform recognition failed", ex); return false; }
    }
}
