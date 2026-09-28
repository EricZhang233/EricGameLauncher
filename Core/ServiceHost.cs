using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EricGameLauncher;

public static class ServiceHost
{
    private const uint ERROR_ALREADY_EXISTS = 183;
    private const uint SYNCHRONIZE = 0x00100000;
    private const string MutexName = "Local\\EricGameLauncher.ServiceHost.v1";
    private const string PipeName = "EricGameLauncher.ServiceHost.v1";
    private const string WakeSignal = "WAKE";
    private const string ExitSignal = "EXIT";

    private static IntPtr _handle = IntPtr.Zero;
    private static bool _isHost = false;
    private static CancellationTokenSource? _cts;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateMutex(IntPtr lpMutexAttributes, bool bInitialOwner, string lpName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenMutex(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    public static bool IsHost => _isHost;

    public static bool TryAcquireHost()
    {
        try
        {
            if (_handle != IntPtr.Zero)
                return _isHost;

            _handle = CreateMutex(IntPtr.Zero, true, MutexName);
            int error = Marshal.GetLastWin32Error();
            if (_handle == IntPtr.Zero)
            {
                LogService.Write("QuickStart", $"ServiceHost CreateMutex failed error={error}, proceeding as host");
                _isHost = true;
            }
            else
            {
                _isHost = error != ERROR_ALREADY_EXISTS;
            }
            LogService.Write("QuickStart", $"ServiceHost TryAcquireHost first={_isHost} error={error}");
            return _isHost;
        }
        catch (Exception ex)
        {
            LogService.Write("QuickStart", "ServiceHost TryAcquireHost failed", ex);
            return false;
        }
    }

    public static bool IsHostRunning()
    {
        try
        {
            IntPtr handle = OpenMutex(SYNCHRONIZE, false, MutexName);
            if (handle == IntPtr.Zero)
                return false;
            CloseHandle(handle);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Write("QuickStart", "ServiceHost IsHostRunning failed", ex);
            return false;
        }
    }

    public static bool NotifyWake()
    {
        using (LogService.StartOperation("QuickStart", "ServiceHost_Wake"))
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(500);
                    byte[] data = Encoding.UTF8.GetBytes(WakeSignal);
                    client.Write(data, 0, data.Length);
                    client.Flush();
                    LogService.Write("QuickStart", $"ServiceHost wake signal sent attempt={attempt}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.Write("QuickStart", $"ServiceHost wake attempt={attempt} failed", ex);
                }
            }
            return false;
        }
    }

    public static bool NotifyExit()
    {
        using (LogService.StartOperation("QuickStart", "ServiceHost_Exit"))
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(500);
                    byte[] data = Encoding.UTF8.GetBytes(ExitSignal);
                    client.Write(data, 0, data.Length);
                    client.Flush();
                    LogService.Write("QuickStart", $"ServiceHost exit signal sent attempt={attempt}");
                    return true;
                }
                catch (Exception ex)
                {
                    LogService.Write("QuickStart", $"ServiceHost exit attempt={attempt} failed", ex);
                }
            }
            return false;
        }
    }

    public static void StartServer(Action onWake, Action? onExit = null)
    {
        try
        {
            StopServer();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => ServerLoop(onWake, onExit, token), token);
            LogService.Write("QuickStart", "ServiceHost server started");
        }
        catch (Exception ex)
        {
            LogService.Write("QuickStart", "ServiceHost StartServer failed", ex);
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

    private static async Task ServerLoop(Action onWake, Action? onExit, CancellationToken token)
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
                    string signal = Encoding.UTF8.GetString(buffer, 0, read).Trim();
                    if (string.Equals(signal, ExitSignal, StringComparison.OrdinalIgnoreCase))
                    {
                        LogService.Write("QuickStart", "ServiceHost exit signal received");
                        if (onExit != null) onExit();
                        continue;
                    }

                    LogService.Write("QuickStart", "ServiceHost wake signal received");
                    onWake();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                LogService.Write("QuickStart", "ServiceHost server loop error", ex);
            }
        }
    }
}
