namespace EricGameLauncher.Testing;

internal static class TestWorkspace
{
    internal static string BaseDirectory => Path.Combine(Path.GetTempPath(), "EricGameLauncher.Tests");

    internal static string NewFixture(string prefix)
    {
        string path = Path.Combine(BaseDirectory, prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void DeleteFixture(string path)
    {
        string resolved = Path.GetFullPath(path);
        string allowed = Path.GetFullPath(BaseDirectory) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture cleanup escaped the temporary test directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
