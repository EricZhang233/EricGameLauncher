using EricGameLauncher;

internal static class IconFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        string executable = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        string textFile = Path.Combine(root, "icon-none.txt");
        File.WriteAllText(textFile, "fixture");

        check(ItemService.SupportsIconExtraction(executable), "icon extraction supports executable files");
        check(ItemService.SupportsIconExtraction(Path.Combine(root, "shortcut.lnk")), "icon extraction supports shortcut files");
        check(ItemService.SupportsIconExtraction("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), "icon extraction supports store app targets");
        check(!ItemService.SupportsIconExtraction(textFile), "icon extraction ignores plain text files");
        check(!ItemService.SupportsIconExtraction("steam://run/570"), "icon extraction ignores protocol launches");

        check(ItemService.SuggestTitle("C:\\Games\\My Game.exe") == "My Game", "title suggestion strips executable extension");
        check(ItemService.SuggestTitle("C:\\Games\\shortcut.lnk") == "shortcut", "title suggestion strips shortcut extension");
        Environment.SetEnvironmentVariable("EGL_ICON_TITLE_DIR", "C:\\Games");
        try { check(ItemService.SuggestTitle("%EGL_ICON_TITLE_DIR%\\Env Game.exe") == "Env Game", "title suggestion expands environment variables"); }
        finally { Environment.SetEnvironmentVariable("EGL_ICON_TITLE_DIR", null); }
        check(ItemService.SuggestTitle("steam://run/570") == null, "title suggestion ignores protocol launches");
        check(ItemService.SuggestTitle("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App") == null, "title suggestion ignores store app paths");
        check(ItemService.SuggestTitle(null) == null && ItemService.SuggestTitle("   ") == null, "title suggestion ignores empty paths");

        if (!File.Exists(executable))
        {
            check(false, "system executable is available for icon extraction");
            return;
        }

        var extracted = ItemService.CreateItem("Icon Async", executable);
        await ItemService.AddItemAsync(extracted);
        string? extractedIcon = ItemService.FindItem(extracted.Id, null)?.IconPath;
        check(!string.IsNullOrEmpty(extractedIcon) && File.Exists(extractedIcon), "add async extracts icon from executable");
        ItemService.RemoveItem(extracted.Id, null, true);

        var noIcon = ItemService.CreateItem("No Icon", textFile);
        await ItemService.AddItemAsync(noIcon);
        check(string.IsNullOrEmpty(ItemService.FindItem(noIcon.Id, null)?.IconPath), "add async keeps non executable target without icon");
        ItemService.RemoveItem(noIcon.Id, null, true);

        var edited = ItemService.CreateItem("Edit Icon", textFile);
        await ItemService.AddItemAsync(edited);
        await ItemService.EditItemAsync(edited, item => item.ExePath = executable);
        check(!string.IsNullOrEmpty(ItemService.FindItem(edited.Id, null)?.IconPath), "edit async fills icon after path change");
        ItemService.RemoveItem(edited.Id, null, true);

        var keepIcon = ItemService.CreateItem("Keep Icon", executable, icon: AppIconService.GetIconPath());
        await ItemService.AddItemAsync(keepIcon);
        string? keptIcon = ItemService.FindItem(keepIcon.Id, null)?.IconPath;
        check(!string.IsNullOrEmpty(keptIcon) && File.Exists(keptIcon), "add async keeps an explicitly selected icon");
        ItemService.RemoveItem(keepIcon.Id, null, true);
    }
}
