using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace EricGameLauncher;

public static class QuickStartService
{
    public const string BackgroundArgument = "-quickstart";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "EricGameLauncher";
    private const string MainExecutableName = "EricGameLauncher.exe";

    public static bool IsActive => ConfigService.QuickStart;

    public static bool IsBackgroundStart => StartupArgs.IsQuickStart && IsActive;

    public static string ResolveMainExecutable()
    {
        try
        {
            string? current = Environment.ProcessPath;
            string dir = Path.GetDirectoryName(current ?? "") ?? "";
            if (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, MainExecutableName);
                if (File.Exists(candidate)) return candidate;
            }
            if (!string.IsNullOrEmpty(current)) return current;
        }
        catch (Exception ex) { LogService.Write("QuickStart", "ResolveMainExecutable failed", ex); }

        try { return Process.GetCurrentProcess().MainModule?.FileName ?? ""; } catch { return ""; }
    }

    public static string BuildAutoStartCommand()
    {
        string exe = ResolveMainExecutable();
        return string.IsNullOrEmpty(exe) ? "" : $"\"{exe}\" {BackgroundArgument}";
    }

    public static string? GetRegisteredCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(RunValueName) as string;
        }
        catch (Exception ex) { LogService.Write("QuickStart", "GetRegisteredCommand failed", ex); return null; }
    }

    public static bool IsAutoStartRegistered()
    {
        string? command = GetRegisteredCommand();
        return !string.IsNullOrEmpty(command);
    }

    public static bool Register()
    {
        using (LogService.StartOperation("QuickStart", "Register"))
        {
            try
            {
                string exe = ResolveMainExecutable();
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    LogService.Write("QuickStart", $"Register aborted, main executable not found path={exe}", null, null, LogService.LogLevel.Error);
                    return false;
                }

                string command = BuildAutoStartCommand();
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
                if (key == null)
                {
                    LogService.Write("QuickStart", "Register aborted, run key unavailable", null, null, LogService.LogLevel.Error);
                    return false;
                }

                key.SetValue(RunValueName, command, RegistryValueKind.String);
                LogService.Write("QuickStart", $"Register written value={RunValueName} command={command}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write("QuickStart", "Register failed", ex, null, LogService.LogLevel.Error);
                return false;
            }
        }
    }

    public static bool Unregister()
    {
        using (LogService.StartOperation("QuickStart", "Unregister"))
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                if (key == null)
                {
                    LogService.Write("QuickStart", "Unregister skipped, run key unavailable");
                    return true;
                }

                if (key.GetValue(RunValueName) != null)
                {
                    key.DeleteValue(RunValueName, false);
                    LogService.Write("QuickStart", $"Unregister removed value={RunValueName}");
                }
                else
                {
                    LogService.Write("QuickStart", "Unregister skipped, value absent");
                }
                return true;
            }
            catch (Exception ex)
            {
                LogService.Write("QuickStart", "Unregister failed", ex, null, LogService.LogLevel.Error);
                return false;
            }
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        using (LogService.StartOperation("QuickStart", "SetEnabled"))
        {
            bool applied = enabled ? Register() : Unregister();
            if (!applied)
            {
                LogService.Write("QuickStart", $"SetEnabled aborted enabled={enabled} (auto start registration not applied)", null, null, LogService.LogLevel.Error);
                return false;
            }

            ConfigService.QuickStart = enabled;
            ConfigService.SaveAll();
            LogService.Write("QuickStart", $"SetEnabled applied enabled={enabled} command={BuildAutoStartCommand()}");
            return true;
        }
    }

    public static void SyncAutoStart()
    {
        using (LogService.StartOperation("QuickStart", "SyncAutoStart"))
        {
            try
            {
                if (IsActive)
                {
                    string command = BuildAutoStartCommand();
                    string? current = GetRegisteredCommand();
                    if (!string.Equals(current, command, StringComparison.OrdinalIgnoreCase))
                    {
                        Register();
                    }
                    return;
                }

                if (IsAutoStartRegistered())
                {
                    Unregister();
                }
            }
            catch (Exception ex) { LogService.Write("QuickStart", "SyncAutoStart failed", ex); }
        }
    }

    public static void ClearAutoStartForUninstall()
    {
        Unregister();
        if (ConfigService.QuickStart)
        {
            ConfigService.QuickStart = false;
            ConfigService.SaveAll();
            LogService.Write("QuickStart", "Uninstall cleared quick start setting");
        }
    }
}
