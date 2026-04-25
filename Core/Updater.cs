using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EricGameLauncher;

public class UpdateService
{
    private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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

    public static async Task<ReleaseInfo?> GetLatestReleaseAsync()
    {
        try
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
            if (!client.DefaultRequestHeaders.Contains("Accept"))
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.html+json");

            var releases = await client.GetFromJsonAsync<List<ReleaseInfo>>(AllReleasesApiUrl);
            if (releases == null || releases.Count == 0) return null;

            var sortedReleases = releases
                .OrderByDescending(r =>
                {
                    var m = Regex.Match(r.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                    return m.Success ? NormalizeVersion(m.Value) : new Version(0, 0, 0);
                })
                .ToList();

            return MergeReleases(sortedReleases);
        }
        catch { return null; }
    }

    public static async Task<ReleaseInfo?> GetLatestStableReleaseAsync()
    {
        try
        {
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
            if (!client.DefaultRequestHeaders.Contains("Accept"))
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.html+json");

            var releases = await client.GetFromJsonAsync<List<ReleaseInfo>>(AllReleasesApiUrl);
            if (releases == null || releases.Count == 0) return null;

            var sortedReleases = releases
                .Where(r => !r.prerelease)
                .OrderByDescending(r =>
                {
                    var m = Regex.Match(r.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
                    return m.Success ? NormalizeVersion(m.Value) : new Version(0, 0, 0);
                })
                .ToList();

            return MergeReleases(sortedReleases);
        }
        catch { return null; }
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
        catch { }
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

    public static async Task<ReleaseInfo?> CheckForUpdateAsync(string channel = "stable")
    {
        try
        {
            var release = await GetReleaseAsync(channel);
            if (release == null || string.IsNullOrEmpty(release.tag_name)) return null;

            var match = Regex.Match(release.tag_name, @"(\d+\.\d+\.\d+(\.\d+)?)");
            if (!match.Success) return null;

            Version latestVersion = NormalizeVersion(match.Value);
            Version currentVersion = NormalizeVersion(AppVersion.Version);

            return latestVersion > currentVersion ? release : null;
        }
        catch { return null; }
    }

    public static void StartUpdater(string downloadUrl)
    {
        try
        {
            string tempDir = Path.Combine(ConfigService.SystemCachePath, "updater.main");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            string mainUpdaterPath = Path.Combine(tempDir, "updater.main.exe");

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string[] resources = { "updater.main.exe", "updater.main.dll", "updater.main.runtimeconfig.json" };
            foreach (var res in resources)
            {
                string resName = $"EricGameLauncher.{res}";
                string outputPath = Path.Combine(tempDir, res);
                using (var stream = assembly.GetManifestResourceStream(resName))
                {
                    if (stream == null) continue;
                    using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                    {
                        stream.CopyTo(fileStream);
                    }
                }
            }

            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string args = string.Join(" ", new[] { installDir, downloadUrl }
                .Select(a => "\"" + a.Replace("\"", "\\\"") + "\""));

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = mainUpdaterPath,
                Arguments = args,
                WorkingDirectory = tempDir,
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch { }
    }
}
