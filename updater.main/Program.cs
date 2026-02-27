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
        [SupportedOSPlatform("windows")]
        static async Task Main(string[] args)
        {
            Console.Title = "Eric Game Launcher - MainUpdater";
            Console.WriteLine("========================================");
            Console.WriteLine("    Eric Game Launcher Update System    ");
            Console.WriteLine("========================================");
            Console.WriteLine();

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: updater.main.exe <install_dir> <download_url>");
                await Task.Delay(3000);
                return;
            }

            string installDir = args[0];
            string downloadUrl = args[1];
            string tempZip = Path.Combine(Path.GetTempPath(), $"update_{Guid.NewGuid():N}.zip");

            // 1. Check for Write Access and Self-Elevate if needed
            if (!HasWriteAccess(installDir))
            {
                if (!IsAdministrator())
                {
                    Console.WriteLine("      Target directory is protected. Requesting administrator privileges...");
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo
                        {
                            FileName = Process.GetCurrentProcess().MainModule?.FileName,
                            // O3: escape any embedded double-quotes in each argument to prevent
                            // command-line parsing errors when paths contain quote characters.
                            Arguments = string.Join(" ", args.Select(a => $"\"{a.Replace("\"", "\\\"")}\"")),
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        return; // Exit current instance
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Elevation failed: {ex.Message}");
                        await Task.Delay(5000);
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Target directory is read-only despite administrator privileges.");
                    await Task.Delay(5000);
                    return;
                }
            }

            try
            {
                // Step 1: Download with Progress
                Console.WriteLine($"[1/4] Downloading update package...");
                // L2: set a generous timeout so the download can never hang indefinitely
                // on a broken connection. 30 minutes covers even very large packages on
                // a slow connection while still ensuring eventual cleanup.
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

                                // Update every 200ms or at completion
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

                // Step 2: Kill existing processes
                Console.WriteLine($"[2/4] Closing Eric Game Launcher...");
                var processes = Process.GetProcessesByName("EricGameLauncher");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(); } catch { }
                }
                await Task.Delay(1000);

                // Step 3: Extract and replace
                Console.WriteLine($"[3/4] Applying updates...");
                string stagingDir = Path.Combine(installDir, "._update_staging");
                string backupDir = Path.Combine(installDir, "._update_backup");

                // Clear any leftover staging/backup from previous failed runs
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
                Directory.CreateDirectory(stagingDir);

                try
                {
                    // Phase 1: Extract everything to staging directory
                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string stagingPath = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                            if (!stagingPath.StartsWith(stagingDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue; // Zip slip

                            // Do not extract data configs
                            if (entry.FullName.ToLower().StartsWith("data/")) continue;

                            string destDir = Path.GetDirectoryName(stagingPath)!;
                            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                            entry.ExtractToFile(stagingPath, true);
                        }
                    }

                    // Phase 2: Backup current files
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

                    // Phase 3: Move from staging to actual
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
                        // Rollback from backup
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
                    throw; // Rethrow to show standard error and exit
                }
                finally
                {
                    // Cleanup phase
                    try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true); } catch { }
                    try { if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true); } catch { }
                }

                // Step 4: Restart
                Console.WriteLine($"[4/4] Restarting application...");
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

                Console.WriteLine();
                Console.WriteLine("Update successful! Closing updater...");
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine("Please try manual update or check network connection.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
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
