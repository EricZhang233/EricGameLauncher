using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Principal;

using System.Runtime.Versioning;

namespace updater.main
{
    class Program
    {
        private static readonly object _logLock = new();

        private static void Log(string message)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher", "log");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "updater.main.log");
                string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z UpdaterMain {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch { }
        }

        [SupportedOSPlatform("windows")]
        static async Task Main(string[] args)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"Start argsCount={args.Length}");
            Console.Title = "Eric Game Launcher - MainUpdater";
            Console.WriteLine("========================================");
            Console.WriteLine("    Eric Game Launcher Update System    ");
            Console.WriteLine("========================================");
            Console.WriteLine();

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: updater.main.exe <install_dir> <download_url>");
                Log("InvalidArgs");
                await Task.Delay(3000);
                return;
            }

            string installDir = args[0];
            string downloadUrl = args[1];
            Log($"Args installDir={installDir}");
            string cacheDir = Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher");
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
            string tempZip = Path.Combine(cacheDir, $"update_{Guid.NewGuid():N}.zip");

            if (!HasWriteAccess(installDir))
            {
                if (!IsAdministrator())
                {
                    Console.WriteLine("      Target directory is protected. Requesting administrator privileges...");
                    Log("ElevationRequired");
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = Process.GetCurrentProcess().MainModule?.FileName,
                            Arguments = string.Join(" ", args.Select(a => $"\"{a.Replace("\"", "\\\"")}\"")),
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        Log("ElevationRelaunch");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Elevation failed: {ex.Message}");
                        Log($"ElevationFailed {ex.Message}");
                        await Task.Delay(5000);
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Target directory is read-only despite administrator privileges.");
                    Log("ElevationNoWriteAccess");
                    await Task.Delay(5000);
                    return;
                }
            }

            try
            {
                var downloadTaskSw = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"[1/4] Downloading update package...");
                Log($"Download Start url={downloadUrl}");
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");

                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReportProgress = totalBytes != -1;

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            var totalRead = 0L;
                            var lastReportTime = DateTime.Now;
                            var startTime = DateTime.Now;
                            int read;

                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;

                                var now = DateTime.Now;
                                var elapsedSinceReport = (now - lastReportTime).TotalMilliseconds;

                                if (elapsedSinceReport > 200 || totalRead == totalBytes)
                                {
                                    lastReportTime = now;
                                    double speed = (totalRead / 1024.0 / 1024.0) / (now - startTime).TotalSeconds;

                                    string progressText;
                                    if (canReportProgress)
                                    {
                                        double percent = (double)totalRead / totalBytes * 100;
                                        progressText = $"\r      Progress: {percent:F1}% ({totalRead / 1024.0 / 1024.0:F2} / {totalBytes / 1024.0 / 1024.0:F2} MB) | Speed: {speed:F2} MB/s    ";
                                    }
                                    else
                                    {
                                        progressText = $"\r      Downloaded: {totalRead / 1024.0 / 1024.0:F2} MB | Speed: {speed:F2} MB/s    ";
                                    }
                                    Console.Write(progressText);
                                }
                            }
                        }
                    }
                }
                Console.WriteLine("\n      Download completed.");
                Log($"Download Complete duration={downloadTaskSw.ElapsedMilliseconds}ms size={new FileInfo(tempZip).Length}");

                Console.WriteLine($"[2/4] Closing Eric Game Launcher...");
                Log("CloseLauncher Start");
                var processes = Process.GetProcessesByName("EricGameLauncher");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(); } catch { }
                }
                await Task.Delay(1000);
                Log("CloseLauncher End");

                Console.WriteLine($"[3/4] Applying updates...");
                Log("ApplyUpdates Start");
                string stagingDir = Path.Combine(installDir, "._update_staging");
                string backupDir = Path.Combine(installDir, "._update_backup");

                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                Directory.CreateDirectory(stagingDir);

                try
                {
                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string stagingPath = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                            if (!stagingPath.StartsWith(stagingDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

                            if (entry.FullName.ToLower().StartsWith("data/")) continue;

                            string destDir = Path.GetDirectoryName(stagingPath)!;
                            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                            entry.ExtractToFile(stagingPath, true);
                        }
                    }

                    Directory.CreateDirectory(backupDir);
                    var currentFiles = Directory.GetFiles(installDir, "*", SearchOption.AllDirectories)
                        .Where(f => !f.StartsWith(stagingDir) && !f.StartsWith(backupDir) && !f.ToLower().Contains("\\data\\") && !f.ToLower().EndsWith(".update_staging") && !f.ToLower().EndsWith(".update_backup"));

                    foreach (var file in currentFiles)
                    {
                        string relative = Path.GetRelativePath(installDir, file);
                        string backupPath = Path.Combine(backupDir, relative);
                        string bDir = Path.GetDirectoryName(backupPath)!;
                        if (!Directory.Exists(bDir)) Directory.CreateDirectory(bDir);
                        File.Move(file, backupPath, true);
                    }

                    var stagedFiles = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
                    foreach (var file in stagedFiles)
                    {
                        string relative = Path.GetRelativePath(stagingDir, file);
                        string finalPath = Path.Combine(installDir, relative);
                        string fDir = Path.GetDirectoryName(finalPath)!;
                        if (!Directory.Exists(fDir)) Directory.CreateDirectory(fDir);
                        File.Move(file, finalPath, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error during update application: " + ex.Message);
                    Console.WriteLine("Attempting rollback...");
                    try
                    {
                        if (Directory.Exists(backupDir))
                        {
                            var backupFiles = Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories);
                            foreach (var file in backupFiles)
                            {
                                string relative = Path.GetRelativePath(backupDir, file);
                                string finalPath = Path.Combine(installDir, relative);
                                if (File.Exists(finalPath)) File.Delete(finalPath);
                                File.Move(file, finalPath, true);
                            }
                        }
                        Console.WriteLine("Rollback successful. The launcher was not corrupted.");
                    }
                    catch (Exception rbEx)
                    {
                        Console.WriteLine("FATAL: Rollback failed! " + rbEx.Message);
                    }
                    throw;
                }
                finally
                {
                    try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                    try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }
                }

                Console.WriteLine($"[4/4] Restarting application...");
                Log("Restart Start");
                string exePath = Path.Combine(installDir, "EricGameLauncher.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    });
                }
                Log("Restart End");

                Console.WriteLine();
                Console.WriteLine("Update successful! Closing updater...");
                Log("Success");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine("Please try manual update or check network connection.");
                Console.WriteLine("Press any key to exit...");
                Log($"Failure {ex.Message}");
                Console.ReadKey();
            }
            finally
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
                Log($"End duration={sw.ElapsedMilliseconds}ms");
            }
        }

        private static bool HasWriteAccess(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                string testPath = Path.Combine(dir, "access_test_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream fs = File.Create(testPath)) { }
                File.Delete(testPath);
                return true;
            }
            catch { return false; }
        }

        [SupportedOSPlatform("windows")]
        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }
    }
}

