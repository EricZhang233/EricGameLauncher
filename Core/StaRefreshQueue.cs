namespace EricGameLauncher;

internal sealed class StaRefreshQueue(Action<bool> refresh, Action<Exception> reportFailure)
{
    private readonly object _gate = new();
    private bool _pending;
    private bool _reload;
    private bool _running;
    private TaskCompletionSource<bool> _idle = CompletedIdle();

    private static TaskCompletionSource<bool> CompletedIdle()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult(true);
        return completion;
    }

    public Task Idle { get { lock (_gate) return _idle.Task; } }

    public void Request(bool reload)
    {
        lock (_gate)
        {
            _pending = true;
            _reload |= reload;
            if (_running) return;
            _running = true;
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var worker = new Thread(Process) { IsBackground = true, Name = "JumpList refresh" };
                if (OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
            }
            catch
            {
                _running = false;
                _idle.TrySetResult(true);
                throw;
            }
        }
    }

    private void Process()
    {
        while (true)
        {
            Thread.Sleep(100);
            bool reload;
            lock (_gate)
            {
                if (!_pending)
                {
                    _running = false;
                    _idle.TrySetResult(true);
                    return;
                }
                reload = _reload;
                _reload = false;
                _pending = false;
            }
            try { refresh(reload); }
            catch (Exception ex) { reportFailure(ex); }
        }
    }
}
