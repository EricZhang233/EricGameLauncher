using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Principal;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using EricGameLauncher;

using System.Runtime.Versioning;

namespace updater.main
{
    class Program
    {
        private static readonly object _logLock = new();
        private static readonly string _logFile = Path.Combine(
            Path.GetTempPath(), "eric", "ericgamelauncher", "log",
            $"updater.main.{DateTime.Now:yyyyMMdd-HHmmss}.log");

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z Updater/INFO] | {message}";
                lock (_logLock) { File.AppendAllText(_logFile, line + Environment.NewLine); }
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

            if (args.Length == 1 && args[0] == "--tasks")
            {
                if (!TryRunTaskFile()) Environment.ExitCode = 1;
                Log("TaskFileExecuted");
                return;
            }

            if (args.Length > 0 && (args[0].Equals("--run-cmd", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--run-bat", StringComparison.OrdinalIgnoreCase)))
            {
                if (args.Length < 2) { Environment.ExitCode = 1; return; }
                TryHandleCommandMode(args);
                return;
            }
            if (args.Length < 2)
            {
                if (TryHandleCommandMode(args)) return;
                Console.WriteLine("Usage: updater.main.exe <install_dir> <download_url> --sha256 <sha256:digest> [--pipe-name <name>] [--main-pid <pid>]");
                Console.WriteLine("       updater.main.exe --run-bat <path> [--args <args>] [--workdir <dir>] [--wait]");
                Console.WriteLine("       updater.main.exe --run-cmd <command> [--workdir <dir>] [--wait]");
                Log("InvalidArgs");
                Environment.ExitCode = 1;
                await Task.Delay(3000);
                return;
            }

            string installDir = args[0];
            string downloadUrl = args[1];
            string expectedDigest = GetArgValue(args, "--sha256");
            if (!UpdatePackageIntegrity.IsValidDigest(expectedDigest))
            {
                Console.Error.WriteLine(GetArgValue(args, "--verification-error"));
                Log("HASH_METADATA_INVALID: update stopped before download");
                Environment.ExitCode = 1;
                WaitForFailureDismissal(args);
                return;
            }
            Log($"Args installDir={installDir}");
            Log($"Args downloadUrl={downloadUrl}");
            Log($"InstallDir exists={Directory.Exists(installDir)}");
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
                        Environment.ExitCode = 2;
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Elevation failed: {ex}");
                        Log($"ElevationFailed {ex}");
                        Environment.ExitCode = 1;
                        await Task.Delay(5000);
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Target directory is read-only despite administrator privileges.");
                    Log("ElevationNoWriteAccess");
                    Environment.ExitCode = 1;
                    await Task.Delay(5000);
                    return;
                }
            }

            string pipeName = GetArgValue(args, "--pipe-name");
            int mainPid = int.TryParse(GetArgValue(args, "--main-pid"), out var pid) ? pid : 0;

            NamedPipeClientStream? pipeClient = null;
            StreamWriter? pipeWriter = null;
            if (!string.IsNullOrEmpty(pipeName))
            {
                try
                {
                    pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    pipeClient.Connect(5000);
                    pipeWriter = new StreamWriter(pipeClient) { AutoFlush = true };
                    Log("PipeConnected");
                }
                catch (Exception ex) { Log($"PipeConnectFailed {ex}"); pipeClient?.Dispose(); pipeClient = null; pipeWriter = null; }
            }

            void SendProgress(string msg)
            {
                try { pipeWriter?.WriteLine(msg); } catch { }
            }

            try
            {
                var downloadTaskSw = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"[1/4] Downloading update package...");
                Log($"Download Start url={downloadUrl}");
                SendProgress("DOWNLOAD 0");
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

                                    if (canReportProgress)
                                    {
                                        double percent = (double)totalRead / totalBytes * 100;
                                        SendProgress($"DOWNLOAD {percent:F1}");
                                    }
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

                using var verifiedPackage = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!await UpdatePackageIntegrity.VerifyAsync(verifiedPackage, expectedDigest))
                {
                    Console.Error.WriteLine(GetArgValue(args, "--verification-error"));
                    Log("HASH_MISMATCH: update stopped before READY and installation");
                    SendProgress("ERROR HASH_MISMATCH");
                    Environment.ExitCode = 1;
                    try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }
                    WaitForFailureDismissal(args);
                    return;
                }
                verifiedPackage.Position = 0;
                Log("SHA256 verification passed");

                SendProgress("READY");
                try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }

                if (mainPid > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(mainPid);
                        Log($"WaitingForMainProcess pid={mainPid}");
                        proc.WaitForExit();
                        Log($"MainProcessExited pid={mainPid}");
                    }
                    catch (Exception ex) { Log($"WaitMainProcessFailed {ex}"); }
                }

                Console.WriteLine($"[2/4] Closing Eric Game Launcher...");
                Log("CloseLauncher Start");
                var processes = Process.GetProcessesByName("EricGameLauncher");
                Log($"CloseLauncher foundProcesses={processes.Length}");
                foreach (var p in processes)
                {
                    using (p)
                    try
                    {
                        string? path = p.MainModule?.FileName;
                        if (path == null || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                        p.CloseMainWindow();
                        if (!p.WaitForExit(10000)) { p.Kill(); p.WaitForExit(); }
                    }
                    catch (Exception ex) { Log($"CloseTargetProcessFailed {ex}"); throw; }
                }
                await Task.Delay(1000);
                Log("CloseLauncher End");

                Console.WriteLine($"[3/4] Applying updates...");
                Log("ApplyUpdates Start");
                using (var archive = new ZipArchive(verifiedPackage, ZipArchiveMode.Read, leaveOpen: true))
                    UpdateTransaction.Apply(installDir, archive);


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
                Console.WriteLine("ERROR: " + ex);
                Console.WriteLine("Please try manual update or check network connection.");
                Log($"Failure {ex}");
                SendProgress("ERROR UPDATE_FAILED");
                Environment.ExitCode = 1;
                try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }
                WaitForFailureDismissal(args);
            }
            finally
            {
                try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch (Exception ex) { Log($"DeleteTempZipFailed {ex}"); }
                Log($"End duration={sw.ElapsedMilliseconds}ms");
            }
        }

        private static void WaitForFailureDismissal(string[] args)
        {
            if (Console.IsInputRedirected) return;
            string prompt = GetArgValue(args, "--failure-close-message");
            if (!string.IsNullOrWhiteSpace(prompt)) Console.WriteLine(prompt);
            Console.ReadKey(true);
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

        private static bool TryHandleCommandMode(string[] args)
        {
            if (args.Length == 0) return false;
            if (args[0].Equals("--run-bat", StringComparison.OrdinalIgnoreCase))
            {
                string? path = args.Length > 1 ? args[1] : null;
                string batArgs = GetArgValue(args, "--args");
                string workDir = GetArgValue(args, "--workdir");
                bool wait = args.Any(a => a.Equals("--wait", StringComparison.OrdinalIgnoreCase));
                return RunBatch(path, batArgs, workDir, wait);
            }
            if (args[0].Equals("--run-cmd", StringComparison.OrdinalIgnoreCase))
            {
                string? command = args.Length > 1 ? args[1] : null;
                string workDir = GetArgValue(args, "--workdir");
                bool wait = args.Any(a => a.Equals("--wait", StringComparison.OrdinalIgnoreCase));
                return RunCommand(command, workDir, wait);
            }
            return false;
        }

        private static bool TryRunTaskFile()
        {
            try
            {
                string taskPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "updater.main.tasks.yaml");
                if (!File.Exists(taskPath)) return false;
                string yamlText = File.ReadAllText(taskPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yamlObj = deserializer.Deserialize<object>(yamlText);
                var normalized = NormalizeYamlObject(yamlObj) as List<object>;
                if (normalized == null) throw new InvalidDataException("Task file must contain a sequence.");
                foreach (var item in normalized)
                {
                    if (item is not Dictionary<string, object> cmd) throw new InvalidDataException("Invalid task entry.");
                    ExecuteCommandDict(cmd);
                    if (Environment.ExitCode != 0) break;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"TaskFileFailed {ex}");
                Environment.ExitCode = 1;
                return true;
            }
        }

        private static void ExecuteCommandDict(Dictionary<string, object> cmd)
        {
            string type = GetString(cmd, "Type").ToLowerInvariant();
            string workDir = GetString(cmd, "WorkDir");
            bool wait = GetBool(cmd, "Wait");
            if (type == "bat")
            {
                string path = GetString(cmd, "Path");
                string args = GetString(cmd, "Args");
                RunBatch(path, args, workDir, wait);
                return;
            }
            if (type == "cmd")
            {
                string command = GetString(cmd, "Command");
                RunCommand(command, workDir, wait);
                return;
            }
            throw new InvalidDataException("Unknown task type.");
        }

        private static object? NormalizeYamlObject(object? obj)
        {
            if (obj is Dictionary<object, object> dict)
            {
                var res = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    var key = kvp.Key?.ToString() ?? "";
                    if (string.IsNullOrEmpty(key)) continue;
                    res[key] = NormalizeYamlObject(kvp.Value) ?? "";
                }
                return res;
            }
            if (obj is List<object> list)
            {
                var res = new List<object>();
                foreach (var item in list)
                {
                    res.Add(NormalizeYamlObject(item) ?? "");
                }
                return res;
            }
            return obj;
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var obj)) return false;
            if (obj is bool b) return b;
            if (bool.TryParse(obj?.ToString(), out var parsed)) return parsed;
            return false;
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var obj)) return "";
            return obj?.ToString() ?? "";
        }

        private static string GetArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return "";
        }

        private static bool RunBatch(string? path, string batArgs, string workDir, bool wait)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                Log("RunBatch invalid path");
                Environment.ExitCode = 1;
                return true;
            }
            string args = $"/c \"\"{path}\" {batArgs}\"";
            return StartProcess("cmd.exe", args, workDir, wait, "RunBatch");
        }

        private static bool RunCommand(string? command, string workDir, bool wait)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                Log("RunCommand empty command");
                Environment.ExitCode = 1;
                return true;
            }
            string args = $"/c {command}";
            return StartProcess("cmd.exe", args, workDir, wait, "RunCommand");
        }

        private static bool StartProcess(string fileName, string arguments, string workDir, bool wait, string tag)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? Environment.CurrentDirectory : workDir
                };
                Log($"{tag} start file={fileName} args={arguments} workDir={psi.WorkingDirectory}");
                using var proc = Process.Start(psi);
                if (proc == null) { Environment.ExitCode = 1; return true; }
                if (wait)
                {
                    proc.WaitForExit();
                    Log($"{tag} exit code={proc.ExitCode}");
                    Environment.ExitCode = proc.ExitCode;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"{tag} failed {ex}");
                Environment.ExitCode = 1;
                return true;
            }
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

