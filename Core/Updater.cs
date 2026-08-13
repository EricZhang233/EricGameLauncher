using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EricGameLauncher;

public class UpdateService
{
    private static readonly HttpClient client;

    static UpdateService()
    {
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.html+json");
    }

    private static bool _tokenApplied = false;

    private static void ApplyGitHubToken()
    {
        if (!string.IsNullOrEmpty(ConfigService.GitHubToken))
        {
            if (!client.DefaultRequestHeaders.Contains("Authorization"))
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ConfigService.GitHubToken}");
                _tokenApplied = true;
                LogService.Write("Update", "ApplyGitHubToken: authenticated (5000 req/h)");
            }
        }
        else if (!_tokenApplied)
        {
            _tokenApplied = true;
            LogService.Write("Update", "ApplyGitHubToken: no token configured, falling back to unauthenticated (60 req/h)");
        }
    }

    private static void InvalidateToken()
    {
        ConfigService.GitHubToken = "";
        ConfigService.SaveAll();
        client.DefaultRequestHeaders.Remove("Authorization");
        _tokenApplied = false;
        LogService.Write("Update", "InvalidateToken: invalid token cleared, fallback to unauthenticated");
    }
    private const string AllReleasesApiUrl = "https://api.github.com/repos/EricZhang233/EricGameLauncher/releases?per_page=100";
    private const string MirrorPrefix = "https://ghproxy.com/";

    public class ReleaseInfo
    {
        public string tag_name { get; set; } = "";
        public string name { get; set; } = "";
        public string html_url { get; set; } = "";
        public string body { get; set; } = "";
        public string body_html { get; set; } = "";
        public bool prerelease { get; set; } = false;
        public List<Asset> assets { get; set; } = new List<Asset>();
    }

    public class Asset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public long size { get; set; }
    }

    private static async Task<List<ReleaseInfo>?> FetchReleasesWithFallbackAsync()
    {
        ApplyGitHubToken();
        try
        {
            var releases = await client.GetFromJsonAsync<List<ReleaseInfo>>(AllReleasesApiUrl);
            LogService.Write("Update", $"FetchReleasesWithFallback fetched releasesCount={(releases?.Count ?? 0)}");
            return releases;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (!string.IsNullOrEmpty(ConfigService.GitHubToken))
            {
                LogService.Write("Update", "FetchReleasesWithFallback: 401 Unauthorized, invalidating token and retrying without auth", ex);
                InvalidateToken();
                try
                {
                    var releases = await client.GetFromJsonAsync<List<ReleaseInfo>>(AllReleasesApiUrl);
                    LogService.Write("Update", $"FetchReleasesWithFallback retry without auth succeeded, releasesCount={(releases?.Count ?? 0)}");
                    return releases;
                }
                catch (Exception ex2) { LogService.Write("Update", "FetchReleasesWithFallback retry without auth failed", ex2); return null; }
            }
            LogService.Write("Update", "FetchReleasesWithFallback 401 without token", ex);
            return null;
        }
        catch (Exception ex) { LogService.Write("Update", "FetchReleasesWithFallback failed", ex); return null; }
    }

    public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        using (LogService.StartOperation("Update", "GetLatestReleaseAsync"))
        {
            try
            {
                var releases = await FetchReleasesWithFallbackAsync();
                if (releases == null || releases.Count == 0) return null;

                var sortedReleases = releases
                    .OrderByDescending(r =>
                    {
                        var m = Regex.Match(r.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                        return m.Success ? NormalizeVersion(m.Value) : new Version(0, 0, 0);
                    })
                    .ToList();
                var merged = MergeReleases(sortedReleases);
                LogService.Write("Update", $"GetLatestReleaseAsync mergedTag={(merged?.tag_name ?? "null")}");
                return merged;
            }
            catch (Exception ex) { LogService.Write("Update", "GetLatestReleaseAsync failed", ex); return null; }
        }
    }

    public static async Task<ReleaseInfo?> GetLatestStableReleaseAsync()
    {
        using (LogService.StartOperation("Update", "GetLatestStableReleaseAsync"))
        {
            try
            {
                var releases = await FetchReleasesWithFallbackAsync();
                if (releases == null || releases.Count == 0) return null;

                var sortedReleases = releases
                    .Where(r => !r.prerelease)
                    .OrderByDescending(r =>
                    {
                        var m = Regex.Match(r.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                        return m.Success ? NormalizeVersion(m.Value) : new Version(0, 0, 0);
                    })
                    .ToList();
                var merged = MergeReleases(sortedReleases);
                LogService.Write("Update", $"GetLatestStableReleaseAsync mergedTag={(merged?.tag_name ?? "null")}");
                return merged;
            }
            catch (Exception ex) { LogService.Write("Update", "GetLatestStableReleaseAsync failed", ex); return null; }
        }
    }

    private static ReleaseInfo? MergeReleases(List<ReleaseInfo> sortedReleases)
    {
        if (sortedReleases.Count == 0) return null;

        Version currentVersion = NormalizeVersion(AppVersion.Version);
        var newerReleases = sortedReleases.Where(r =>
        {
            var m = Regex.Match(r.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
            return m.Success && NormalizeVersion(m.Value) > currentVersion;
        }).ToList();

        LogService.Write("Update", $"MergeReleases found sorted={sortedReleases.Count} newerReleases={newerReleases.Count}");

        if (newerReleases.Count <= 1)
            return sortedReleases.FirstOrDefault();

        var latest = newerReleases.First();
        var olderNew = newerReleases.Skip(1).ToList();

        string mergedBody = latest.body;
        string mergedBodyHtml = !string.IsNullOrEmpty(latest.body_html) ? latest.body_html : latest.body;

        foreach (var r in olderNew)
        {
            mergedBody += $"\n\n---\n\n## {r.name}\n\n{r.body}";
            string rBodyHtml = !string.IsNullOrEmpty(r.body_html) ? r.body_html : r.body;
            mergedBodyHtml += $"<hr/><h2>{r.name}</h2>{rBodyHtml}";
        }

        LogService.Write("Update", $"MergeReleases merged latest={latest.tag_name} olderCount={olderNew.Count}");

        return new ReleaseInfo
        {
            tag_name = latest.tag_name,
            name = latest.name,
            html_url = latest.html_url,
            body = mergedBody,
            body_html = mergedBodyHtml,
            prerelease = latest.prerelease,
            assets = latest.assets
        };
    }

    public static Task<ReleaseInfo?> GetReleaseAsync(string channel)
        => channel == "latest" ? GetLatestReleaseAsync() : GetLatestStableReleaseAsync();

    public static bool CheckForceUpdateAsync(Version? latestAvailableVersion = null)
    {
        try
        {
            var info = ServerConfigManager.CurrentConfig;
            if (info?.ForceUpdate != null && !string.IsNullOrEmpty(info.ForceUpdate.MinVersion))
            {
                Version minV = NormalizeVersion(info.ForceUpdate.MinVersion);
                Version currentV = NormalizeVersion(AppVersion.Version);

                if (latestAvailableVersion != null && minV > latestAvailableVersion)
                {
                    return false;
                }

                return currentV < minV;
            }
        }
        catch (Exception ex) { LogService.Write("Update", "CheckForceUpdateAsync failed", ex); }
        return false;
    }

    public static Version NormalizeVersion(string versionStr)
    {
        if (Version.TryParse(versionStr, out Version? v) && v != null)
        {
            int major = v.Major >= 0 ? v.Major : 0;
            int minor = v.Minor >= 0 ? v.Minor : 0;
            int build = v.Build >= 0 ? v.Build : 0;
            int revision = v.Revision >= 0 ? v.Revision : 0;
            return new Version(major, minor, build, revision);
        }
        return new Version(0, 0, 0, 0);
    }

    public static Version? ExtractVersion(string tagName)
    {
        var match = Regex.Match(tagName, @"(\d+\.\d+\.\d+(\.\d+)?)");
        return match.Success ? NormalizeVersion(match.Value) : null;
    }

    public static async Task<ReleaseInfo?> CheckForUpdateAsync(string channel = "stable")
    {
        using (LogService.StartOperation("Update", "CheckForUpdateAsync"))
        {
            var sw = Stopwatch.StartNew();
            try
            {
                LogService.Write("Update", $"CheckForUpdate Start channel={channel} currentVer={AppVersion.Version}");
                var release = await GetReleaseAsync(channel);
                if (release == null || string.IsNullOrEmpty(release.tag_name)) return null;

                var match = Regex.Match(release.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                if (!match.Success) return null;

                Version latestVersion = NormalizeVersion(match.Value);
                Version currentVersion = NormalizeVersion(AppVersion.Version);

                var result = latestVersion > currentVersion ? release : null;
                LogService.Write("Update", $"CheckForUpdate End hasUpdate={(result != null)} targetVer={release.tag_name} duration={sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (Exception ex)
            {
                LogService.Write("Update", $"CheckForUpdate Failed: duration={sw.ElapsedMilliseconds}ms", ex);
                return null;
            }
        }
    }

    public static async Task StartUpdaterAndWaitAsync(string downloadUrl, Action<string>? onProgress = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            LogService.Write("Update", $"StartUpdater Start: downloadUrl={downloadUrl}");
            string tempDir = Path.Combine(ConfigService.SystemCachePath, "updater.main");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string mainUpdaterPath = Path.Combine(tempDir, "updater.main.exe");

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string prefix = "EricGameLauncher.updater.main.";
            foreach (var resName in assembly.GetManifestResourceNames())
            {
                if (!resName.StartsWith(prefix)) continue;
                string fileName = resName.Substring(prefix.Length);
                string outputPath = Path.Combine(tempDir, fileName);
                using (var stream = assembly.GetManifestResourceStream(resName))
                {
                    if (stream == null) continue;
                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }

            string pipeName = $"EricGameLauncher_UpdatePipe_{System.Diagnostics.Process.GetCurrentProcess().Id}";
            using var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string args = $"\"{installDir.Replace("\"", "\\\"")}\" \"{downloadUrl.Replace("\"", "\\\"")}\" --pipe-name \"{pipeName}\" --main-pid {System.Diagnostics.Process.GetCurrentProcess().Id}";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = mainUpdaterPath,
                Arguments = args,
                WorkingDirectory = tempDir,
                UseShellExecute = true
            };

            Process.Start(psi);
            LogService.Write("Update", $"StartUpdater Launched: duration={sw.ElapsedMilliseconds}ms args={psi.Arguments}");

            await pipeServer.WaitForConnectionAsync(cts.Token);
            LogService.Write("Update", $"PipeConnected: duration={sw.ElapsedMilliseconds}ms");

            using var reader = new StreamReader(pipeServer);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                LogService.Write("Update", $"PipeReceived: {line}");
                onProgress?.Invoke(line);
                if (line == "READY") break;
            }
        }
        catch (Exception ex)
        {
            LogService.Write("Update", $"StartUpdater Failed: duration={sw.ElapsedMilliseconds}ms", ex);
        }
    }
}
