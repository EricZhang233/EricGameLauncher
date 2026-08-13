namespace EricGameLauncher;

public static class LaunchService
{
    public static (string path, bool admin) GetLaunchTarget(AppItem item, bool useAlt = false)
    {
        if (useAlt && !string.IsNullOrEmpty(item.AlternativeLaunchCommand))
            return (item.AlternativeLaunchCommand, item.IsAltAdmin);
        return (item.ExePath ?? "", item.IsAdmin);
    }

    public static (string path, bool admin)? GetAlongsideTarget(AppItem item)
    {
        if (item.RunAlongside && !string.IsNullOrEmpty(item.AlongsideCommand))
            return (item.AlongsideCommand, item.IsAlongsideAdmin);
        return null;
    }

    public static (string path, bool admin)? GetManagerTarget(AppItem item)
    {
        var mgrPath = item.RuntimeManagerPath ?? item.MgrPath;
        if (!string.IsNullOrEmpty(mgrPath))
            return (mgrPath, item.IsMgrAdmin);
        return null;
    }

    public static void Launch(AppItem item, bool useAlt = false)
    {
        using (LogService.StartOperation("Launch", $"Launch {item.Title}"))
        {
            var (path, admin) = GetLaunchTarget(item, useAlt);
            if (string.IsNullOrEmpty(path)) return;

            ProcessRunner.Run(path, admin);

            var alongside = GetAlongsideTarget(item);
            if (alongside.HasValue)
                ProcessRunner.Run(alongside.Value.path, alongside.Value.admin);
        }
    }

    public static void LaunchManager(AppItem item)
    {
        using (LogService.StartOperation("Launch", $"LaunchManager {item.Title}"))
        {
            var mgr = GetManagerTarget(item);
            if (mgr.HasValue)
                ProcessRunner.Run(mgr.Value.path, mgr.Value.admin);
        }
    }

    public static void LaunchCustomCommand(CustomMenuItem ci)
    {
        if (!string.IsNullOrEmpty(ci.Command))
            ProcessRunner.Run(ci.Command, ci.IsAdmin);
    }
}
