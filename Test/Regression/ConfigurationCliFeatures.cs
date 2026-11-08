using EricGameLauncher;
using System.Text.Json;

internal static class ConfigurationCliFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, Func<string[], Task<(int Code, string Output, string Error)>> cli)
    {
        string backup = Path.Combine(ConfigService.CurrentDataPath, "cli configuration backup.zip");
        string originalTitle = ConfigService.AppTitle;
        try
        {
            ConfigService.AppTitle = "CLI archive settings";
            ConfigService.SaveSettings();
            var active = ItemService.CreateItem("CLI archive active", @"C:\EGL-configuration-active.exe");
            var recycled = ItemService.CreateItem("CLI archive recycled", @"C:\EGL-configuration-recycled.exe");
            ItemService.AddItem(active);
            ItemService.AddItem(recycled);
            ItemService.RemoveItem(recycled.Id, null);
            var exported = await cli(["config", "--export", "--path", backup, "--json"]);
            using (var json = JsonDocument.Parse(exported.Output))
                check(exported.Code == 0 && json.RootElement.GetProperty("success").GetBoolean()
                    && json.RootElement.GetProperty("path").GetString() == backup && File.Exists(backup), "CLI configuration export supports paths with spaces and structured results");
            byte[] settingsBefore = File.ReadAllBytes(ConfigService.SettingsFilePath);
            byte[] itemsBefore = File.ReadAllBytes(ConfigService.ItemsFilePath);
            bool Unchanged() => File.ReadAllBytes(ConfigService.SettingsFilePath).SequenceEqual(settingsBefore)
                && File.ReadAllBytes(ConfigService.ItemsFilePath).SequenceEqual(itemsBefore);
            var preview = await cli(["config", "--import", "--path", backup, "--preview", "--json"]);
            using (var json = JsonDocument.Parse(preview.Output))
                check(preview.Code == 0 && json.RootElement.GetProperty("preview").GetBoolean()
                    && json.RootElement.GetProperty("hasConflicts").GetBoolean()
                    && json.RootElement.GetProperty("information").GetProperty("OriginalFileName").GetString() == Path.GetFileName(backup)
                    && Unchanged(), "CLI backup preview reports metadata and conflicts without importing");
            var textPreview = await cli(["config", "--import", "--path", backup, "--preview"]);
            check(textPreview.Code == 0 && textPreview.Output.Contains(Path.GetFileName(backup)) && Unchanged(), "CLI backup preview also supports readable text output");
            var noPolicy = await cli(["config", "--import", "--path", backup, "--json"]);
            using (var json = JsonDocument.Parse(noPolicy.Output))
                check(noPolicy.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean() && Unchanged(), "CLI import cannot overwrite existing configuration without an explicit policy");
            foreach (string[] invalid in new[]
            {
                new[] {"--export", "--import", "--path", backup},
                new[] {"--export", "--path", backup, "--items"},
                new[] {"--import", "--path", backup, "--replace", "--merge"},
                new[] {"--import", "--path", backup, "--preview", "--replace"},
                new[] {"--path", backup},
                new[] {"--export", "--path"}
            })
            {
                var rejected = await cli(new[] {"config"}.Concat(invalid).Append("--json").ToArray());
                using var json = JsonDocument.Parse(rejected.Output);
                check(rejected.Code != 0 && !json.RootElement.GetProperty("success").GetBoolean() && Unchanged(), "CLI configuration rejects invalid options without modifying data: " + string.Join(' ', invalid));
            }
            ConfigService.AppTitle = "CLI destination settings";
            ConfigService.SaveSettings();
            ItemService.AddItem(ItemService.CreateItem("CLI destination extra", @"C:\EGL-configuration-extra.exe"));
            settingsBefore = File.ReadAllBytes(ConfigService.SettingsFilePath);
            var itemsOnly = await cli(["config", "--import", "--path", backup, "--items", "--replace", "--json"]);
            check(itemsOnly.Code == 0 && ConfigService.LoadItems().Single().Title == active.Title
                && ConfigService.LoadRecycleBinItems().Single().Title == recycled.Title
                && File.ReadAllBytes(ConfigService.SettingsFilePath).SequenceEqual(settingsBefore), "CLI item-only replacement restores library and recycle bin while preserving settings");
            itemsBefore = File.ReadAllBytes(ConfigService.ItemsFilePath);
            var settingsOnly = await cli(["config", "--import", "--path", backup, "--settings", "--replace", "--json"]);
            ConfigService.Reload();
            using (var json = JsonDocument.Parse(settingsOnly.Output))
                check(settingsOnly.Code == 0 && ConfigService.AppTitle == "CLI archive settings"
                    && json.RootElement.GetProperty("quickStartSynchronized").GetBoolean()
                    && json.RootElement.GetProperty("restartRecommended").GetBoolean()
                    && File.ReadAllBytes(ConfigService.ItemsFilePath).SequenceEqual(itemsBefore), "CLI settings-only replacement preserves items and reports settings synchronization");
            ConfigService.AppTitle = "CLI preserved settings";
            ConfigService.SaveSettings();
            ItemService.AddItem(ItemService.CreateItem("CLI retained extra", @"C:\EGL-configuration-retained.exe"));
            var merged = await cli(["config", "--import", "--path", backup, "--merge", "--json"]);
            ConfigService.Reload();
            using (var json = JsonDocument.Parse(merged.Output))
                check(merged.Code == 0 && ConfigService.AppTitle == "CLI preserved settings"
                    && json.RootElement.GetProperty("added").GetInt32() == 0 && json.RootElement.GetProperty("skipped").GetInt32() == 2
                    && ConfigService.LoadItems().Count == 2 && ConfigService.LoadRecycleBinItems().Count == 1, "CLI merge preserves existing settings and items and skips duplicate active and recycled targets");
            var repeated = await cli(["config", "--import", "--path", backup, "--items", "--merge", "--json"]);
            using (var json = JsonDocument.Parse(repeated.Output))
                check(repeated.Code == 0 && json.RootElement.GetProperty("added").GetInt32() == 0 && json.RootElement.GetProperty("skipped").GetInt32() == 2, "CLI repeated merge does not duplicate imported targets");
            var help = await cli(["config", "--help"]);
            var skill = await cli(["skill"]);
            check(help.Code == 0 && help.Output.Contains("--preview") && help.Output.Contains("--merge")
                && skill.Code == 0 && skill.Output.Contains("config --export --path"), "CLI embedded help and integration guide document configuration operations");
            check((await cli(["config", "--import", "--path", backup + ".missing", "--replace", "--json"])).Code != 0, "CLI missing backup reports failure");
        }
        finally
        {
            ConfigService.Reload();
            ConfigService.MutateItems((items, recycled) => { items.Clear(); recycled.Clear(); });
            ConfigService.AppTitle = originalTitle;
            ConfigService.SaveSettings();
            if (File.Exists(backup)) File.Delete(backup);
        }
    }
}
