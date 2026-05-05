using System;
using System.IO;

namespace EricGameLauncher;

internal static class LogService
{
    private static readonly object _lock = new();
    private static int _startupRefCount = 0;

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

    internal static void Write(string tag, string message)
    {
        try
        {
            if (string.IsNullOrEmpty(tag)) tag = "LOG";
            if (message == null) message = string.Empty;
            Directory.CreateDirectory(LogDir);
            string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {tag} {message}{Environment.NewLine}";
            string fileName = ResolveLogFile(tag);
            string modulePath = Path.Combine(LogDir, fileName);
            WriteLine(modulePath, line);
            if (IsStartupActive && !string.Equals(fileName, "startup.log", StringComparison.OrdinalIgnoreCase))
            {
                string startupPath = Path.Combine(LogDir, "startup.log");
                WriteLine(startupPath, line);
            }
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
