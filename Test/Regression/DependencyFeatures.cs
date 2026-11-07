using EricGameLauncher;
using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;

internal static class DependencyFeatures
{
    internal static async Task RunAsync(Action<bool, string> check, string root, string cli)
    {
        var references = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            if (Path.GetFullPath(path).Equals(typeof(DependencyFeatures).Assembly.Location, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var metadata = pe.GetMetadataReader();
            references.Add(metadata.GetString(metadata.GetAssemblyDefinition().Name), metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToArray());
        }
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(new[] { "EricGameLauncher", "EricGameLauncher.Cli" });
        while (pending.TryDequeue(out string? name))
        {
            if (!reachable.Add(name) || !references.TryGetValue(name, out var dependencies)) continue;
            foreach (string dependency in dependencies) pending.Enqueue(dependency);
        }
        check(references.Keys.All(reachable.Contains), "every distributed managed DLL belongs to the launcher dependency closure");
        check(new[] { "Microsoft.WindowsAppRuntime.Bootstrap.dll", "Microsoft.Web.WebView2.Core.dll", "WebView2Loader.dll" }.All(name => File.Exists(Path.Combine(AppContext.BaseDirectory, name))), "native bootstrap and WebView2 dependencies remain available");
        check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll")), "unused Windows App SDK background-task host is not distributed");
        await QuickStartAsync(check, root, cli);
    }

    private static async Task QuickStartAsync(Action<bool, string> check, string root, string cli)
    {
        if (ServiceHost.IsHostRunning()) throw new InvalidOperationException("Quick start fixture requires an idle service-host pipe.");
        bool previous = ConfigService.QuickStart;
        Process? child = null;
        try
        {
            ConfigService.QuickStart = true;
            ConfigService.SaveSettings();
            var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(cli)!, "EricGameLauncher.exe"))
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("-debug");
            start.ArgumentList.Add("-service");
            child = Process.Start(start)!;
            Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
            await SendAsync(child, "WAKE");
            var timer = Stopwatch.StartNew();
            while (!child.HasExited && child.MainWindowHandle == IntPtr.Zero && timer.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(50);
                child.Refresh();
            }
            check(!child.HasExited && child.MainWindowHandle != IntPtr.Zero, "real quick start host starts and wakes a window without UniversalBGTask");
            check(!child.Modules.Cast<ProcessModule>().Any(m => m.ModuleName.Equals("Microsoft.Windows.ApplicationModel.Background.UniversalBGTask.dll", StringComparison.OrdinalIgnoreCase)), "quick start does not load the SDK background-task host indirectly");
            await SendAsync(child, "EXIT");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            check(child.ExitCode == 0, "real quick start host exits normally after its owned pipe receives EXIT");
            await output;
            await error;
        }
        finally
        {
            if (child != null)
            {
                if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
            ConfigService.QuickStart = previous;
            ConfigService.SaveSettings();
        }
    }

    private static async Task SendAsync(Process child, string message)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (child.HasExited) throw new InvalidOperationException($"Quick start fixture exited: {child.ExitCode}");
            using var pipe = new NamedPipeClientStream(".", "EricGameLauncher.ServiceHost.v1", PipeDirection.Out);
            try { await pipe.ConnectAsync(100); }
            catch (TimeoutException) { continue; }
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint owner) || owner != child.Id)
                throw new InvalidOperationException("Service-host pipe does not belong to the fixture process.");
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            await writer.WriteLineAsync(message);
            return;
        }
        throw new TimeoutException("Quick start fixture pipe did not become available.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
