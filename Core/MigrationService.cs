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
                if (process != null) await process.WaitForExitAsync();
            }
            catch (Exception ex) { LogService.Write("Migration", "CfgUpdater failed", ex); }

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
            catch (Exception ex) { LogService.Write("Migration", "Restart failed", ex); }

            LogService.Write("Migration", "Exit requested");
            Environment.Exit(0);
        }
    }
}
