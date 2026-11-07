using EricGameLauncher;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

internal static class StoragePolicyFeatures
{
    internal static void Run(Action<bool, string> check, string root)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.Windows })
        {
            string directory = Environment.GetFolderPath(folder);
            check(StoragePolicy.RequiresSystemMode(directory), $"protected installation root requires system storage: {folder}");
            check(StoragePolicy.RequiresSystemMode(Path.Combine(directory.ToUpperInvariant(), "EricGameLauncher", "sub", "..")), $"normalized protected child requires system storage: {folder}");
            check(!StoragePolicy.IsProtectedDirectory(directory + "-portable"), $"protected root matching respects directory boundaries: {folder}");
        }

        string install = Path.Combine(root, "storage-policy");
        Directory.CreateDirectory(install);
        check(!StoragePolicy.RequiresSystemMode(install), "writable installation permits storage switching");
        CheckDeniedDirectory(check, install);
        string data = Path.Combine(install, "Data");
        Directory.CreateDirectory(data);
        CheckDeniedDirectory(check, data, install);
        check(!Directory.EnumerateFiles(install, ".egl-write-probe-*", SearchOption.AllDirectories).Any(), "storage write probes leave no files behind");

        var locked = typeof(ConfigService).GetField("_storageModeLocked", BindingFlags.Static | BindingFlags.NonPublic)!;
        var debug = typeof(ConfigService).GetField("_debugModeApplied", BindingFlags.Static | BindingFlags.NonPublic)!;
        var currentPath = typeof(ConfigService).GetProperty(nameof(ConfigService.CurrentDataPath))!;
        object? originalLock = locked.GetValue(null), originalDebug = debug.GetValue(null);
        string originalPath = ConfigService.CurrentDataPath;
        try
        {
            locked.SetValue(null, true);
            check(ConfigService.IsStorageModeLocked, "Core exposes the locked storage policy");
            bool rejected = false;
            try { ConfigService.SwitchStorageMode(false); }
            catch (InvalidOperationException ex) { rejected = ex.Message == Text.Cli("ErrStorageLocked"); }
            check(rejected && ConfigService.CurrentDataPath == originalPath, "Core rejects portable switching before data or mode changes");
            debug.SetValue(null, false);
            typeof(ConfigService).GetMethod("FollowStorageMode", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            check(ConfigService.IsSystemMode, "locked storage follows system mode without consulting the portable preference");
            ConfigService.SwitchStorageMode(true);
            check(ConfigService.IsSystemMode, "requesting the locked system mode is a safe no-op");
        }
        finally
        {
            locked.SetValue(null, originalLock);
            debug.SetValue(null, originalDebug);
            currentPath.SetValue(null, originalPath);
        }
    }

    private static void CheckDeniedDirectory(Action<bool, string> check, string directory, string? install = null)
    {
        var info = new DirectoryInfo(directory);
        byte[] original = info.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var denied = info.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            info.SetAccessControl(denied);
            check(StoragePolicy.RequiresSystemMode(install ?? directory), install == null ? "unwritable custom installation requires system storage" : "unwritable existing Data directory requires system storage");
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
        check(!StoragePolicy.RequiresSystemMode(install ?? directory), "restoring directory permissions permits storage switching");
    }
}
