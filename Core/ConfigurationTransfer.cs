namespace EricGameLauncher;

public static partial class ConfigService
{
    internal static string ConfigurationDirectory()
    {
        if (!_initialized) Initialize();
        FollowStorageMode();
        return CurrentDataPath;
    }

    internal static void ConfigurationImported()
    {
        LoadConfigData();
        Text.Load(Language);
        if (LoadError != null) throw new InvalidDataException(LoadError);
    }

    internal static void NotifyConfigurationImported()
    {
        try { DataChanged?.Invoke(); }
        catch (Exception ex) { LogService.Write("Config", "Configuration import notification failed", ex); }
    }
}
