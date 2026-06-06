using System;
using System.IO;

namespace EricGameLauncher;

internal static class DebugPaths
{
    public static bool IsDebug()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var a in args)
            {
                if (string.Equals(a, "-debug", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { }
        return false;
    }

    public static string DebugBaseDirectory()
    {
        try { return Directory.GetCurrentDirectory(); } catch { return "."; }
    }

    public static void ApplyIfDebug()
    {
        try
        {
            if (!IsDebug()) return;
            var baseDir = DebugBaseDirectory();
            ConfigService.ApplyDebugMode(baseDir);
        }
        catch (Exception ex) { LogService.Write("Debug", "ApplyIfDebug failed", ex); }
    }
}
