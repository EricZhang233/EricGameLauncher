namespace EricGameLauncher;

public static class LaunchService
{
    public static (string path, bool admin) GetLaunchTarget(AppItem item, bool useAlt = false)
    {
        if (useAlt && !string.IsNullOrEmpty(item.AlternativeLaunchCommand))
            return (item.AlternativeLaunchCommand, item.IsAltAdmin);
        return (item.ExePath ?? "", item.IsAdmin);
    }

    public static (string path, bool admin)? GetAlongsideTarget(AppItem item, bool requestAlongside = false)
    {
        if ((item.RunAlongside || requestAlongside) && !string.IsNullOrEmpty(item.AlongsideCommand))
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

    public static LaunchResult Launch(AppItem item, bool useAlt = false, bool requestAlongside = false)
    {
        using (LogService.StartOperation("Launch", $"Launch {item.Title}"))
        {
            var (path, admin) = GetLaunchTarget(item, useAlt);
            if (string.IsNullOrEmpty(path)) return new(false, Text.T("Launch_Failed"));

            var result = ProcessRunner.Run(path, admin);
            if (!result.Success) return result;
            try
            {
                item.LaunchCount = LaunchCountCache.Increment(item.Id);
            }
            catch (Exception ex) { LogService.Write("Launch", "Launch count persistence failed", ex); }

            var alongside = GetAlongsideTarget(item, requestAlongside);
            if (alongside.HasValue)
                return ProcessRunner.Run(alongside.Value.path, alongside.Value.admin);
            return result;
        }
    }

    public static LaunchResult LaunchManager(AppItem item)
    {
        using (LogService.StartOperation("Launch", $"LaunchManager {item.Title}"))
        {
            var mgr = GetManagerTarget(item);
            if (mgr.HasValue)
                return ProcessRunner.Run(mgr.Value.path, mgr.Value.admin);
            return new(false, Text.T("Launch_Failed"));
        }
    }

    public static void LaunchCustomCommand(CustomMenuItem ci)
    {
        if (!string.IsNullOrEmpty(ci.Command))
            ProcessRunner.Run(ci.Command, ci.IsAdmin);
    }
}
