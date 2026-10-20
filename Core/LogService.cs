using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace EricGameLauncher;

internal static class LogService
{
    internal enum LogLevel { Debug, Info, Warn, Error }
    private static readonly string Session = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N")[..6];
    private static string LogDir => Path.Combine(ConfigService.SystemCachePath, "log");
    private static readonly Channel<string> Entries = Channel.CreateBounded<string>(new BoundedChannelOptions(4096) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private static long Dropped;
    private static int Stopped;
    private static readonly object CriticalGate = new();
    private static readonly Task Writer = Task.Run(WriteLoop);

    static LogService() { AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushAndStop(); }
    internal static void StartupEnter() { }
    internal static void StartupExit() { }

    internal static void Write(string tag, string message, Exception? ex = null, string? operationId = null,
        LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        if (Volatile.Read(ref Stopped) != 0) return;
        string exception = ex == null ? "" : $" Exception={ex.GetType().FullName}:{ex.Message} Stack={ex.StackTrace}";
        string line = SensitiveText.Redact($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {tag}/{level.ToString().ToUpperInvariant()}] {Path.GetFileName(callerFile)}:{callerLine}.{caller}{(operationId == null ? "" : " op=" + operationId)} | {message}{exception}");
        if (!Entries.Writer.TryWrite(line))
        {
            if (level >= LogLevel.Warn)
                try { lock (CriticalGate) { Directory.CreateDirectory(LogDir); File.AppendAllText(Path.Combine(LogDir, $"app-{Session}-critical.log"), line + Environment.NewLine); } } catch { }
            else Interlocked.Increment(ref Dropped);
        }
    }

    internal sealed class OperationTimer : IDisposable
    {
        private readonly string Tag;
        private readonly string Name;
        private readonly string Id;
        private readonly Stopwatch Timer = Stopwatch.StartNew();
        internal OperationTimer(string tag, string name, string? id)
        { Tag = tag; Name = name; Id = id ?? Guid.NewGuid().ToString("N"); Write(Tag, $"OperationStart {Name}", operationId: Id); }
        public void Dispose() => Write(Tag, $"OperationEnd {Name} duration={Timer.ElapsedMilliseconds}ms", operationId: Id);
    }

    internal static OperationTimer StartOperation(string tag, string name, string? operationId = null) => new(tag, name, operationId);

    private static async Task WriteLoop()
    {
        StreamWriter? output = null;
        try
        {
            Directory.CreateDirectory(LogDir);
            var retained = new DirectoryInfo(LogDir).GetFiles("app-*.log").OrderByDescending(x => x.LastWriteTimeUtc).ToArray();
            long retainedBytes = 0;
            foreach (var file in retained)
            {
                retainedBytes += file.Length;
                if (retainedBytes > 100L * 1024 * 1024 || DateTime.UtcNow - file.LastWriteTimeUtc > TimeSpan.FromDays(30))
                    try { file.Delete(); } catch { }
            }
            int part = 0;
            output = Open(part);
            while (await Entries.Reader.WaitToReadAsync())
            {
                int batch = 0;
                while (batch++ < 256 && Entries.Reader.TryRead(out var line)) await output.WriteLineAsync(line);
                long dropped = Interlocked.Exchange(ref Dropped, 0);
                if (dropped > 0) await output.WriteLineAsync($"[{DateTime.UtcNow:O} Log/WARN] | Dropped low-priority log entries count={dropped}");
                await output.FlushAsync();
                if (output.BaseStream.Length >= 5 * 1024 * 1024)
                { await output.DisposeAsync(); output = Open(++part); }
            }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ericgamelauncher-log-error.txt"), SensitiveText.Redact(ex.Message) + Environment.NewLine); } catch { }
        }
        finally { if (output != null) await output.DisposeAsync(); }
    }

    private static StreamWriter Open(int part) => new(new FileStream(Path.Combine(LogDir, $"app-{Session}-{part}.log"), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false), 16384);

    internal static void FlushAndStop()
    {
        if (Interlocked.Exchange(ref Stopped, 1) != 0) return;
        Entries.Writer.TryComplete();
        try { Writer.Wait(TimeSpan.FromSeconds(5)); } catch { }
    }
}
