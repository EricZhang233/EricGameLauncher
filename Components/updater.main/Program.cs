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
using EricGameLauncher.Installation;

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
            if (args.Length >= 4 && args[0] == "--restart-agent")
            {
                await RunRestartAgentAsync(args[1], args[2], args[3], args.Skip(4));
                return;
            }
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
            string ownerSid = GetArgValue(args, "--owner-sid");
            if (ownerSid.Length == 0)
            {
                ownerSid = InstallRegistration.CurrentUserSid;
                args = args.Concat(new[] { "--owner-sid", ownerSid }).ToArray();
            }
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
            string tempZip = Path.Combine(cacheDir, $"update_{Guid.NewGuid():N}.exe");
            string restartSignal = GetArgValue(args, "--restart-signal");
            if (string.IsNullOrEmpty(restartSignal))
            {
                restartSignal = Path.Combine(cacheDir, "restart-" + Guid.NewGuid().ToString("N"));
                var broker = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string value in new[] { "--restart-agent", restartSignal, Path.GetFullPath(installDir), "EricGameLauncher.exe" }) broker.ArgumentList.Add(value);
                using var started = Process.Start(broker) ?? throw new InvalidOperationException();
                args = args.Concat(new[] { "--restart-signal", restartSignal }).ToArray();
                Log($"Original-token restart agent started pid={started.Id}");
            }
            void SignalRestart(string state)
            {
                string temporary = restartSignal + "." + Guid.NewGuid().ToString("N");
                try { File.WriteAllText(temporary, state); File.Move(temporary, restartSignal, true); }
                finally { try { File.Delete(temporary); } catch { } }
            }
            void FailRestart() { try { SignalRestart("FAIL"); } catch { } }

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
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        foreach (string value in args) psi.ArgumentList.Add(value);
                        Process.Start(psi);
                        Log("ElevationRelaunch");
                        Environment.ExitCode = 2;
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR: Elevation failed: {ex}");
                        Log($"ElevationFailed {ex}");
                        FailRestart();
                        Environment.ExitCode = 1;
                        await Task.Delay(5000);
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Target directory is read-only despite administrator privileges.");
                    Log("ElevationNoWriteAccess");
                    FailRestart();
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
                    pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipeClient.Connect(5000);
                    pipeWriter = new StreamWriter(pipeClient, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
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
                using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                using var monitor = new CancellationTokenSource();
                var approved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!string.IsNullOrEmpty(pipeName) && pipeClient == null) throw new IOException(GetArgValue(args, "--controller-error"));
                async Task ReadControlAsync()
                {
                    if (pipeClient == null) { approved.TrySetResult(true); return; }
                    try
                    {
                        using var control = new StreamReader(pipeClient, Encoding.UTF8, true, 1024, leaveOpen: true);
                        if (await control.ReadLineAsync(operation.Token) == "PROCEED") approved.TrySetResult(true);
                        else operation.Cancel();
                    }
                    catch { operation.Cancel(); }
                }
                async Task WatchCancellationAsync()
                {
                    string cancel = GetArgValue(args, "--cancel-file");
                    try
                    {
                        while (!monitor.IsCancellationRequested)
                        {
                            if (cancel.Length > 0 && File.Exists(cancel)) { operation.Cancel(); return; }
                            await Task.Delay(200, monitor.Token);
                        }
                    }
                    catch (OperationCanceledException) { }
                }
                var controlTask = ReadControlAsync();
                var cancellationTask = WatchCancellationAsync();
                using var stopMonitor = operation.Token.Register(() => monitor.Cancel());
                try
                {
                var downloadTaskSw = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"[1/4] Downloading update package...");
                Log($"Download Start url={downloadUrl}");
                SendProgress("DOWNLOAD 0");
                using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
                    using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await UpdateDownload.DownloadAsync(client, downloadUrl, fileStream, percent => SendProgress($"DOWNLOAD {percent:F1}"),
                        TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(60), operation.Token);
                }
                Console.WriteLine("\n      Download completed.");
                Log($"Download Complete duration={downloadTaskSw.ElapsedMilliseconds}ms size={new FileInfo(tempZip).Length}");

                using var verifiedPackage = new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!await UpdatePackageIntegrity.VerifyAsync(verifiedPackage, expectedDigest, operation.Token))
                {
                    Console.Error.WriteLine(GetArgValue(args, "--verification-error"));
                    Log("HASH_MISMATCH: update stopped before READY and installation");
                    FailRestart();
                    SendProgress("ERROR HASH_MISMATCH");
                    Environment.ExitCode = 1;
                    try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }
                    WaitForFailureDismissal(args);
                    return;
                }
                verifiedPackage.Position = 0;
                Log("SHA256 verification passed");

                SendProgress("READY");
                await approved.Task.WaitAsync(operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                monitor.Cancel();
                await cancellationTask;
                await controlTask;
                try { pipeWriter?.Dispose(); pipeClient?.Dispose(); } catch { }

                if (mainPid > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(mainPid);
                        Log($"WaitingForMainProcess pid={mainPid}");
                        await proc.WaitForExitAsync(operation.Token);
                        Log($"MainProcessExited pid={mainPid}");
                    }
                    catch (ArgumentException) { }
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
                        if (path == null || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir)), StringComparison.OrdinalIgnoreCase)) continue;
                        p.CloseMainWindow();
                        if (!p.WaitForExit(10000)) { p.Kill(); p.WaitForExit(); }
                    }
                    catch (Exception ex) { Log($"CloseTargetProcessFailed {ex}"); throw; }
                }
                await Task.Delay(1000);
                Log("CloseLauncher End");

                Console.WriteLine($"[3/4] Applying updates...");
                Log("ApplyUpdates Start");
                if (InstallRegistration.Find(installDir, ownerSid) != null)
                {
                    if (verifiedPackage.ReadByte() != 'M' || verifiedPackage.ReadByte() != 'Z') throw new InvalidDataException("UPDATE_INSTALLER_REQUIRED");
                    verifiedPackage.Position = 0;
                    using (var archive = new ZipArchive(verifiedPackage, ZipArchiveMode.Read, leaveOpen: true))
                        if (!archive.Entries.Any(e => e.FullName == "EricGameLauncher.exe")) throw new InvalidDataException("UPDATE_INVALID_INSTALLER");
                    var setup = new ProcessStartInfo(tempZip) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = cacheDir };
                    foreach (string value in new[] { "--quiet", "--update", "--directory", Path.GetFullPath(installDir), "--owner-sid", ownerSid }) setup.ArgumentList.Add(value);
                    Log("Applying verified installer silently");
                    using var installation = Process.Start(setup) ?? throw new IOException("UPDATE_INSTALLER_START_FAILED");
                    await installation.WaitForExitAsync();
                    if (installation.ExitCode != 0) throw new IOException($"UPDATE_INSTALLER_FAILED:{installation.ExitCode}");
                }
                else
                {
                    Log("Applying verified package as ZIP for portable installation");
                    verifiedPackage.Position = 0;
                    using var archive = new ZipArchive(verifiedPackage, ZipArchiveMode.Read, leaveOpen: true);
                    UpdateTransaction.Apply(installDir, archive);
                }


                Console.WriteLine($"[4/4] Restarting application...");
                Log("Restart Start");
                string exePath = Path.Combine(installDir, "EricGameLauncher.exe");
                if (File.Exists(exePath))
                {
                    SignalRestart("SUCCESS");
                }
                Log("Restart End");

                Console.WriteLine();
                Console.WriteLine("Update successful! Closing updater...");
                Log("Success");
                await Task.Delay(2000);
                }
                finally
                {
                    monitor.Cancel();
                    operation.Cancel();
                    await cancellationTask;
                    await controlTask;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: " + ex);
                Console.WriteLine("Please try manual update or check network connection.");
                Log($"Failure {ex}");
                FailRestart();
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

        private static async Task RunRestartAgentAsync(string signal, string directory, string executable, IEnumerable<string> arguments)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(35));
                string status = "";
                while (status is not ("SUCCESS" or "FAIL"))
                {
                    if (File.Exists(signal)) status = File.ReadAllText(signal);
                    if (status is not ("SUCCESS" or "FAIL")) await Task.Delay(200, deadline.Token);
                }
                if (status != "SUCCESS") return;
                string target = Path.Combine(directory, executable);
                if (Path.GetFileName(executable) != executable || !File.Exists(target)) return;
                var start = new ProcessStartInfo(target) { WorkingDirectory = directory, UseShellExecute = false };
                foreach (string argument in arguments) start.ArgumentList.Add(argument);
                using var restarted = Process.Start(start);
                Log($"Restarted from original-token agent pid={restarted?.Id}");
            }
            catch (Exception ex) { Log($"RestartAgentFailed {ex}"); Environment.ExitCode = 1; }
            finally { try { File.Delete(signal); } catch { } }
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

