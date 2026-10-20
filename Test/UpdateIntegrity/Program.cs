using EricGameLauncher;
using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

int checks = 0;
void Assert(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

byte[] payload;
using (var buffer = new MemoryStream())
{
    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
    using (var writer = new StreamWriter(zip.CreateEntry("EricGameLauncher.exe").Open()))
        writer.Write("test fixture; never executed");
    payload = buffer.ToArray();
}
string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(payload));
Assert(UpdatePackageIntegrity.IsValidDigest(digest), "GitHub SHA256 metadata accepted");
Assert(await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(payload), digest.ToLowerInvariant()), "matching bytes accepted");
Assert(await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(payload), digest.ToUpperInvariant()), "digest is case insensitive");
byte[] tampered = (byte[])payload.Clone();
tampered[^1] ^= 1;
Assert(!await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(tampered), digest), "same-length tampering rejected");
Assert(!await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(payload[..^1]), digest), "truncated download rejected");
foreach (string? invalid in new[] { null, "", digest[7..], "sha1:" + digest[7..], digest[..^1], digest + "0", "sha256:" + new string('z', 64) })
    Assert(!UpdatePackageIntegrity.IsValidDigest(invalid), $"malformed metadata rejected ({invalid?.Length.ToString() ?? "null"})");

if (args.Length != 1) throw new ArgumentException("Pass the built updater.main.exe path to run updater integration checks.");
string updaterSource = Path.GetFullPath(args[0]);
string fixtureRoot = EricGameLauncher.Testing.TestWorkspace.NewFixture("EricGameLauncher.UpdateIntegrity.");
Directory.CreateDirectory(fixtureRoot);
try
{
    string updaterDir = Path.Combine(fixtureRoot, "updater");
    Directory.CreateDirectory(updaterDir);
    foreach (string file in Directory.GetFiles(Path.GetDirectoryName(updaterSource)!))
        File.Copy(file, Path.Combine(updaterDir, Path.GetFileName(file)));
    await File.WriteAllTextAsync(Path.Combine(updaterDir, "updater.main.tasks.yaml"), "[]");

    await CheckUpdaterAsync("mismatch", "sha256:" + new string('0', 64), "ERROR HASH_MISMATCH", 200);
    await CheckUpdaterAsync("missing digest", "", null, 200);
    await CheckUpdaterAsync("HTTP failure", digest, "ERROR UPDATE_FAILED", 500);
    await CheckUpdaterAsync("verified", digest, "READY", 200);

    Assert(await RunModeAsync("--run-cmd", "exit /b 0", "--wait") == 0, "explicit command mode executes successfully");
    Assert(await RunModeAsync("--run-cmd", "exit /b 7", "--wait") == 7, "explicit command mode preserves failure exit code");
    string batchPath = Path.Combine(fixtureRoot, "fixture command.bat");
    await File.WriteAllTextAsync(batchPath, "@exit /b 9\r\n");
    Assert(await RunModeAsync("--run-bat", batchPath, "--wait") == 9, "batch mode accepts spaced paths and preserves exit code");
    Assert(await RunModeAsync("--run-bat", batchPath + ".missing", "--wait") != 0, "missing batch file reports failure");
    Assert(await RunModeAsync("--run-cmd", "", "--wait") != 0, "empty command reports failure");
    Assert(await RunModeAsync("--run-cmd") != 0, "missing command argument reports failure");
    string taskPath = Path.Combine(updaterDir, "updater.main.tasks.yaml");
    await File.WriteAllTextAsync(taskPath, "tasks: [broken");
    Assert(await RunModeAsync("--tasks") != 0, "malformed explicit task file reports failure");
    await File.WriteAllTextAsync(taskPath, "- Type: unsupported\n");
    Assert(await RunModeAsync("--tasks") != 0, "unknown explicit task type reports failure");
    await File.WriteAllTextAsync(taskPath, "- Type: cmd\n  Command: exit /b 6\n  Wait: true\n- Type: cmd\n  Command: exit /b 0\n  Wait: true\n");
    Assert(await RunModeAsync("--tasks") == 6, "task sequence stops on first failure without masking its exit code");
    File.Delete(taskPath);
    Assert(await RunModeAsync("--tasks") != 0, "missing explicit task file reports failure");

    async Task<int> RunModeAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(updaterDir, Path.GetFileName(updaterSource)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = updaterDir
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Updater did not start");
        Task<string> output = child.StandardOutput.ReadToEndAsync(), error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
            return child.ExitCode;
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
        }
    }

    async Task CheckUpdaterAsync(string label, string expectedDigest, string? expectedSignal, int status)
    {
        string caseRoot = Path.Combine(fixtureRoot, label.Replace(' ', '-'));
        string installDir = Path.Combine(caseRoot, "install");
        string tempDir = Path.Combine(caseRoot, "temp");
        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(tempDir);
        string sentinel = Path.Combine(installDir, "original.txt");
        await File.WriteAllTextAsync(sentinel, "original installation");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string pipeName = "EricGameLauncher.IntegrityTest." + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        Task connection = pipe.WaitForConnectionAsync(timeout.Token);
        Task serve = ServeAsync();
        var start = new ProcessStartInfo(Path.Combine(updaterDir, Path.GetFileName(updaterSource)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = updaterDir
        };
        start.Environment["TEMP"] = tempDir;
        start.Environment["TMP"] = tempDir;
        foreach (string argument in new[] { installDir, $"http://127.0.0.1:{port}/update.zip", "--sha256", expectedDigest,
            "--verification-error", "fixture verification failed", "--pipe-name", pipeName, "--main-pid", Environment.ProcessId.ToString() })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Updater did not start");
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        try
        {
            if (expectedSignal == null)
            {
                await child.WaitForExitAsync(timeout.Token);
                Assert(child.ExitCode != 0 && !connection.IsCompletedSuccessfully, label + ": rejected before download");
            }
            else
            {
                await connection;
                using var reader = new StreamReader(pipe);
                string? signal;
                do
                {
                    signal = await reader.ReadLineAsync(timeout.Token);
                } while (signal != null && signal.StartsWith("DOWNLOAD ", StringComparison.Ordinal));
                if (signal != expectedSignal)
                {
                    await child.WaitForExitAsync(timeout.Token);
                    Console.WriteLine(await stdout);
                    Console.WriteLine(await stderr);
                }
                Assert(signal == expectedSignal, $"{label}: expected {expectedSignal}, received {signal}");
                await serve;
                if (expectedSignal == "READY")
                {
                    Assert(!child.HasExited, "verified updater waits for parent before installing");
                    string packagePath = Directory.GetFiles(tempDir, "update_*.zip", SearchOption.AllDirectories).Single();
                    bool writeBlocked = false;
                    try { using var writable = File.OpenWrite(packagePath); }
                    catch (IOException) { writeBlocked = true; }
                    Assert(writeBlocked, "verified archive remains locked against modification");
                }
                else
                {
                    await child.WaitForExitAsync(timeout.Token);
                    Assert(child.ExitCode != 0, label + ": failure exit code");
                    if (expectedSignal == "ERROR HASH_MISMATCH")
                        Assert((await stderr).Contains("fixture verification failed"), "updater displays verification failure");
                    Assert(Directory.GetFiles(tempDir, "update_*.zip", SearchOption.AllDirectories).Length == 0,
                        label + ": downloaded package cleaned up");
                }
            }
            Assert(await File.ReadAllTextAsync(sentinel) == "original installation" && Directory.GetFiles(installDir).Length == 1,
                label + ": installation untouched");
            Assert(!Directory.Exists(Path.Combine(installDir, "._update_staging")), label + ": no premature extraction");
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            Assert(!(await stdout).Contains("[2/4]"), label + ": no launcher shutdown");
            timeout.Cancel();
            try { await serve; } catch (OperationCanceledException) { }
        }

        async Task ServeAsync()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            using NetworkStream stream = client.GetStream();
            var request = new byte[8192];
            int used = 0;
            while (used < request.Length)
            {
                int count = await stream.ReadAsync(request.AsMemory(used), timeout.Token);
                if (count == 0) break;
                used += count;
                if (Encoding.ASCII.GetString(request, 0, used).Contains("\r\n\r\n")) break;
            }
            byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token);
            if (status == 200) await stream.WriteAsync(payload, timeout.Token);
        }
    }
}
finally
{
    string resolvedFixture = Path.GetFullPath(fixtureRoot);
    string allowedPrefix = Path.Combine(EricGameLauncher.Testing.TestWorkspace.BaseDirectory, "EricGameLauncher.UpdateIntegrity.");
    if (!resolvedFixture.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Fixture cleanup escaped the temporary test directory.");
    Directory.Delete(resolvedFixture, recursive: true);
}
Console.WriteLine($"{checks} checks passed.");
