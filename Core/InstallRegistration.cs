using Microsoft.Win32;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace EricGameLauncher.Installation;

public sealed record InstallRecord(string Directory, string Id, string OwnerSid, string DesktopShortcut, string StartMenuShortcut);

[SupportedOSPlatform("windows")]
public static class InstallRegistration
{
    public const string ProductName = "EricGameLauncher";
    public const string ProductId = "EricSoft.EricGameLauncher";
    public const string UninstallerName = "Uninstall.exe";
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EricSoft.EricGameLauncher";
    public const string AppPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\EricGameLauncher.exe";
    public static string CurrentUserSid => WindowsIdentity.GetCurrent().User!.Value;
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "EricSoft", ProductName);

    public static InstallRecord? Find(string directory, string? ownerSid = null)
    {
        using var user = UserHive(ownerSid ?? CurrentUserSid, false);
        using var key = user.OpenSubKey(RegistryPath);
        if (key?.GetValue("InstallLocation") is not string registered || key.GetValue("InstallId") is not string id || !Guid.TryParseExact(id, "N", out _)
            || key.GetValue("OwnerSid") as string != (ownerSid ?? CurrentUserSid)) return null;
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!root.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(registered)), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(root, UninstallerName)) || !File.Exists(Path.Combine(root, ProductName + ".exe"))) return null;
        return new InstallRecord(root, id, ownerSid ?? CurrentUserSid, key.GetValue("DesktopShortcut") as string ?? "", key.GetValue("StartMenuShortcut") as string ?? "");
    }

    public static string? RegisteredDirectory(string? ownerSid = null)
    {
        using var user = UserHive(ownerSid ?? CurrentUserSid, false);
        using var key = user.OpenSubKey(RegistryPath);
        return key?.GetValue("InstallLocation") as string;
    }

    private static RegistryKey UserHive(string sid, bool writable)
    {
        _ = new SecurityIdentifier(sid);
        return sid == CurrentUserSid ? Registry.CurrentUser : Registry.Users.OpenSubKey(sid, writable) ?? throw new InvalidOperationException("INSTALL_USER_HIVE_UNAVAILABLE");
    }
}
