using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Principal;

namespace Updater
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "Eric Game Launcher - Updater";
            Console.WriteLine("========================================");
            Console.WriteLine("    Eric Game Launcher Update System    ");
            Console.WriteLine("========================================");
            Console.WriteLine();

            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Updater.exe <install_dir> <download_url>");
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
                            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
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
                using (var client = new HttpClient())
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
                using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                {
                    // Clean install dir EXCEPT data folder
                    var dirInfo = new DirectoryInfo(installDir);
                    if (dirInfo.Exists)
                    {
                        foreach (var file in dirInfo.GetFiles())
                        {
                            try { file.Delete(); } catch { }
                        }
                        foreach (var dir in dirInfo.GetDirectories())
                        {
                            if (dir.Name.ToLower() != "data")
                            {
                                try { dir.Delete(true); } catch { }
                            }
                        }
                    }

                    // Extract all
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

                        string destinationPath = Path.Combine(installDir, entry.FullName);
                        string destDir = Path.GetDirectoryName(destinationPath)!;

                        if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                        
                        // Don't overwrite data folder content if it somehow exists in zip
                        if (entry.FullName.ToLower().StartsWith("data/")) continue;

                        entry.ExtractToFile(destinationPath, true);
                    }
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
