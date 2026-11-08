using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EricGameLauncher;

[Flags]
public enum ConfigurationSections { Settings = 1, Items = 2 }
public enum ConfigurationImportMode { Replace, Merge }
public sealed record ConfigurationImportPlan(string Directory, string Fingerprint, bool HasConflicts);
public sealed record ConfigurationImportResult(int Added, int Skipped);
public sealed record ConfigurationBackupInfo(string OriginalFileName, int RuleVersion, int ConfigurationVersion, string GeneratorVersion, DateTimeOffset GeneratedAt);
public sealed class ConfigurationChangedException() : IOException(Text.T("ConfigArchive_Changed"));

public sealed class ConfigurationArchive
{
    public const string IdentifierFile = "EricGameLauncher.config.json";
    private const string Format = "EricGameLauncher.Configuration";
    private const string TransactionFolder = ".configuration-import";
    private const long MaximumFileSize = 64 * 1024 * 1024;
    private const long MaximumTotalSize = 512 * 1024 * 1024;
    private static readonly Regex IconName = new(@"^icons/[a-f0-9]{64}\.[a-z0-9]{1,12}$", RegexOptions.CultureInvariant);
    private static readonly Regex ImportedIconName = new(@"^ico/import-[a-f0-9]{64}\.[a-z0-9]{1,12}$", RegexOptions.CultureInvariant);
    private static readonly ISerializer ArchiveSerializer = new SerializerBuilder().WithTypeConverter(new TimestampYaml()).DisableAliases().Build();
    private static readonly IDeserializer ArchiveDeserializer = new DeserializerBuilder().WithTypeConverter(new TimestampYaml())
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().WithDuplicateKeyChecking().WithMaximumRecursion(64).Build();
    private readonly Dictionary<string, byte[]> files;
    public int MissingIcons { get; }
    public ConfigurationBackupInfo Information { get; }

