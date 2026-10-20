using System.Diagnostics;
using EricGameLauncher;

string root = EricGameLauncher.Testing.TestWorkspace.NewFixture("EGL.LogStress.");
try
{
ConfigService.SystemCachePath = root;
string logs = Path.Combine(root, "log");
Directory.CreateDirectory(logs);
string old = Path.Combine(logs, "app-expired.log"), recent = Path.Combine(logs, "app-recent.log");
File.WriteAllText(old, "expired"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));
File.WriteAllText(recent, "recent");
var timer = Stopwatch.StartNew();
LogService.Write("Stress", "rotation-probe|" + new string('r', 6 * 1024 * 1024) + "|end", level: LogService.LogLevel.Warn);
bool rotated = SpinWait.SpinUntil(() => Directory.GetFiles(logs, "app-*-1.log").Length > 0, TimeSpan.FromSeconds(15));
int count = 12000;
Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
    LogService.Write("Stress", $"critical-{i:D5}|" + new string('x', 1024) + "|end", level: LogService.LogLevel.Warn));
LogService.Write("Stress", "flush-tail-marker", level: LogService.LogLevel.Error);
LogService.FlushAndStop();
var files = Directory.GetFiles(logs, "app-*.log").Where(path => path != recent).ToArray();
string contents = string.Join('\n', files.Select(File.ReadAllText));
var present = System.Text.RegularExpressions.Regex.Matches(contents, @"critical-(\d{5})\|").Select(x => int.Parse(x.Groups[1].Value)).ToHashSet();
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("FAIL " + name); Console.WriteLine("PASS " + name); checks++; }
Console.WriteLine($"METRIC duration={timer.ElapsedMilliseconds}ms files={files.Length} expected={count} observed={present.Count} fixture={root}");
Check(present.Count == count, "concurrent warning entries all survive bounded-queue overflow");
Check(contents.Contains("flush-tail-marker"), "shutdown drains final error marker");
Check(!File.Exists(old) && File.Exists(recent), "retention removes expired session and preserves recent session");
Check(rotated, "primary log rotates after a dedicated entry exceeds its size threshold");
Check(contents.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(x => x.Contains("critical-")).All(x => x.TrimEnd('\r').EndsWith("|end")), "parallel fallback writes preserve complete lines");
Console.WriteLine($"{checks} checks passed.");
}
finally
{
    LogService.FlushAndStop();
    EricGameLauncher.Testing.TestWorkspace.DeleteFixture(root);
}

namespace EricGameLauncher { internal static class ConfigService { internal static string SystemCachePath = ""; } }
