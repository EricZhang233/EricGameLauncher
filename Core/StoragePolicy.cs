namespace EricGameLauncher;

internal static class StoragePolicy
{
    internal static bool RequiresSystemMode(string installDirectory)
    {
        if (IsProtectedDirectory(installDirectory)) return true;
        if (!CanWriteDirectory(installDirectory)) return true;
        string data = Path.Combine(installDirectory, "Data");
        return Directory.Exists(data) && !CanWriteDirectory(data);
    }

    internal static bool IsProtectedDirectory(string installDirectory)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        string[] protectedRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        ];
        foreach (string root in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (path.Equals(normalized, StringComparison.OrdinalIgnoreCase) || path.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(directory, ".egl-write-probe-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            LogService.Write("Config", $"Installation storage is not writable: {directory}", ex);
            return false;
        }
    }
}
