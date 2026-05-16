using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace EricGameLauncher;

internal static class LogService
{
    private static readonly object _lock = new();
    private static int _startupRefCount = 0;
    private const long MaxLogFileBytes = 5 * 1024 * 1024; // 5 MB

    private static string LogDir => Path.Combine(ConfigService.SystemCachePath, "log");

    internal static void StartupEnter()
    {
        lock (_lock)
        {
            _startupRefCount++;
        }
    }

    internal static void StartupExit()
    {
        lock (_lock)
        {
            _startupRefCount--;
            if (_startupRefCount < 0) _startupRefCount = 0;
        }
    }

    private static bool IsStartupActive
    {
        get
        {
            lock (_lock)
            {
                return _startupRefCount > 0;
            }
        }
    }

    internal enum LogLevel { Debug, Info, Warn, Error }

    internal static void Write(string tag, string message, Exception? ex = null, string? operationId = null, LogLevel level = LogLevel.Info, [CallerMemberName] string caller = "", [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0)
    {
        try
        {
            if (string.IsNullOrEmpty(tag)) tag = "LOG";
            if (message == null) message = string.Empty;
            Directory.CreateDirectory(LogDir);
            string callerFileName = string.IsNullOrEmpty(callerFile) ? string.Empty : Path.GetFileName(callerFile);
            string exText = string.Empty;
            if (ex != null)
            {
                try
                {
                    exText = $" Exception={ex.GetType().FullName}:{ex.Message} Stack={ex.StackTrace}";
                }
                catch { }
            }
            string meta = $"{callerFileName}:{callerLine}.{caller}";
            string lvl = level.ToString().ToUpperInvariant();
            string op = string.IsNullOrEmpty(operationId) ? string.Empty : $" op={operationId}";
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {tag}/{lvl}] {meta}{op} | {message}{exText}{Environment.NewLine}";
            string fileName;
            if (string.Equals(callerFileName, "LogService.cs", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "logmgr.log";
            }
            else
            {
                fileName = ResolveLogFile(tag);
            }
            string modulePath = Path.Combine(LogDir, fileName);
            RotateIfNeeded(modulePath);
            WriteLine(modulePath, line);
            if (IsStartupActive && !string.Equals(fileName, "startup.log", StringComparison.OrdinalIgnoreCase))
            {
                string startupPath = Path.Combine(LogDir, "startup.log");
                RotateIfNeeded(startupPath);
                WriteLine(startupPath, line);
            }
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

    private static string ResolveLogFile(string tag)
    {
        string t = tag.ToLowerInvariant();
        return t switch
        {
            "startup" => "startup.log",
            "update" => "update.log",
            "scan" => "scan.log",
            "network" => "network.log",
            "config" => "config.log",
            "ui" => "ui.log",
            "platform" => "platform.log",
            "item" => "item.log",
            "shortcut" => "shortcut.log",
            "shell" => "shell.log",
            "run" => "run.log",
            "i18n" => "i18n.log",
            // default application-wide log
            _ => "app.log"
        };
    }

    private static void WriteLine(string path, string line)
    {
        lock (_lock)
        {
            File.AppendAllText(path, line);
        }
    }
}
