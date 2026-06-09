using System;
using System.IO;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

using System.Runtime.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: updater.cfgver.exe <items_path>");
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

            List<object>? rulesList = null;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("updater.cfgver.migration_rules.yaml");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string rulesYaml = reader.ReadToEnd();
                    var deserializer = new DeserializerBuilder().Build();
                    rulesList = deserializer.Deserialize<List<object>>(rulesYaml);
                }
            }
            catch (Exception ex) { Log($"RulesLoadFailed info={ex}"); return; }

            if (rulesList == null) return;

            Dictionary<object, object>? configRoot;
            try
            {
                string configYaml = File.ReadAllText(inputPath);
                var deserializer = new DeserializerBuilder().Build();
                configRoot = deserializer.Deserialize<Dictionary<object, object>>(configYaml);
            }
            catch (Exception ex) { Log($"ConfigReadFailed info={ex}"); return; }

            if (configRoot == null) return;

            int currentVersion = 1;
            if (configRoot.TryGetValue("version", out var verObj) && verObj != null)
            {
                try { currentVersion = Convert.ToInt32(verObj); } catch { currentVersion = 1; }
            }

            bool migratedAny = false;
            int startVersion = currentVersion;

            while (true)
            {
                Dictionary<object, object>? matchingRule = null;
                foreach (var r in rulesList)
                {
                    if (r is Dictionary<object, object> rule)
                    {
                        int fromVer = 0;
                        if (rule.TryGetValue("from", out var fv) && fv != null)
                            try { fromVer = Convert.ToInt32(fv); } catch { }
                        if (fromVer == currentVersion)
                        {
                            matchingRule = rule;
                            break;
                        }
                    }
                }

                if (matchingRule == null) break;

                int nextVersion = 0;
                if (matchingRule.TryGetValue("to", out var tv) && tv != null)
                    try { nextVersion = Convert.ToInt32(tv); } catch { }

                if (matchingRule.TryGetValue("transformations", out var tfsObj) && tfsObj is List<object> transformations)
                {
                    if (configRoot.TryGetValue("items", out var itemsObj) && itemsObj is List<object> itemsArr)
                    {
                        foreach (var itemNode in itemsArr)
                        {
                            if (itemNode is not Dictionary<object, object> itemObj) continue;
                            foreach (var tf in transformations)
                            {
                                if (tf is not Dictionary<object, object> tfObj) continue;
                                if (!tfObj.TryGetValue("type", out var typeObj)) continue;
                                string tfType = typeObj?.ToString() ?? "";

                                if (tfType == "MoveAndGroup")
                                {
                                    string targetObjName = "";
                                    if (tfObj.TryGetValue("target", out var tgt) && tgt != null) targetObjName = tgt.ToString() ?? "";
                                    if (tfObj.TryGetValue("sourceFields", out var sfObj) && sfObj is Dictionary<object, object> sourceFields)
                                    {
                                        if (!string.IsNullOrEmpty(targetObjName))
                                        {
                                            var newDict = new Dictionary<object, object>();
                                            bool hasAnyField = false;

                                            foreach (var field in sourceFields)
                                            {
                                                string newPropName = field.Key?.ToString() ?? "";
                                                string oldPropName = field.Value?.ToString() ?? "";

                                                if (itemObj.TryGetValue(oldPropName, out var oldPropValue) && oldPropValue != null)
                                                {
                                                    hasAnyField = true;
                                                    newDict[newPropName] = DeepCopyYaml(oldPropValue);
                                                    itemObj.Remove(oldPropName);
                                                }
                                            }

                                            if (hasAnyField)
                                            {
                                                itemObj[targetObjName] = newDict;
                                            }
                                        }
                                    }
                                }
                                else if (tfType == "SplitRecycleBin")
                                {
                                    string itemsKey = "items";
                                    if (tfObj.TryGetValue("itemsKey", out var ik) && ik != null) itemsKey = ik.ToString() ?? "items";
                                    string recycleKey = "recycleBin";
                                    if (tfObj.TryGetValue("recycleKey", out var rk) && rk != null) recycleKey = rk.ToString() ?? "recycleBin";
                                    string statusField = "status";
                                    if (tfObj.TryGetValue("statusField", out var sf) && sf != null) statusField = sf.ToString() ?? "status";
                                    int normalValue = 0;
                                    if (tfObj.TryGetValue("normalValue", out var nv) && nv != null) try { normalValue = Convert.ToInt32(nv); } catch { }

                                    if (configRoot.TryGetValue(itemsKey, out var itemsRoot) && itemsRoot is List<object> itemsArray)
                                    {
                                        List<object> recycleArray;
                                        if (configRoot.TryGetValue(recycleKey, out var recycleRoot) && recycleRoot is List<object> existingRecycle)
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
                                            if (itemsArray[i] is not Dictionary<object, object> obj) continue;
                                            int statusValue = normalValue;
                                            if (obj.TryGetValue(statusField, out var statusNode) && statusNode != null)
                                            {
                                                try { statusValue = Convert.ToInt32(statusNode); } catch (Exception ex) { Log($"ParseStatusFailed {ex}"); statusValue = normalValue; }
                                            }

                                            if (statusValue != normalValue)
                                            {
                                                recycleArray.Add(DeepCopyYaml(obj));
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
                configRoot["version"] = currentVersion;
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

        private static object DeepCopyYaml(object obj)
        {
            var serializer = new SerializerBuilder().Build();
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<object>(serializer.Serialize(obj));
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
