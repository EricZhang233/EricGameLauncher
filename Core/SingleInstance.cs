using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EricGameLauncher;

internal static class SingleInstance
{
    private const uint ERROR_ALREADY_EXISTS = 183;
    private const string MutexName = "Local\\EricGameLauncher.SingleInstance.v1";
    private const string PipeName = "EricGameLauncher.SingleInstance.v1";
    private const string ActivateSignal = "ACTIVATE";

    private static IntPtr _handle = IntPtr.Zero;
    private static bool _isFirst = true;
    private static CancellationTokenSource? _cts;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    public static bool TryAcquire()
    {
        using (LogService.StartOperation("App", "SingleInstance_TryAcquire"))
        {
            try
            {
                if (_handle != IntPtr.Zero)
                    return _isFirst;

                _handle = CreateMutex(IntPtr.Zero, true, MutexName);
                int error = Marshal.GetLastWin32Error();
                if (_handle == IntPtr.Zero)
                {
                    LogService.Write("App", $"SingleInstance CreateMutex failed error={error}, proceeding as first instance");
                    _isFirst = true;
                }
                else
                {
                    _isFirst = error != ERROR_ALREADY_EXISTS;
                }
                LogService.Write("App", $"SingleInstance TryAcquire name={MutexName} first={_isFirst} error={error}");
                return _isFirst;
            }
            catch (Exception ex)
            {
                LogService.Write("App", "SingleInstance TryAcquire failed", ex);
                return true;
            }
        }
    }

    public static void StartServer(Action onActivate)
    {
        try
        {
            StopServer();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => ServerLoop(onActivate, token), token);
            LogService.Write("App", "SingleInstance StartServer started");
        }
        catch (Exception ex)
        {
            LogService.Write("App", "SingleInstance StartServer failed", ex);
        }
    }

    public static void StopServer()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch { }
    }

    public static bool NotifyRunningInstance()
    {
        using (LogService.StartOperation("App", "SingleInstance_Notify"))
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(1000);
                    byte[] data = Encoding.UTF8.GetBytes(ActivateSignal);
                    client.Write(data, 0, data.Length);
                    client.Flush();
                    LogService.Write("App", $"SingleInstance Notify sent signal attempt={attempt}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.Write("App", $"SingleInstance Notify attempt={attempt} failed", ex);
                }
            }
            return false;
        }
    }

    private static async Task ServerLoop(Action onActivate, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(token);
                var buffer = new byte[64];
                int read = await server.ReadAsync(buffer, 0, buffer.Length, token);
                if (read > 0)
                {
                    LogService.Write("App", "SingleInstance received activate signal");
                    onActivate();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                LogService.Write("App", "SingleInstance ServerLoop error", ex);
            }
        }
    }
}