    private sealed class Manifest
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public int ConfigVersion { get; set; }
        public string AppVersion { get; set; } = "";
        public DateTimeOffset ExportedAt { get; set; }
        public int MissingIcons { get; set; }
    }

    private sealed class Journal
    {
        public string Format { get; set; } = "";
        public bool Committed { get; set; }
        public List<string> Written { get; set; } = [];
        public List<string> Originals { get; set; } = [];
    }

    private ConfigurationArchive(Dictionary<string, byte[]> files, Manifest manifest, string originalFileName)
    {
        this.files = files;
        MissingIcons = manifest.MissingIcons;
        Information = new(originalFileName, manifest.Version, manifest.ConfigVersion, manifest.AppVersion, manifest.ExportedAt);
    }

    private static InvalidDataException Invalid() => new(Text.T("ConfigArchive_Invalid"));
    private static byte[] Encode<T>(T value) => Encoding.UTF8.GetBytes(ArchiveSerializer.Serialize(value!));
    private static T Decode<T>(byte[] bytes)
    {
        string yaml = Encoding.UTF8.GetString(bytes);
        var parser = new YamlDotNet.Core.Parser(new StringReader(yaml));
        int events = 0;
        while (parser.MoveNext())
            if (++events > 1000000 || parser.Current is YamlDotNet.Core.Events.AnchorAlias) throw Invalid();
        return ArchiveDeserializer.Deserialize<T>(yaml) ?? throw Invalid();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static ConfigurationArchive Read(string path)
    {
        using var operation = LogService.StartOperation("Config", "ReadConfigurationArchive");
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count is < 3 or > 50000) throw Invalid();
            var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                string name = entry.FullName;
                if (name != IdentifierFile && name != "settings.yaml" && name != "items.yaml" && !IconName.IsMatch(name)) throw Invalid();
                long limit = name == IdentifierFile ? 8 * 1024 * 1024 : MaximumFileSize;
                if (entry.Length < 1 || entry.Length > limit || (total += entry.Length) > MaximumTotalSize || contents.ContainsKey(name)) throw Invalid();
                using var stream = entry.Open();
                using var output = new MemoryStream();
                byte[] buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer)) != 0)
                {
                    if (output.Length + read > entry.Length) throw Invalid();
                    output.Write(buffer, 0, read);
                }
                if (output.Length != entry.Length) throw Invalid();
                contents.Add(name, output.ToArray());
            }
            if (!contents.Remove(IdentifierFile, out var identity)) throw Invalid();
            var manifest = JsonSerializer.Deserialize<Manifest>(identity) ?? throw Invalid();
            if (manifest.Format != Format || manifest.Version != 1 || manifest.ConfigVersion != ConfigService.CurrentConfigVersion || manifest.MissingIcons < 0) throw Invalid();
            var package = new ConfigurationArchive(contents, manifest, Path.GetFileName(path));
            package.Validate();
            return package;
        }
        catch (Exception ex) when (ex is not IOException && ex is not UnauthorizedAccessException && ex is not InvalidDataException)
        { throw new InvalidDataException(Text.T("ConfigArchive_Invalid"), ex); }
    }

    private void Validate()
    {
        if (!files.TryGetValue("settings.yaml", out var settingsBytes) || !files.TryGetValue("items.yaml", out var itemsBytes)) throw Invalid();
        var settings = Decode<AppSettings>(settingsBytes);
        var items = Decode<ItemsData>(itemsBytes);
        ValidateItems(items);
        if (settings.Window == null || settings.LaunchMode is not ("single" or "double") || settings.UpdateChannel is not ("stable" or "latest")
            || settings.Language is not ("system" or "EN" or "Zh-CN")) throw Invalid();
        void CheckIcon(string? icon)
        {
            if (!string.IsNullOrEmpty(icon) && (!IconName.IsMatch(icon) || !files.ContainsKey(icon))) throw Invalid();
        }
        CheckIcon(settings.AppIconPath);
        foreach (var item in items.Items.Concat(items.RecycleBinItems)) CheckIcon(item.Icon);
        foreach (var file in files.Where(x => IconName.IsMatch(x.Key)))
            if (Path.GetFileNameWithoutExtension(file.Key) != Hash(file.Value)) throw Invalid();
    }

    private static void ValidateItems(ItemsData items)
    {
        if (items.Version != ConfigService.CurrentConfigVersion || items.Items == null || items.RecycleBinItems == null) throw Invalid();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Items)
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Actions?.Main == null || item.Status != 0) throw Invalid();
        ids.Clear();
        foreach (var item in items.RecycleBinItems)
            if (item == null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.RecycleId) || !ids.Add(item.RecycleId) || item.Actions?.Main == null) throw Invalid();
    }

    public static int Export(string destination)
    {
        using var operation = LogService.StartOperation("Config", "ExportConfigurationArchive");
        if (!Path.GetExtension(destination).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Text.T("ConfigArchive_ZipRequired"));
        Dictionary<string, byte[]> contents = new(StringComparer.OrdinalIgnoreCase);
        int missing = 0;
        long iconBytes = 0;
        lock (ConfigPersistence.Gate)
        {
            string root = ConfigService.ConfigurationDirectory();
            using var lease = ConfigPersistence.Enter(root);
            ConfigService.SaveAll();
            if (ConfigService.LoadError != null || ConfigService.RequiresMigration) throw new InvalidDataException(ConfigService.LoadError ?? Text.T("DataFile_MigrationRequired"));
            var settings = ConfigPersistence.Read<AppSettings>(Path.Combine(root, "settings.yaml"));
            var items = ConfigPersistence.Read<ItemsData>(Path.Combine(root, "items.yaml"));
            ValidateItems(items);
            string? StoreIcon(string? reference)
            {
                if (string.IsNullOrWhiteSpace(reference)) return null;
                string expanded = Environment.ExpandEnvironmentVariables(reference);
                string source = Path.IsPathRooted(expanded) ? expanded
                    : expanded.StartsWith("ico/", StringComparison.OrdinalIgnoreCase) || expanded.StartsWith("ico\\", StringComparison.OrdinalIgnoreCase) || (!expanded.Contains('/') && !expanded.Contains('\\'))
                        ? Path.Combine(root, "ico", Path.GetFileName(expanded)) : Path.GetFullPath(expanded);
                if (!File.Exists(source)) { missing++; return null; }
                if (new FileInfo(source).Length > MaximumFileSize) throw new InvalidDataException(Text.T("ConfigArchive_IconTooLarge"));
                byte[] bytes = File.ReadAllBytes(source);
                if (bytes.Length == 0 || bytes.LongLength > MaximumFileSize) throw new InvalidDataException(Text.T("ConfigArchive_IconTooLarge"));
                string extension = Path.GetExtension(source).TrimStart('.').ToLowerInvariant();
                if (!Regex.IsMatch(extension, "^[a-z0-9]{1,12}$", RegexOptions.CultureInvariant)) extension = "png";
                string name = "icons/" + Hash(bytes) + "." + extension;
                if (contents.TryAdd(name, bytes)) iconBytes += bytes.LongLength;
                if (iconBytes > MaximumTotalSize) throw Invalid();
                return name;
            }
            foreach (var item in items.Items.Concat(items.RecycleBinItems)) item.Icon = StoreIcon(item.Icon);
            if (!string.IsNullOrWhiteSpace(settings.AppIconPath))
            {
                string custom = Environment.ExpandEnvironmentVariables(settings.AppIconPath);
                int index = 0;
                if (!File.Exists(custom) && custom.LastIndexOf(',') is int comma && comma > 0 && int.TryParse(custom[(comma + 1)..], out index)) custom = custom[..comma];
                using var icon = IconHelper.ExtractLargestIcon(custom, index);
                if (icon != null)
                {
                    using var output = new MemoryStream();
                    icon.Save(output);
                    byte[] bytes = output.ToArray();
                    settings.AppIconPath = "icons/" + Hash(bytes) + ".ico";
                    if (contents.TryAdd(settings.AppIconPath, bytes)) iconBytes += bytes.LongLength;
                    if (iconBytes > MaximumTotalSize) throw Invalid();
                }
                else { settings.AppIconPath = StoreIcon(Path.GetFullPath(custom)) ?? ""; }
            }
            contents.Add("settings.yaml", Encode(settings));
            contents.Add("items.yaml", Encode(items));
        }
        if (contents.Count > 49999 || contents.Any(x => x.Value.Length == 0 || x.Value.LongLength > MaximumFileSize) || contents.Sum(x => (long)x.Value.Length) > MaximumTotalSize) throw Invalid();
        var manifest = new Manifest { Format = Format, Version = 1, ConfigVersion = ConfigService.CurrentConfigVersion, AppVersion = AppVersion.DisplayVersion,
            ExportedAt = DateTimeOffset.UtcNow, MissingIcons = missing };
        new ConfigurationArchive(contents, manifest, Path.GetFileName(destination)).Validate();
        string temporary = Path.GetFullPath(destination) + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in contents.Append(new KeyValuePair<string, byte[]>(IdentifierFile, JsonSerializer.SerializeToUtf8Bytes(manifest))))
                {
                    using var output = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
                    output.Write(file.Value);
                }
            }
            File.Move(temporary, Path.GetFullPath(destination), true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return missing;
    }

    private static void CheckSections(ConfigurationSections sections)
    {
        if (sections == 0 || (sections & ~(ConfigurationSections.Settings | ConfigurationSections.Items)) != 0) throw new ArgumentOutOfRangeException(nameof(sections));
    }

    private static string Fingerprint(string root, ConfigurationSections sections)
    {
        string Mark(string name) => File.Exists(Path.Combine(root, name)) ? Hash(File.ReadAllBytes(Path.Combine(root, name))) : "missing";
        return ((sections & ConfigurationSections.Settings) != 0 ? Mark("settings.yaml") : "") + ":" + ((sections & ConfigurationSections.Items) != 0 ? Mark("items.yaml") : "");
    }

    public static ConfigurationImportPlan Preview(ConfigurationSections sections)
    {
        using var operation = LogService.StartOperation("Config", "PreviewConfigurationImport");
        CheckSections(sections);
        lock (ConfigPersistence.Gate)
        {
            string root = ConfigService.ConfigurationDirectory();
            using var lease = ConfigPersistence.Enter(root);
            return new(root, Fingerprint(root, sections),
                ((sections & ConfigurationSections.Settings) != 0 && File.Exists(Path.Combine(root, "settings.yaml")))
                || ((sections & ConfigurationSections.Items) != 0 && File.Exists(Path.Combine(root, "items.yaml"))));
        }
    }

    public ConfigurationImportResult Import(ConfigurationSections sections, ConfigurationImportMode mode, ConfigurationImportPlan plan, Action<string>? checkpoint = null)
    {
        using var operation = LogService.StartOperation("Config", "ImportConfigurationArchive");
        CheckSections(sections);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        int added = 0, skipped = 0;
        lock (ConfigPersistence.Gate)
        {
            string root = ConfigService.ConfigurationDirectory();
            using var lease = ConfigPersistence.Enter(root);
            if (!root.Equals(plan.Directory, StringComparison.OrdinalIgnoreCase) || Fingerprint(root, sections) != plan.Fingerprint) throw new ConfigurationChangedException();
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string? RestoreIcon(string? reference)
            {
                if (string.IsNullOrEmpty(reference)) return null;
                string relative = "ico/import-" + Path.GetFileName(reference);
                writes.TryAdd(relative, files[reference]);
                return Path.GetFileName(relative);
            }
            if ((sections & ConfigurationSections.Settings) != 0)
            {
                var incoming = Decode<AppSettings>(files["settings.yaml"]);
                byte[] settings = Encode(incoming);
                string path = Path.Combine(root, "settings.yaml");
                if (mode == ConfigurationImportMode.Merge && File.Exists(path))
                {
                    _ = ConfigPersistence.Read<AppSettings>(path);
                    var existing = Decode<Dictionary<object, object>>(File.ReadAllBytes(path));
                    var proposed = Decode<Dictionary<object, object>>(settings);
                    MergeMissing(existing, proposed);
                    settings = Encode(existing);
                }
                var values = Decode<Dictionary<object, object>>(settings);
                if (values.TryGetValue("appIconPath", out var appIcon) && appIcon is string reference && IconName.IsMatch(reference))
                    values["appIconPath"] = Path.Combine(root, "ico", RestoreIcon(reference)!);
                settings = Encode(values);
                writes.Add("settings.yaml", settings);
            }
            if ((sections & ConfigurationSections.Items) != 0)
            {
                var incoming = Decode<ItemsData>(files["items.yaml"]);
                var destination = mode == ConfigurationImportMode.Merge ? ConfigPersistence.Read<ItemsData>(Path.Combine(root, "items.yaml")) : new ItemsData();
                ValidateItems(destination);
                var ids = destination.Items.Concat(destination.RecycleBinItems).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var recycleIds = destination.RecycleBinItems.Select(x => x.RecycleId!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var paths = destination.Items.Concat(destination.RecycleBinItems).Select(x => PathHashHelper.PathKey(x.Actions?.Main?.Path)).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
                void Add(IEnumerable<AppItemDto> source, List<AppItemDto> target, bool recycled)
                {
                    foreach (var item in source)
                    {
                        string key = PathHashHelper.PathKey(item.Actions?.Main?.Path);
                        if (mode == ConfigurationImportMode.Merge && key.Length > 0 && !paths.Add(key)) { skipped++; continue; }
                        if (mode == ConfigurationImportMode.Merge)
                        {
                            while (!ids.Add(item.Id)) item.Id = PathHashHelper.NewRandomId();
                            if (recycled) while (!recycleIds.Add(item.RecycleId!)) item.RecycleId = PathHashHelper.NewRandomId();
                        }
                        item.Icon = RestoreIcon(item.Icon);
                        if (!recycled) { item.Status = 0; item.DeletedAt = null; item.RecycleId = null; }
                        target.Add(item); added++;
                    }
                }
                Add(incoming.Items, destination.Items, false);
                Add(incoming.RecycleBinItems, destination.RecycleBinItems, true);
                writes.Add("items.yaml", Encode(destination));
            }
            Apply(root, writes, checkpoint);
            ConfigService.ConfigurationImported();
        }
        ConfigService.NotifyConfigurationImported();
        return new(added, skipped);
    }

    private static void MergeMissing(Dictionary<object, object> existing, Dictionary<object, object> incoming)
    {
        foreach (var entry in incoming)
        {
            if (!existing.TryGetValue(entry.Key, out var current)) existing.Add(entry.Key, entry.Value);
            else if (current is Dictionary<object, object> left && entry.Value is Dictionary<object, object> right) MergeMissing(left, right);
        }
    }

    private static string Target(string root, string relative)
    {
        if (relative is not ("settings.yaml" or "items.yaml") && !ImportedIconName.IsMatch(relative)) throw Invalid();
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        string parent = Path.GetDirectoryName(path)!;
        if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        return path;
    }

    private static void Apply(string root, Dictionary<string, byte[]> writes, Action<string>? checkpoint)
    {
        string transaction = Path.Combine(root, TransactionFolder);
        Directory.CreateDirectory(transaction);
        var journal = new Journal { Format = Format };
        void Save() => AtomicFile.Write(Path.Combine(transaction, "journal.json"), JsonSerializer.Serialize(journal));
        Save();
        try
        {
            foreach (var file in writes)
            {
                string target = Target(root, file.Key);
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(file.Value)) continue;
                if (File.Exists(target))
                {
                    string backup = Path.Combine(transaction, "original", file.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup);
                    journal.Originals.Add(file.Key);
                }
                journal.Written.Add(file.Key);
                Save();
                checkpoint?.Invoke("write:" + file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string staged = Path.Combine(transaction, "staged");
                using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { output.Write(file.Value); output.Flush(true); }
                File.Move(staged, target, true);
            }
            checkpoint?.Invoke("commit");
            journal.Committed = true;
            Save();
        }
        catch { Recover(root); throw; }
        Directory.Delete(transaction, true);
    }

    internal static void Recover(string root)
    {
        string transaction = Path.Combine(root, TransactionFolder);
        if (!Directory.Exists(transaction)) return;
        if ((File.GetAttributes(transaction) & FileAttributes.ReparsePoint) != 0) throw Invalid();
        string journalPath = Path.Combine(transaction, "journal.json");
        if (!File.Exists(journalPath))
        {
            if (Directory.EnumerateFileSystemEntries(transaction).Any()) throw Invalid();
            Directory.Delete(transaction);
            return;
        }
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? throw Invalid();
        if (journal.Format != Format || journal.Written == null || journal.Originals == null) throw Invalid();
        foreach (string relative in journal.Written.Concat(journal.Originals)) _ = Target(root, relative);
        if (!journal.Committed)
            foreach (string relative in journal.Written.AsEnumerable().Reverse())
            {
                string target = Target(root, relative);
                if (journal.Originals.Contains(relative)) File.Copy(Path.Combine(transaction, "original", relative), target, true);
                else if (File.Exists(target)) File.Delete(target);
            }
        Directory.Delete(transaction, true);
    }
}
