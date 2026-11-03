if (args.Length >= 2 && args[0] == "--capture")
{
    using var identity = OperatingSystem.IsWindows() ? System.Security.Principal.WindowsIdentity.GetCurrent() : null;
    string temporary = args[1] + "." + Environment.ProcessId;
    File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(new
    {
        Arguments = args.Skip(2).ToArray(),
        WorkingDirectory = Environment.CurrentDirectory,
        User = Environment.UserName,
        UserSid = OperatingSystem.IsWindows() ? identity?.User?.Value : null,
        IsAdministrator = OperatingSystem.IsWindows() && identity != null && new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)
    }));
    File.Move(temporary, args[1], true);
    return 0;
}
if (args.Length != 1) return 2;
File.WriteAllText(args[0] + "." + Environment.ProcessId, "completed");
return 0;
