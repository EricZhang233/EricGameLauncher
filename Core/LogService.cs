using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace EricGameLauncher;

internal static class LogService
{
    private static readonly object _lock = new();
    private static int _startupRefCount = 0;
    private const long MaxLogFileBytes = 5 * 1024 * 1024;

    private static string LogDir => Path.Combine(ConfigService.SystemCachePath, "log");
    private static string LogFilePath => Path.Combine(LogDir, "app.log");

    internal static void StartupEnter()
    {
        lock (_lock) { _startupRefCount++; }
    }

    internal static void StartupExit()
    {
        lock (_lock)
        {
            _startupRefCount--;
            if (_startupRefCount < 0) _startupRefCount = 0;
        }
    }

    internal enum LogLevel { Debug, Info, Warn, Error }

    private sealed class LogEntry
    {
        public DateTime Timestamp;
        public string? Tag;
        public string? Message;
        public Exception? Exception;
        public string? OperationId;
        public LogLevel Level;
        public string? Caller;
        public string? CallerFile;
        public int CallerLine;
    }

    private static readonly ConcurrentQueue<LogEntry> _entryPool = new();

    private static LogEntry RentEntry()
    {
        if (_entryPool.TryDequeue(out var e)) return e;
        return new LogEntry();
    }

    private static void ReturnEntry(LogEntry e)
    {
        e.Timestamp = default;
        e.Tag = null;
        e.Message = null;
        e.Exception = null;
        e.OperationId = null;
        e.Level = default;
        e.Caller = null;
        e.CallerFile = null;
        e.CallerLine = 0;
        _entryPool.Enqueue(e);
    }

    private static readonly Channel<LogEntry> _channel;
    private static readonly CancellationTokenSource _cts = new();
    private static readonly Task _writerTask;

    static LogService()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateUnbounded<LogEntry>(options);
        _writerTask = Task.Run(WriteLoop);
    }

    internal static void Write(
        string tag,
        string message,
        Exception? ex = null,
        string? operationId = null,
        LogLevel level = LogLevel.Info,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(tag)) tag = "LOG";
            if (message == null) message = string.Empty;

            string callerFileName = string.IsNullOrEmpty(callerFile) ? string.Empty : Path.GetFileName(callerFile);
            var entry = RentEntry();
            entry.Timestamp = DateTime.UtcNow;
            entry.Tag = tag;
            entry.Message = message;
            entry.Exception = ex;
            entry.OperationId = operationId;
            entry.Level = level;
            entry.Caller = caller;
            entry.CallerFile = callerFileName;
            entry.CallerLine = callerLine;
            _channel.Writer.TryWrite(entry);
        }
        catch { }
    }

    internal sealed class OperationTimer : IDisposable
    {
        private readonly string _tag;
        private readonly string _name;
        private readonly Stopwatch _sw;
        private readonly string _operationId;

        internal OperationTimer(string tag, string name, string? operationId = null)
        {
            _tag = string.IsNullOrEmpty(tag) ? "App" : tag;
            _name = name ?? string.Empty;
            _sw = Stopwatch.StartNew();
            _operationId = string.IsNullOrEmpty(operationId) ? Guid.NewGuid().ToString("N") : operationId!;
            Write(_tag, $"OperationStart {_name}", null, _operationId, LogLevel.Info);
        }

        public void Dispose()
        {
            _sw.Stop();
            Write(_tag, $"OperationEnd {_name} duration={_sw.ElapsedMilliseconds}ms", null, _operationId, LogLevel.Info);
        }
    }

    internal static OperationTimer StartOperation(string tag, string name, string? operationId = null)
    {
        return new OperationTimer(tag, name, operationId);
    }

    private static async Task WriteLoop()
    {
        try { Directory.CreateDirectory(LogDir); } catch { }

        StreamWriter? writer = null;

        try
        {
            while (await _channel.Reader.WaitToReadAsync(_cts.Token))
            {
                if (writer == null)
                {
                    RotateIfNeeded(LogFilePath);
                    writer = new StreamWriter(LogFilePath, append: true) { AutoFlush = false };
                }

                var sb = new StringBuilder(512);
                while (_channel.Reader.TryRead(out var entry))
                {
                    sb.Clear();
                    sb.Append('[');
                    sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    sb.Append("Z ");
                    sb.Append(entry.Tag);
                    sb.Append('/');
                    sb.Append(entry.Level.ToString().ToUpperInvariant());
                    sb.Append("] ");
                    sb.Append(entry.CallerFile);
                    sb.Append(':');
                    sb.Append(entry.CallerLine);
                    sb.Append('.');
                    sb.Append(entry.Caller);
                    if (!string.IsNullOrEmpty(entry.OperationId))
                    {
                        sb.Append(" op=");
                        sb.Append(entry.OperationId);
                    }
                    sb.Append(" | ");
                    sb.Append(entry.Message);
                    if (entry.Exception != null)
                    {
                        try
                        {
                            sb.Append(" Exception=");
                            sb.Append(entry.Exception.GetType().FullName);
                            sb.Append(':');
                            sb.Append(entry.Exception.Message);
                            sb.Append(" Stack=");
                            sb.Append(entry.Exception.StackTrace);
                        }
                        catch { }
                    }

                    await writer.WriteLineAsync(sb.ToString());
                    // return pooled entry
                    try { ReturnEntry(entry); } catch { }
                }

                await writer.FlushAsync();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (writer != null)
            {
                try { await writer.DisposeAsync(); } catch { }
            }
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var fi = new FileInfo(path);
            if (fi.Length <= MaxLogFileBytes) return;
            string archive = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Move(path, archive);
        }
        catch { }
    }

    internal static void FlushAndStop()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
        try { _writerTask.Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _writerTask.Dispose(); } catch { }
    }
}