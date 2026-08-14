using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace EricGameLauncher;

public static class MigrationService
{
    public static async Task RunMigrationAndRestart()
    {
        using (LogService.StartOperation("Migration", "Run"))
        {
            LogService.Write("Migration", "Start");
            string configPath = ConfigService.ItemsFilePath;

            bool migrationFailed = false;
            string failureDetail = string.Empty;

            try
            {
                string tempDir = Path.Combine(ConfigService.SystemCachePath, "updater.cfgver");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                string cfgUpdaterPath = Path.Combine(tempDir, "updater.cfgver.exe");

                var assembly = Assembly.GetExecutingAssembly();
                string cfgPrefix = "EricGameLauncher.updater.cfgver.";
                foreach (var resName in assembly.GetManifestResourceNames())
                {
                    if (!resName.StartsWith(cfgPrefix)) continue;
                    string fileName = resName.Substring(cfgPrefix.Length);
                    string outputPath = Path.Combine(tempDir, fileName);
                    using var stream = assembly.GetManifestResourceStream(resName);
                    if (stream == null) continue;
                    using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fileStream);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = cfgUpdaterPath,
                    WorkingDirectory = tempDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add(configPath);
                var process = Process.Start(psi);
                if (process == null)
                {
                    migrationFailed = true;
                    failureDetail = "CfgUpdater process could not be started";
                }
                else
                {
                    await process.WaitForExitAsync();
                    int exitCode = process.ExitCode;
                    if (exitCode != 0)
                    {
                        migrationFailed = true;
                        failureDetail = $"CfgUpdater exited with code {exitCode}";
                    }
                }
            }
            catch (Exception ex)
            {
                migrationFailed = true;
                failureDetail = ex.ToString();
                LogService.Write("Migration", "CfgUpdater failed", ex, null, LogService.LogLevel.Error);
            }

            if (migrationFailed)
            {
                LogService.Write("Migration", $"CfgUpdater migration failed: {failureDetail}", null, null, LogService.LogLevel.Error);
                QuarantineOldConfig(configPath);
            }
            else
            {
                LogService.Write("Migration", "CfgUpdater migration completed");
            }

            try
            {
                string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(currentExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = currentExe,
                        WorkingDirectory = Path.GetDirectoryName(currentExe),
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex) { LogService.Write("Migration", "Restart failed", ex, null, LogService.LogLevel.Error); }

            LogService.Write("Migration", "Exit requested");
            Environment.Exit(0);
        }
    }

    private static void QuarantineOldConfig(string configPath)
    {
        try
        {
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return;
            string quarantinePath = $"{configPath}.failed.{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(configPath, quarantinePath);
            LogService.Write("Migration", $"Old config quarantined to {quarantinePath}");
        }
        catch (Exception ex)
        {
            LogService.Write("Migration", "Quarantine old config failed", ex, null, LogService.LogLevel.Error);
        }
    }
}
