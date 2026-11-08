using EricGameLauncher;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

internal static class ConfigurationFeatures
{
    internal static void Run(Action<bool, string> check, string root)
    {
        string source = Path.Combine(root, "configuration-source");
        string destination = Path.Combine(root, "configuration-destination");
        string backup = Path.Combine(root, "configuration.zip");
        byte[] icon = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6B9sAAAAASUVORK5CYII=");
        string externalIcon = Path.Combine(root, "configuration-icon.png");
        File.WriteAllBytes(externalIcon, icon);
        string externalAppIcon = Path.Combine(root, "configuration-app.ico");
        using (var input = AppIconService.OpenDefaultIconStream())
        using (var output = File.Create(externalAppIcon)) input.CopyTo(output);
        void Use(string directory)
        {
            ConfigService.ApplyDebugMode(directory);
            ConfigService.Reload();
            Text.Load("EN");
        }
        static bool Rejects(Action action)
        { try { action(); return false; } catch (Exception ex) when (ex is IOException or InvalidDataException) { return true; } }
        try
        {
            Use(source);
            ConfigService.AppTitle = "Archive source";
            ConfigService.AppIconPath = externalAppIcon;
            ConfigService.SetWindowBounds(12, 34, 900, 600);
            ConfigService.SaveGitHubToken("ghp_configuration_archive_fixture");
            ConfigService.SetSetting("lang", "EN");
            var first = ItemService.CreateItem("Archive one", @"C:\Archive\One.exe", icon: externalIcon);
            var second = ItemService.CreateItem("Archive two", @"C:\Archive\Two.exe", icon: externalIcon);
            var recycled = ItemService.CreateItem("Archive recycled", @"C:\Archive\Recycled.exe", icon: externalIcon);
            ItemService.AddItem(first);
            ItemService.AddItem(second);
            ItemService.AddItem(recycled);
            ItemService.RemoveItem(recycled.Id, null);
            var exportStarted = DateTimeOffset.UtcNow;
            check(ConfigurationArchive.Export(backup) == 0, "configuration export includes all available icon sources");
            using (var zip = ZipFile.OpenRead(backup))
            {
                check(zip.GetEntry(ConfigurationArchive.IdentifierFile) != null && zip.GetEntry("settings.yaml") != null && zip.GetEntry("items.yaml") != null, "configuration archive contains its identifier, settings and item lists");
                using (var identityStream = zip.GetEntry(ConfigurationArchive.IdentifierFile)!.Open())
                using (var identifier = JsonDocument.Parse(identityStream))
                    check(!identifier.RootElement.TryGetProperty("Files", out _), "configuration identifier does not record a file list");
                check(zip.Entries.Count(x => x.FullName.StartsWith("icons/", StringComparison.Ordinal)) == 2, "configuration archive deduplicates identical item icons and includes the custom application icon");
                using var reader = new StreamReader(zip.GetEntry("settings.yaml")!.Open());
                string yaml = reader.ReadToEnd();
                check(!yaml.Contains("ghp_configuration_archive_fixture", StringComparison.Ordinal) && !yaml.Contains(externalAppIcon, StringComparison.Ordinal), "export retains token encryption and replaces external application icon paths with archive references");
            }
            var package = ConfigurationArchive.Read(backup);
            check(package.Information.OriginalFileName == Path.GetFileName(backup) && package.Information.RuleVersion == 1
                && package.Information.ConfigurationVersion == ConfigService.CurrentConfigVersion && package.Information.GeneratorVersion == AppVersion.DisplayVersion
                && package.Information.GeneratedAt >= exportStarted && package.Information.GeneratedAt <= DateTimeOffset.UtcNow, "archive recognition retains the original filename, format and configuration versions, generator version and creation time");
            var both = ConfigurationSections.Settings | ConfigurationSections.Items;
            Use(destination);
            ConfigService.AppTitle = "Keep destination";
            ConfigService.SetWindowBounds(111, 222, 999, 777);
            ConfigService.SaveSettings();
            var duplicate = ItemService.CreateItem("Keep duplicate", @"c:/archive/ONE.exe");
            ItemService.AddItem(duplicate);
            var collision = ItemService.CreateItem("Keep ID collision", @"C:\Archive\Existing.exe");
            collision.Id = second.Id;
            ItemService.AddItem(collision);
            string itemsPath = ConfigService.ItemsFilePath;
            string settingsPath = ConfigService.SettingsFilePath;
            byte[] itemsBefore = File.ReadAllBytes(itemsPath);
            var settingsPlan = ConfigurationArchive.Preview(ConfigurationSections.Settings);
            check(settingsPlan.HasConflicts, "existing settings require an explicit import policy");
            package.Import(ConfigurationSections.Settings, ConfigurationImportMode.Replace, settingsPlan);
            check(ConfigService.AppTitle == "Archive source" && File.ReadAllBytes(itemsPath).SequenceEqual(itemsBefore), "settings-only import preserves the item list byte for byte");
            check(ConfigService.GitHubToken == "ghp_configuration_archive_fixture" && File.Exists(ConfigService.AppIconPath), "settings-only import restores the encrypted token and relocates the custom application icon");
            ConfigService.AppTitle = "Merge destination";
            ConfigService.SetWindowBounds(111, 222, 999, 777);
            ConfigService.SaveSettings();
            File.WriteAllText(settingsPath, "appTitle: Merge destination\nwindow:\n  y: 222\n  width: 999\n  height: 777\n");
            var merged = package.Import(both, ConfigurationImportMode.Merge, ConfigurationArchive.Preview(both));
            check(merged.Added == 2 && merged.Skipped == 1, "merge skips an equivalent launch target and adds new active and recycled items");
            var active = ConfigService.LoadItems();
            var restoredSecond = active.Single(x => x.Title == second.Title);
            check(active.Any(x => x.Title == collision.Title && x.Id == second.Id) && restoredSecond.Id != second.Id && active.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == active.Count, "ID collisions receive new IDs without replacing existing items");
            var window = ConfigService.GetWindowBounds();
            var mergedSettings = ConfigPersistence.Read<Dictionary<object, object>>(settingsPath);
            check(ConfigService.AppTitle == "Merge destination" && window.Width == 999 && window.X == 12 && mergedSettings["language"].ToString() == "EN", "settings merge keeps existing values and fills missing top-level and nested fields");
            check(File.ReadAllBytes(restoredSecond.IconPath!).SequenceEqual(icon) && Path.GetDirectoryName(restoredSecond.IconPath) == ConfigService.FixedCachePath, "imported item icons use local collision-free filenames with the original bytes");
            check(ConfigService.LoadRecycleBinItems().Single().Title == recycled.Title, "configuration import retains recycle-bin entries");
            var repeated = package.Import(both, ConfigurationImportMode.Merge, ConfigurationArchive.Preview(both));
            check(repeated.Added == 0 && repeated.Skipped == 3, "repeated merge is idempotent across active and recycled entries");
            byte[] settingsBefore = File.ReadAllBytes(settingsPath);
            package.Import(ConfigurationSections.Items, ConfigurationImportMode.Replace, ConfigurationArchive.Preview(ConfigurationSections.Items));
            check(File.ReadAllBytes(settingsPath).SequenceEqual(settingsBefore) && ConfigService.LoadItems().Select(x => x.Title).SequenceEqual(new[] { first.Title, second.Title }), "items-only replacement replaces the selected lists and preserves settings byte for byte");
            var stale = ConfigurationArchive.Preview(both);
            ConfigService.AppTitle = "Changed after confirmation";
            ConfigService.SaveSettings();
            bool changed = false;
            try { package.Import(both, ConfigurationImportMode.Replace, stale); } catch (ConfigurationChangedException) { changed = true; }
            check(changed && ConfigService.AppTitle == "Changed after confirmation", "concurrent configuration changes require renewed confirmation before import");
            settingsBefore = File.ReadAllBytes(settingsPath);
            itemsBefore = File.ReadAllBytes(itemsPath);
            var removedIcons = Directory.GetFiles(ConfigService.FixedCachePath, "import-*").ToDictionary(x => x, File.ReadAllBytes);
            foreach (string path in removedIcons.Keys) File.Delete(path);
            string[] iconsBefore = Directory.GetFiles(ConfigService.FixedCachePath).Order().ToArray();
            check(Rejects(() => package.Import(both, ConfigurationImportMode.Replace, ConfigurationArchive.Preview(both), step => { if (step == "commit") throw new IOException("injected archive commit failure"); })), "configuration import reports an injected transaction failure");
            check(File.ReadAllBytes(settingsPath).SequenceEqual(settingsBefore) && File.ReadAllBytes(itemsPath).SequenceEqual(itemsBefore)
                && Directory.GetFiles(ConfigService.FixedCachePath).Order().SequenceEqual(iconsBefore) && !Directory.Exists(Path.Combine(ConfigService.CurrentDataPath, ".configuration-import")), "failed import rolls back settings, items and newly written icons");
            foreach (var file in removedIcons) File.WriteAllBytes(file.Key, file.Value);
            string transaction = Path.Combine(ConfigService.CurrentDataPath, ".configuration-import");
            Directory.CreateDirectory(Path.Combine(transaction, "original"));
            File.WriteAllBytes(Path.Combine(transaction, "original", "settings.yaml"), settingsBefore);
            File.WriteAllText(Path.Combine(transaction, "journal.json"), JsonSerializer.Serialize(new { Format = "EricGameLauncher.Configuration", Committed = false, Written = new[] { "settings.yaml" }, Originals = new[] { "settings.yaml" } }));
            File.WriteAllText(settingsPath, "interrupted import");
            ConfigService.Reload();
            check(File.ReadAllBytes(settingsPath).SequenceEqual(settingsBefore) && !Directory.Exists(transaction), "the next configuration access recovers an interrupted import before reading data");
            string invalid = Path.Combine(root, "configuration-invalid.zip");
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Create))
            {
                foreach (string name in new[] { "settings.yaml", "items.yaml", "icons/placeholder.png" })
                { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write("fixture"); }
            }
            check(Rejects(() => ConfigurationArchive.Read(invalid)), "unidentified ZIP archives cannot be imported");
            File.Copy(backup, invalid, true);
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Update))
            {
                zip.GetEntry("settings.yaml")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("settings.yaml").Open());
                writer.Write("appTitle: tampered");
            }
            check(ConfigurationArchive.Read(invalid).MissingIcons == 0, "valid edited settings need no file-list updates");
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Update))
            {
                zip.GetEntry("settings.yaml")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("settings.yaml").Open());
                writer.Write("launchMode: unsupported");
            }
            check(Rejects(() => ConfigurationArchive.Read(invalid)), "configuration content is still validated without a file list");
            File.Copy(backup, invalid, true);
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Update))
            { using var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()); writer.Write("untrusted"); }
            check(Rejects(() => ConfigurationArchive.Read(invalid)) && !File.Exists(Path.Combine(root, "outside.txt")), "archive paths are validated before any extraction or configuration writes");
            File.Copy(backup, invalid, true);
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Update))
            { using var writer = new StreamWriter(zip.CreateEntry("ITEMS.YAML").Open()); writer.Write("duplicate"); }
            check(Rejects(() => ConfigurationArchive.Read(invalid)), "configuration archives reject case-colliding payload entries");
            ConfigService.MutateItems((items, _) => items[0].IconPath = Path.Combine(root, "missing-archive-icon.png"));
            check(ConfigurationArchive.Export(Path.Combine(root, "configuration-missing-icon.zip")) == 1, "missing icon files are reported while remaining configuration can still be exported");
            check(ConfigurationArchive.Read(Path.Combine(root, "configuration-missing-icon.zip")).MissingIcons == 1, "archives retain missing-icon information for import feedback");
            check(File.ReadAllBytes(backup).Length > 0 && Rejects(() => ConfigurationArchive.Export(Path.Combine(root, "wrong-extension.yaml"))), "configuration export requires a ZIP filename");
        }
        finally { Use(root); }
    }
}
