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
        => CompleteLaunch(item, ExecuteLaunch(CreateRequest(item, useAlt, requestAlongside)));

    public static async Task<LaunchResult> LaunchAsync(AppItem item, bool useAlt = false, bool requestAlongside = false)
    {
        var request = CreateRequest(item, useAlt, requestAlongside);
        var outcome = await RunOnLaunchThreadAsync(() => ExecuteLaunch(request));
        return CompleteLaunch(item, outcome);
    }

    private sealed record LaunchRequest(string? Title, string Id, (string path, bool admin) Target,
        (string path, bool admin)? Alongside);

    private static LaunchRequest CreateRequest(AppItem item, bool useAlt, bool requestAlongside)
        => new(item.Title, item.Id, GetLaunchTarget(item, useAlt), GetAlongsideTarget(item, requestAlongside));

    private static LaunchResult CompleteLaunch(AppItem item, (LaunchResult Result, long? Count) outcome)
    {
        if (outcome.Count.HasValue) item.LaunchCount = outcome.Count.Value;
        return outcome.Result;
    }

    private static (LaunchResult Result, long? Count) ExecuteLaunch(LaunchRequest request)
    {
        using (LogService.StartOperation("Launch", $"Launch {request.Title}"))
        {
            var (path, admin) = request.Target;
            if (string.IsNullOrEmpty(path)) return (new(false, Text.T("Launch_Failed")), null);

            var result = ProcessRunner.Run(path, admin);
            if (!result.Success) return (result, null);
            long? count = null;
            try
            {
                count = LaunchCountCache.Increment(request.Id);
            }
            catch (Exception ex) { LogService.Write("Launch", "Launch count persistence failed", ex); }

            var alongside = request.Alongside;
            if (alongside.HasValue)
                return (ProcessRunner.Run(alongside.Value.path, alongside.Value.admin), count);
            return (result, count);
        }
    }

    public static Task<LaunchResult> LaunchManagerAsync(AppItem item)
    {
        var target = GetManagerTarget(item);
        return RunOnLaunchThreadAsync(() => LaunchManagerTarget(target));
    }

    public static LaunchResult LaunchManager(AppItem item)
    {
        using (LogService.StartOperation("Launch", $"LaunchManager {item.Title}"))
        {
            return LaunchManagerTarget(GetManagerTarget(item));
        }
    }

    private static LaunchResult LaunchManagerTarget((string path, bool admin)? target)
        => target.HasValue ? ProcessRunner.Run(target.Value.path, target.Value.admin) : new(false, Text.T("Launch_Failed"));

    public static Task LaunchCustomCommandAsync(CustomMenuItem ci)
    {
        var command = new CustomMenuItem { Command = ci.Command, IsAdmin = ci.IsAdmin };
        return RunOnLaunchThreadAsync(() => { LaunchCustomCommand(command); return true; });
    }

    internal static Task<T> RunOnLaunchThreadAsync<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                LogService.Write("Launch", "Background STA launch started");
                completion.TrySetResult(action());
            }
            catch (Exception ex)
            {
                LogService.Write("Launch", "Background launch failed", ex);
                completion.TrySetException(ex);
            }
        }) { IsBackground = true, Name = "Application launch" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    public static void LaunchCustomCommand(CustomMenuItem ci)
    {
        if (!string.IsNullOrEmpty(ci.Command))
            ProcessRunner.Run(ci.Command, ci.IsAdmin);
    }
}
