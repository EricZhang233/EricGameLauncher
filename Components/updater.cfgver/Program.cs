using System;
using System.IO;

using System.Runtime.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace updater.cfgver
{
    class Program
    {
        private static readonly object _logLock = new();
        private static readonly string _logFile = Path.Combine(
            Path.GetTempPath(), "eric", "ericgamelauncher", "log",
            $"updater.cfgver.{DateTime.Now:yyyyMMdd-HHmmss}.log");

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z UpdaterCfg/INFO] | {message}";
                lock (_logLock) { File.AppendAllText(_logFile, line + Environment.NewLine); }
            }
            catch { }
        }

        [SupportedOSPlatform("windows")]
        static int Main(string[] args)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Log($"Start argsCount={args.Length}");
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: updater.cfgver.exe <items_path>");
                Log("InvalidArgs");
                return 2;
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
                return 3;
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
            catch (Exception ex) { Log($"RulesLoadFailed info={ex}"); return 4; }

            if (rulesList == null) return 5;

            Dictionary<object, object>? configRoot;
            try
            {
                string configYaml = File.ReadAllText(inputPath);
                var deserializer = new DeserializerBuilder().Build();
                configRoot = deserializer.Deserialize<Dictionary<object, object>>(configYaml);
            }
            catch (Exception ex) { Log($"ConfigReadFailed info={ex}"); return 6; }

            if (configRoot == null) return 7;

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

            if (!migratedAny)
            {
                Log("NoRuleApplied");
                return 10;
            }

            string backupPath = $"{inputPath}.bak.v{startVersion}";
            try
            {
                File.Copy(inputPath, backupPath, true);
            }
            catch (Exception ex) { Log($"BackupFailed info={ex}"); return 8; }

            try
            {
                var serializer = new SerializerBuilder().Build();
                string newYaml = serializer.Serialize(configRoot);
                File.WriteAllText(inputPath, newYaml);
            }
            catch (Exception ex) { Log($"WriteFailed info={ex}"); return 9; }
            Log($"End duration={sw.ElapsedMilliseconds}ms modified={(migratedAny ? "yes" : "no")}");
            return 0;
        }

        private static object DeepCopyYaml(object obj)
        {
            var serializer = new SerializerBuilder().Build();
            var deserializer = new DeserializerBuilder().Build();
            return deserializer.Deserialize<object>(serializer.Serialize(obj));
        }
    }
}
