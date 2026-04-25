using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

using System.Runtime.Versioning;

namespace updater.cfgver
{
    class Program
    {
        [SupportedOSPlatform("windows")]
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: updater.cfgver.exe <config_path>");
                return;
            }

            string inputPath = args[0];

            if (inputPath.StartsWith("\"") && inputPath.EndsWith("\""))
            {
                inputPath = inputPath.Substring(1, inputPath.Length - 2);
            }

            if (!File.Exists(inputPath))
            {
                return;
            }

            JsonNode? rulesArray = null;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("updater.cfgver.migration_rules.json");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string rulesJson = reader.ReadToEnd();
                    rulesArray = JsonNode.Parse(rulesJson);
                }
            }
            catch { return; }

            if (rulesArray == null) return;

            JsonObject? configRoot;
            try
            {
                string configJson = File.ReadAllText(inputPath);
                configRoot = JsonNode.Parse(configJson)?.AsObject();
            }
            catch { return; }

            if (configRoot == null) return;

            int currentVersion = 1;
            if (configRoot.TryGetPropertyValue("Version", out var verNode) && verNode != null)
            {
                currentVersion = verNode.GetValue<int>();
            }

            bool migratedAny = false;
            int startVersion = currentVersion;

            while (true)
            {
                JsonNode? matchingRule = null;
                if (rulesArray is JsonArray arr)
                {
                    foreach (var rule in arr)
                    {
                        if (rule?["From"]?.GetValue<int>() == currentVersion)
                        {
                            matchingRule = rule;
                            break;
                        }
                    }
                }

                if (matchingRule == null) break;

                int nextVersion = matchingRule["To"]!.GetValue<int>();

                var transformations = matchingRule["Transformations"]?.AsArray();
                if (transformations != null && configRoot.TryGetPropertyValue("items", out var itemsNode) && itemsNode is JsonArray itemsArr)
                {
                    foreach (var itemNode in itemsArr)
                    {
                        if (itemNode is JsonObject itemObj)
                        {
                            foreach (var tf in transformations)
                            {
                                if (tf is not JsonObject tfObj) continue;
                                string tfType = tfObj["Type"]?.GetValue<string>() ?? "";

                                if (tfType == "MoveAndGroup")
                                {
                                    string targetObjName = tfObj["Target"]?.GetValue<string>() ?? "";
                                    var sourceFields = tfObj["SourceFields"]?.AsObject();

                                    if (sourceFields != null && !string.IsNullOrEmpty(targetObjName))
                                    {
                                        JsonObject newObj = new JsonObject();
                                        bool hasAnyField = false;

                                        foreach (var field in sourceFields)
                                        {
                                            string newPropName = field.Key;
                                            string oldPropName = field.Value?.GetValue<string>() ?? "";

                                            if (itemObj.TryGetPropertyValue(oldPropName, out var oldPropValue) && oldPropValue != null)
                                            {
                                                hasAnyField = true;
                                                newObj[newPropName] = oldPropValue.DeepClone();
                                                itemObj.Remove(oldPropName);
                                                var existingKey = itemObj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, oldPropName, StringComparison.OrdinalIgnoreCase));
                                                if (existingKey != null) itemObj.Remove(existingKey);
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
                                    string itemsKey = tfObj["ItemsKey"]?.GetValue<string>() ?? "items";
                                    string recycleKey = tfObj["RecycleKey"]?.GetValue<string>() ?? "recycleBinItems";
                                    string statusField = tfObj["StatusField"]?.GetValue<string>() ?? "Status";
                                    int normalValue = tfObj["NormalValue"]?.GetValue<int>() ?? 0;

                                    if (configRoot.TryGetPropertyValue(itemsKey, out var itemsRoot) && itemsRoot is JsonArray itemsArray)
                                    {
                                        JsonArray recycleArray;
                                        if (configRoot.TryGetPropertyValue(recycleKey, out var recycleRoot) && recycleRoot is JsonArray existingRecycle)
                                        {
                                            recycleArray = existingRecycle;
                                        }
                                        else
                                        {
                                            recycleArray = new JsonArray();
                                            configRoot[recycleKey] = recycleArray;
                                        }

                                        for (int i = itemsArray.Count - 1; i >= 0; i--)
                                        {
                                            if (itemsArray[i] is not JsonObject obj) continue;
                                            int statusValue = normalValue;
                                            if (obj.TryGetPropertyValue(statusField, out var statusNode) && statusNode != null)
                                            {
                                                try { statusValue = statusNode.GetValue<int>(); } catch { statusValue = normalValue; }
                                            }

                                            if (statusValue != normalValue)
                                            {
                                                recycleArray.Add(obj.DeepClone());
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
            catch { return; }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string newJson = configRoot.ToJsonString(options);
                File.WriteAllText(inputPath, newJson);
            }
            catch { }
        }
    }
}
