using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EricGameLauncher;

internal static class StartupArgs
{
    public static bool IsDebug { get; private set; }
    public static string? GitHubToken { get; private set; }
    private static string[] _rawArgs = Array.Empty<string>();

    public static void Parse()
    {
        try
        {
            _rawArgs = Environment.GetCommandLineArgs();
            foreach (var a in _rawArgs)
            {
                if (string.Equals(a, "-debug", StringComparison.OrdinalIgnoreCase))
                    IsDebug = true;
            }
        }
        catch { }
    }

    public static void Apply()
    {
        try
        {
            if (IsDebug)
                ConfigService.ApplyDebugMode(Directory.GetCurrentDirectory());
        }
        catch (Exception ex) { LogService.Write("Debug", "StartupArgs.Apply failed", ex); }
    }

    public static void LoadDebugConfig()
    {
        try
        {
            string path = Path.Combine(ConfigService.CurrentDataPath, "debug.yaml");
            if (!File.Exists(path)) return;
            var yaml = File.ReadAllText(path);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var cfg = deserializer.Deserialize<Dictionary<string, string>>(yaml);
            if (cfg != null && cfg.TryGetValue("githubToken", out var token) && !string.IsNullOrWhiteSpace(token))
            {
                GitHubToken = token.Trim();
                LogService.Write("Startup", $"LoadDebugConfig: githubToken loaded");
            }
        }
        catch (Exception ex) { LogService.Write("Startup", "LoadDebugConfig failed", ex); }
    }

    public static void LogEnvironment()
    {
        try
        {
            var argsPart = string.Join(" ", _rawArgs.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            LogService.Write("Startup", $"CommandLine={Environment.ProcessPath} {argsPart}".TrimEnd());
            LogService.Write("Startup", $"WorkDir={Directory.GetCurrentDirectory()}");
            LogService.Write("Startup", $"Config={ConfigService.ConfigFilePath} (exists={File.Exists(ConfigService.ConfigFilePath)})");
            LogService.Write("Startup", $"DataPath={ConfigService.CurrentDataPath}");
            LogService.Write("Startup", $"CachePath={ConfigService.SystemCachePath}");
        }
        catch { }
    }
}

internal static class DebugPaths
{
    public static bool IsDebug() => StartupArgs.IsDebug;
    public static void ApplyIfDebug() => StartupArgs.Apply();
    public static string DebugBaseDirectory()
    {
        try { return Directory.GetCurrentDirectory(); } catch { return "."; }
    }
}
