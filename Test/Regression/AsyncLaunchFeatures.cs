using EricGameLauncher;

internal static class AsyncLaunchFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, string root, string fixture)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int caller = Environment.CurrentManagedThreadId;
        var pending = LaunchService.RunOnLaunchThreadAsync(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return (Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState());
        });
        try
        {
            check(entered.Wait(TimeSpan.FromSeconds(10)) && !pending.IsCompleted,
                "slow launch returns control while the shell operation is pending");
        }
        finally { release.Set(); }
        var worker = await pending;
        check(worker.Item1 != caller && worker.Item2 == ApartmentState.STA,
            "shell launch runs on a separate STA thread");

        bool propagated = false;
        try { await LaunchService.RunOnLaunchThreadAsync<bool>(() => throw new InvalidOperationException("launch fixture")); }
        catch (InvalidOperationException) { propagated = true; }
        check(propagated, "background launch exceptions reach the caller");

        string marker = Path.Combine(root, "async-launch");
        var item = ItemService.CreateItem("Async launch fixture", $"\"{fixture}\" \"{marker}\"");
        item.RunAlongside = true;
        item.AlongsideCommand = Path.Combine(root, "async-missing-alongside.exe");
        var result = await LaunchService.LaunchAsync(item);
        check(!result.Success && item.LaunchCount == 1,
            "async alongside failure preserves the successful main launch count");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        bool completed = false;
        while (DateTime.UtcNow < deadline && !completed)
        {
            try { completed = Directory.GetFiles(root, "async-launch.*").Any(path => File.ReadAllText(path) == "completed"); }
            catch (IOException) { }
            if (!completed) await Task.Delay(50);
        }
        check(completed, "async launch starts the target process");
        item.ExePath = Path.Combine(root, "async-missing-main.exe");
        result = await LaunchService.LaunchAsync(item);
        check(!result.Success && item.LaunchCount == 1, "async main failure does not increment the count");
    }
}
