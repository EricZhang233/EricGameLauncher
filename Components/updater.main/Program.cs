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
                lock (_logLock) { File.AppendAllText(_logFile, line + Environment.NewLine + message + Environment.NewLine); }
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

            if (TryRunTaskFile())
            {
                Log("TaskFileExecuted");
                return;
            }

            if (args.Length < 2)
            {
                if (TryHandleCommandMode(args)) return;
                Console.WriteLine("Usage: updater.main.exe <install_dir> <download_url> [--ready-event <name>] [--main-pid <pid>]");
                Console.WriteLine("       updater.main.exe --run-bat <path> [--args <args>] [--workdir <dir>] [--wait]");
                Console.WriteLine("       updater.main.exe --run-cmd <command> [--workdir <dir>] [--wait]");
                Log("InvalidArgs");
                await Task.Delay(3000);
                return;
            }

            string installDir = args[0];
            string downloadUrl = args[1];
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
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Elevation failed: {ex}");
                        Log($"ElevationFailed {ex}");
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
                    try { p.Kill(); p.WaitForExit(); Log($"Killed process Id={p.Id} Name={p.ProcessName}"); } catch (Exception ex) { Log($"KillProcessFailed {ex}"); }
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
                        .Where(f => !f.StartsWith(stagingDir) && !f.StartsWith(backupDir) && !f.ToLower().Contains("\\data\\") && !f.ToLower().EndsWith(".update_staging") && !f.ToLower().EndsWith(".update_backup"))
                        .ToList();
                    Log($"Applying updates: currentFilesToBackup={currentFiles.Count}");

                    foreach (var file in currentFiles)
                    {
                        string relative = Path.GetRelativePath(installDir, file);
                        string backupPath = Path.Combine(backupDir, relative);
                        string bDir = Path.GetDirectoryName(backupPath)!;
                        if (!Directory.Exists(bDir)) Directory.CreateDirectory(bDir);
                        File.Move(file, backupPath, true);
                    }
                    Log($"Applying updates: backup moved, backupDir={backupDir}");

                    var stagedFiles = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories).ToList();
                    Log($"Applying updates: stagedFilesCount={stagedFiles.Count}");
                    foreach (var file in stagedFiles)
                    {
                        string relative = Path.GetRelativePath(stagingDir, file);
                        string finalPath = Path.Combine(installDir, relative);
                        string fDir = Path.GetDirectoryName(finalPath)!;
                        if (!Directory.Exists(fDir)) Directory.CreateDirectory(fDir);
                        File.Move(file, finalPath, true);
                    }
                    Log("Applying updates: staged files moved into install directory");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error during update application: " + ex);
                    Console.WriteLine("Attempting rollback...");
                    try
                    {
                        if (Directory.Exists(backupDir))
                        {
                            var backupFiles = Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories).ToList();
                            Log($"Rollback: backupFilesCount={backupFiles.Count}");
                            foreach (var file in backupFiles)
                            {
                                string relative = Path.GetRelativePath(backupDir, file);
                                string finalPath = Path.Combine(installDir, relative);
                                if (File.Exists(finalPath)) File.Delete(finalPath);
                                File.Move(file, finalPath, true);
                            }
                            Log("Rollback: restored backup files");
                        }
                        Console.WriteLine("Rollback successful. The launcher was not corrupted.");
                    }
                    catch (Exception rbEx)
                    {
                        Console.WriteLine("FATAL: Rollback failed! " + rbEx);
                        Log($"Rollback failed: {rbEx}");
                    }
                    throw;
                }
                finally
                {
                    try { if (Directory.Exists(stagingDir)) { Directory.Delete(stagingDir, true); Log("Cleanup: stagingDir deleted"); } } catch (Exception ex) { Log($"Cleanup staging delete failed: {ex}"); }
                    try { if (Directory.Exists(backupDir)) { Directory.Delete(backupDir, true); Log("Cleanup: backupDir deleted"); } } catch (Exception ex) { Log($"Cleanup backup delete failed: {ex}"); }
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
                Console.WriteLine("ERROR: " + ex);
                Console.WriteLine("Please try manual update or check network connection.");
                Console.WriteLine("Press any key to exit...");
                Log($"Failure {ex}");
                Console.ReadKey();
            }
            finally
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch (Exception ex) { Log($"DeleteTempZipFailed {ex}"); }
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
                if (normalized == null) return false;
                foreach (var item in normalized)
                {
                    if (item is not Dictionary<string, object> cmd) continue;
                    ExecuteCommandDict(cmd);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"TaskFileFailed {ex}");
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
                var proc = Process.Start(psi);
                if (proc == null) return true;
                if (wait)
                {
                    proc.WaitForExit();
                    Log($"{tag} exit code={proc.ExitCode}");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log($"{tag} failed {ex}");
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

    internal static class LogService
    {
        private const long MaxLogFileBytes = 5 * 1024 * 1024;
        private static string LogDir => Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher", "log");
        private static string LogFilePath => Path.Combine(LogDir, "updater.main.log");

        internal enum LogLevel { Debug, Info, Warn, Error }

        private sealed class LogEntry
        {
            public DateTime Timestamp;
            public string? Tag;
            public string? Message;
            public Exception? Exception;
            public string? OperationId;
            public LogLevel Level;
            public string? Caller;
            public string? CallerFile;
            public int CallerLine;
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _entryPool = new();
        private static LogEntry RentEntry() => _entryPool.TryDequeue(out var e) ? e : new LogEntry();
        private static void ReturnEntry(LogEntry e)
        {
            e.Timestamp = default;
            e.Tag = null;
            e.Message = null;
            e.Exception = null;
            e.OperationId = null;
            e.Level = default;
            e.Caller = null;
            e.CallerFile = null;
            e.CallerLine = 0;
            _entryPool.Enqueue(e);
        }

        private static readonly Channel<LogEntry> _channel;
        private static readonly CancellationTokenSource _cts = new();
        private static readonly Task _writerTask;

        static LogService()
        {
            var options = new UnboundedChannelOptions { SingleReader = true, SingleWriter = false };
            _channel = Channel.CreateUnbounded<LogEntry>(options);
            _writerTask = Task.Run(WriteLoop);
        }

        internal static void Write(
            string tag,
            string message,
            Exception? ex = null,
            string? operationId = null,
            LogLevel level = LogLevel.Info,
            [CallerMemberName] string caller = "",
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
        {
            try
            {
                if (string.IsNullOrEmpty(tag)) tag = "LOG";
                if (message == null) message = string.Empty;

                string callerFileName = string.IsNullOrEmpty(callerFile) ? string.Empty : Path.GetFileName(callerFile);
                var entry = RentEntry();
                entry.Timestamp = DateTime.UtcNow;
                entry.Tag = tag;
                entry.Message = message;
                entry.Exception = ex;
                entry.OperationId = operationId;
                entry.Level = level;
                entry.Caller = caller;
                entry.CallerFile = callerFileName;
                entry.CallerLine = callerLine;
                _channel.Writer.TryWrite(entry);
            }
            catch { }
        }

        private static async Task WriteLoop()
        {
            try { Directory.CreateDirectory(LogDir); } catch { }

            StreamWriter? writer = null;
            try
            {
                while (await _channel.Reader.WaitToReadAsync(_cts.Token))
                {
                    if (writer == null)
                    {
                        RotateIfNeeded(LogFilePath);
                        writer = new StreamWriter(LogFilePath, append: true) { AutoFlush = false };
                    }

                    var sb = new StringBuilder(512);
                    while (_channel.Reader.TryRead(out var entry))
                    {
                        sb.Clear();
                        sb.Append('[');
                        sb.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        sb.Append("Z ");
                        sb.Append(entry.Tag);
                        sb.Append('/');
                        sb.Append(entry.Level.ToString().ToUpperInvariant());
                        sb.Append("] ");
                        sb.Append(entry.CallerFile);
                        sb.Append(':');
                        sb.Append(entry.CallerLine);
                        sb.Append('.');
                        sb.Append(entry.Caller);
                        if (!string.IsNullOrEmpty(entry.OperationId))
                        {
                            sb.Append(" op=");
                            sb.Append(entry.OperationId);
                        }
                        sb.Append(" | ");
                        sb.Append(entry.Message);
                        if (entry.Exception != null)
                        {
                            try
                            {
                                sb.Append(" Exception=");
                                sb.Append(entry.Exception.GetType().FullName);
                                sb.Append(':');
                                sb.Append(entry.Exception.Message);
                                sb.Append(" Stack=");
                                sb.Append(entry.Exception.StackTrace);
                            }
                            catch { }
                        }

                        await writer.WriteLineAsync(sb.ToString());
                        try { ReturnEntry(entry); } catch { }
                    }

                    await writer.FlushAsync();
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (writer != null)
                {
                    try { await writer.DisposeAsync(); } catch { }
                }
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var fi = new FileInfo(path);
                if (fi.Length <= MaxLogFileBytes) return;
                string archive = path + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(path, archive);
            }
            catch { }
        }

        internal static void FlushAndStop()
        {
            _channel.Writer.Complete();
            _cts.Cancel();
            try { _writerTask.Wait(TimeSpan.FromSeconds(3)); } catch { }
            try { _writerTask.Dispose(); } catch { }
        }
    }

