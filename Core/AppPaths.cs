using System.Reflection;

namespace EricGameLauncher;

internal static class AppPaths
{
    internal static string InstallDirectory
    {
        get
        {
            string? executable = Environment.ProcessPath;
            string? entryName = Assembly.GetEntryAssembly()?.GetName().Name;
            return executable != null && string.Equals(Path.GetFileNameWithoutExtension(executable), entryName, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(executable)!
                : AppContext.BaseDirectory;
        }
    }
}
