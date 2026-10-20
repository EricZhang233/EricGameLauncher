using System.Diagnostics;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace EricGameLauncher.Tests;

public sealed class MachineTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public Task ProductionCliUpdaterAndLogging() => Run("Integration");

    [Fact]
    [Trait("Category", "Contracts")]
    public Task TestRunnerAndReleaseContracts() => Run("Contracts");

    [Fact]
    [Trait("Category", "Packaging")]
    public Task ReleasePackageAndSingleLifecycleSmoke() => Run("Packaging");

    private async Task Run(string area)
    {
        string root = typeof(MachineTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(x => x.Key == "TestRoot").Value!;
        var start = new ProcessStartInfo("pwsh.exe")
        {
            WorkingDirectory = Path.GetFullPath(Path.Combine(root, "..")),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "Scripts", "validate.ps1"), "-Area", area }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(35));
            output.WriteLine(await stdout);
            output.WriteLine(await stderr);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
}
