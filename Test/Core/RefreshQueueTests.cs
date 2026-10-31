using EricGameLauncher;
using Xunit;

namespace EricGameLauncher.Tests;

public sealed class RefreshQueueTests
{
    [Fact]
    public async Task SlowCommitDoesNotBlockRequestsAndLatestStateWins()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var states = new List<(int State, bool Reload, ApartmentState Apartment)>();
        int latest = 1;
        var queue = new StaRefreshQueue(reload =>
        {
            int snapshot = Volatile.Read(ref latest);
            if (snapshot == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            states.Add((snapshot, reload, Thread.CurrentThread.GetApartmentState()));
        }, _ => { });
        queue.Request(true);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            await Task.Run(() =>
            {
                for (int i = 2; i <= 100; i++)
                {
                    Volatile.Write(ref latest, i);
                    queue.Request(i == 50);
                }
            }).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(queue.Idle.IsCompleted);
        }
        finally { release.Set(); }
        await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 100 }, states.Select(x => x.State));
        Assert.All(states, state => Assert.True(state.Reload));
        if (OperatingSystem.IsWindows()) Assert.All(states, state => Assert.Equal(ApartmentState.STA, state.Apartment));
        queue.Request(false);
        await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(states[^1].Reload);
    }

    [Fact]
    public async Task FailureIsReportedAndNextRequestRecovers()
    {
        int attempts = 0, failures = 0;
        var queue = new StaRefreshQueue(_ =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException();
        }, _ => Interlocked.Increment(ref failures));
        queue.Request(true);
        await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, failures);
        queue.Request(false);
        await queue.Idle.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, attempts);
    }
}
