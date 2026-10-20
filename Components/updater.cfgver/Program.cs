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

            int targetVersion = args.Length >= 3 && args[1] == "--target-version" && int.TryParse(args[2], out int requested) ? requested : 1;
            int startVersion = currentVersion;
            bool migratedAny;
            try { migratedAny = MigrationRules.Apply(configRoot, rulesList, targetVersion); }
            catch (Exception ex) { Log($"MigrationFailed {ex}"); return 11; }
            if (!migratedAny) return 0;


            string backupPath = $"{inputPath}.bak.v{startVersion}.{Guid.NewGuid():N}";
            try
            {
                File.Copy(inputPath, backupPath, true);
            }
            catch (Exception ex) { Log($"BackupFailed info={ex}"); return 8; }

            try
            {
                var serializer = new SerializerBuilder().Build();
                string newYaml = serializer.Serialize(configRoot);
                string temporary = inputPath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(newYaml);
                        file.Write(bytes); file.Flush(true);
                    }
                    File.Replace(temporary, inputPath, null);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
