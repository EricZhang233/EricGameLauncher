using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace EricGameLauncher;

public static class MigrationService
{
    public static async Task RunMigrationAsync()
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
                psi.ArgumentList.Add("--target-version");
                psi.ArgumentList.Add(ConfigService.CurrentConfigVersion.ToString());
                using var process = Process.Start(psi);
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
                throw new DataFileException(configPath);
            }
            else
            {
                LogService.Write("Migration", "CfgUpdater migration completed");
            }

            ConfigService.Reload();
        }
    }

}
