using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

using System.Runtime.Versioning;

namespace updater.cfgver
{
    class Program
    {
        private static readonly object _logLock = new();

        private static void Log(string message)
        {
            try
            {
                LogService.Write("UpdaterCfg", message);
            }
            catch (Exception ex)
            {
                try
                {
                    string fbdir = Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher", "log");
                    Directory.CreateDirectory(fbdir);
                    string fb = Path.Combine(fbdir, "updater.cfgver.fallback.log");
                    File.AppendAllText(fb, ex.ToString() + Environment.NewLine);
                }
                catch { }
            }
        }

        [SupportedOSPlatform("windows")]
        static void Main(string[] args)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"Start argsCount={args.Length}");
            if (TryHandleCommandMode(args)) return;
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: updater.cfgver.exe <config_path>");
                Console.WriteLine("       updater.cfgver.exe --run-bat <path> [--args <args>] [--workdir <dir>] [--wait]");
                Console.WriteLine("       updater.cfgver.exe --run-cmd <command> [--workdir <dir>] [--wait]");
                Log("InvalidArgs");
                return;
            }

            string inputPath = args[0];
            Log($"Input {inputPath}");

            if (inputPath.StartsWith("\"") && inputPath.EndsWith("\""))
            {
                inputPath = inputPath.Substring(1, inputPath.Length - 2);
            }

            if (!File.Exists(inputPath))
            {
                Log("InputMissing");
                return;
            }

            List<Dictionary<string, object>> rulesArray = new();
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("updater.cfgver.migration_rules.yaml");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string rulesYaml = reader.ReadToEnd();
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    var yamlObj = deserializer.Deserialize<object>(rulesYaml);
                    var normalized = NormalizeYamlObject(yamlObj) as List<object>;
                    if (normalized != null)
                    {
                        rulesArray = normalized
                            .OfType<Dictionary<string, object>>()
                            .ToList();
                    }
                }
            }
            catch (Exception ex) { Log($"RulesLoadFailed info={ex}"); return; }

            if (rulesArray.Count == 0) return;

            Dictionary<string, object>? configRoot;
            try
            {
                string configYaml = File.ReadAllText(inputPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yamlObj = deserializer.Deserialize<object>(configYaml);
                configRoot = NormalizeYamlObject(yamlObj) as Dictionary<string, object>;
            }
            catch (Exception ex) { Log($"ConfigReadFailed info={ex}"); return; }

            if (configRoot == null) return;

            int currentVersion = 1;
            if (configRoot.TryGetValue("Version", out var verObj))
                currentVersion = GetInt(verObj, 1);

            bool migratedAny = false;
            int startVersion = currentVersion;

            while (true)
            {
                Dictionary<string, object>? matchingRule = null;
                foreach (var rule in rulesArray)
                {
                    if (TryGetInt(rule, "From", out var fromVersion) && fromVersion == currentVersion)
                    {
                        matchingRule = rule;
                        break;
                    }
                }

                if (matchingRule == null) break;

                int nextVersion = GetInt(matchingRule["To"], currentVersion);

                if (TryGetList(matchingRule, "Commands", out var commands))
                {
                    foreach (var cmd in commands)
                    {
                        if (cmd is not Dictionary<string, object> cmdObj) continue;
                        ExecuteCommandDict(cmdObj);
                    }
                }

                if (TryGetList(matchingRule, "Transformations", out var transformations) && TryGetList(configRoot, "items", out var itemsArr))
                {
                    foreach (var itemNode in itemsArr)
                    {
                        if (itemNode is Dictionary<string, object> itemObj)
                        {
                            foreach (var tf in transformations)
                            {
                                if (tf is not Dictionary<string, object> tfObj) continue;
                                string tfType = GetString(tfObj, "Type");

                                if (tfType == "MoveAndGroup")
                                {
                                    string targetObjName = GetString(tfObj, "Target");
                                    if (!TryGetDict(tfObj, "SourceFields", out var sourceFields)) continue;

                                    if (!string.IsNullOrEmpty(targetObjName))
                                    {
                                        var newObj = new Dictionary<string, object>();
                                        bool hasAnyField = false;

                                        foreach (var field in sourceFields)
                                        {
                                            string newPropName = field.Key;
                                            string oldPropName = field.Value?.ToString() ?? "";

                                            if (!string.IsNullOrEmpty(oldPropName) && itemObj.TryGetValue(oldPropName, out var oldPropValue))
                                            {
                                                hasAnyField = true;
                                                newObj[newPropName] = oldPropValue;
                                                itemObj.Remove(oldPropName);
                                            }
                                        }

                                        if (hasAnyField)
                                        {
                                            itemObj[targetObjName] = newObj;
                                        }
                                    }
                                }
                                else if (tfType == "SplitRecycleBin")
                                {
                                    string itemsKey = GetString(tfObj, "ItemsKey", "items");
                                    string recycleKey = GetString(tfObj, "RecycleKey", "recycleBinItems");
                                    string statusField = GetString(tfObj, "StatusField", "Status");
                                    int normalValue = GetInt(tfObj, "NormalValue", 0);

                                    if (TryGetList(configRoot, itemsKey, out var itemsArray))
                                    {
                                        List<object> recycleArray;
                                        if (TryGetList(configRoot, recycleKey, out var existingRecycle))
                                        {
                                            recycleArray = existingRecycle;
                                        }
                                        else
                                        {
                                            recycleArray = new List<object>();
                                            configRoot[recycleKey] = recycleArray;
                                        }

                                        for (int i = itemsArray.Count - 1; i >= 0; i--)
                                        {
                                            if (itemsArray[i] is not Dictionary<string, object> obj) continue;
                                            int statusValue = normalValue;
                                            if (obj.TryGetValue(statusField, out var statusNode))
                                            {
                                                statusValue = GetInt(statusNode, normalValue);
                                            }

                                            if (statusValue != normalValue)
                                            {
                                                recycleArray.Add(obj);
                                                itemsArray.RemoveAt(i);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                currentVersion = nextVersion;
                migratedAny = true;
                configRoot["Version"] = currentVersion;
            }

            if (!migratedAny) return;

            string backupPath = $"{inputPath}.bak.v{startVersion}";
            try
            {
                File.Copy(inputPath, backupPath, true);
            }
            catch (Exception ex) { Log($"BackupFailed info={ex}"); return; }

            try
            {
                var serializer = new SerializerBuilder().Build();
                string newYaml = serializer.Serialize(configRoot);
                File.WriteAllText(inputPath, newYaml);
            }
            catch (Exception ex) { Log($"WriteFailed info={ex}"); }
            Log($"End duration={sw.ElapsedMilliseconds}ms modified={(migratedAny ? "yes" : "no")}");
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

        private static bool TryGetList(Dictionary<string, object> dict, string key, out List<object> list)
        {
            list = new List<object>();
            if (!dict.TryGetValue(key, out var obj)) return false;
            if (obj is List<object> l)
            {
                list = l;
                return true;
            }
            return false;
        }

        private static bool TryGetDict(Dictionary<string, object> dict, string key, out Dictionary<string, object> map)
        {
            map = new Dictionary<string, object>();
            if (!dict.TryGetValue(key, out var obj)) return false;
            if (obj is Dictionary<string, object> m)
            {
                map = m;
                return true;
            }
            return false;
        }

        private static bool TryGetInt(Dictionary<string, object> dict, string key, out int value)
        {
            value = 0;
            if (!dict.TryGetValue(key, out var obj)) return false;
            value = GetInt(obj, 0);
            return true;
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (!dict.TryGetValue(key, out var obj)) return defaultValue;
            return GetInt(obj, defaultValue);
        }

        private static int GetInt(object? obj, int defaultValue)
        {
            if (obj == null) return defaultValue;
            if (obj is int i) return i;
            if (obj is long l) return (int)l;
            if (obj is double d) return (int)d;
            if (int.TryParse(obj.ToString(), out var parsed)) return parsed;
            return defaultValue;
        }

        private static string GetString(Dictionary<string, object> dict, string key, string defaultValue = "")
        {
            if (!dict.TryGetValue(key, out var obj)) return defaultValue;
            var text = obj?.ToString() ?? "";
            return string.IsNullOrEmpty(text) ? defaultValue : text;
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

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var obj)) return false;
            if (obj is bool b) return b;
            if (bool.TryParse(obj?.ToString(), out var parsed)) return parsed;
            return false;
        }
    }

        internal static class LogService
        {
            private const long MaxLogFileBytes = 5 * 1024 * 1024;
            private static string LogDir => Path.Combine(Path.GetTempPath(), "eric", "ericgamelauncher", "log");
            private static string LogFilePath => Path.Combine(LogDir, "updater.cfgver.log");

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
}
