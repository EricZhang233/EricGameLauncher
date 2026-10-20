using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EricGameLauncher;

public sealed class UpdateFetchException(string message, Exception? inner = null) : IOException(message, inner);

public class UpdateService
{
    private static HttpClient client;

    static UpdateService()
    {
        client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3.full+json");
    }

    private static readonly SemaphoreSlim FetchGate = new(1, 1);
    private static List<ReleaseInfo>? _cachedReleases;
    private static DateTime _cachedAt;
    private static string _cachedToken = "";
    private static readonly Dictionary<string, (string? ETag, List<ReleaseInfo> Releases)> PageCache = new();
    internal static void ConfigureTransport(HttpMessageHandler handler)
    {
        client.Dispose();
        client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Add("User-Agent", "EricGameLauncher-Updater");
        _cachedReleases = null;
        PageCache.Clear();
    }

    public static void InvalidateCache() => _cachedAt = DateTime.MinValue;
    private const string AllReleasesApiUrl = "https://api.github.com/repos/EricZhang233/EricGameLauncher/releases?per_page=100";
    public const string LatestReleaseUrl = "https://github.com/EricZhang233/EricGameLauncher/releases/latest";


    public class ReleaseInfo
    {
        public string tag_name { get; set; } = "";
        public string name { get; set; } = "";
        public string html_url { get; set; } = "";
        public string body { get; set; } = "";
        public string body_html { get; set; } = "";
        public string published_at { get; set; } = "";
        public string created_at { get; set; } = "";
        public bool prerelease { get; set; } = false;
        public List<Asset> assets { get; set; } = new List<Asset>();
    }

    public class ReleaseHistoryEntry
    {
        public string TagName { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTimeOffset? PublishedAt { get; set; }
        public string Body { get; set; } = "";
        public string BodyHtml { get; set; } = "";
    }

    public class Asset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public string? digest { get; set; }
        public long size { get; set; }
    }

    private static async Task<List<ReleaseInfo>?> FetchReleasesWithFallbackAsync()
    {
        await FetchGate.WaitAsync();
        try
        {
            string token = ConfigService.GitHubToken;
            if (_cachedReleases != null && _cachedToken == token && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(2)) return _cachedReleases;
            var releases = new List<ReleaseInfo>();
            for (int page = 1; ; page++)
            {
                string url = AllReleasesApiUrl + "&page=" + page;
                string cacheKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))) + ":" + page;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                if (PageCache.TryGetValue(cacheKey, out var cached) && cached.ETag != null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
                using var response = await client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
                {
                    if (ConfigService.GitHubToken == token) { ConfigService.GitHubToken = ""; ConfigService.SaveSettings(); }
                    token = ""; releases.Clear(); page = 0; continue;
                }
                List<ReleaseInfo> entries;
                if (response.StatusCode == HttpStatusCode.NotModified) entries = cached.Releases;
                else
                {
                    if (!response.IsSuccessStatusCode)
                        throw new UpdateFetchException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests ? Text.T("Update_RateLimited") : Text.T("Update_NetworkFailed"));
                    entries = await response.Content.ReadFromJsonAsync<List<ReleaseInfo>>() ?? throw new UpdateFetchException(Text.T("Update_NetworkFailed"));
                    PageCache[cacheKey] = (response.Headers.ETag?.ToString(), entries);
                }
                releases.AddRange(entries);
                if (entries.Count < 100) break;
            }
            _cachedReleases = releases; _cachedToken = token; _cachedAt = DateTime.UtcNow;
            return releases;
        }
        catch (UpdateFetchException) { throw; }
        catch (Exception ex) { throw new UpdateFetchException(Text.T("Update_NetworkFailed"), ex); }
        finally { FetchGate.Release(); }
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
            catch (Exception ex) { LogService.Write("Update", "GetLatestReleaseAsync failed", ex); throw; }
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
            catch (Exception ex) { LogService.Write("Update", "GetLatestStableReleaseAsync failed", ex); throw; }
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

    public static async Task<List<ReleaseHistoryEntry>?> GetReleaseHistoryAsync()
    {
        using (LogService.StartOperation("Update", "GetReleaseHistoryAsync"))
        {
            List<ReleaseInfo>? releases;
            try
            {
                releases = await FetchReleasesWithFallbackAsync();
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "GetReleaseHistoryAsync failed", ex);
                throw;
            }

            if (releases == null)
            {
                LogService.Write("Update", "GetReleaseHistoryAsync failed: releases unavailable");
                return null;
            }

            var entries = releases
                .Where(r => !string.IsNullOrEmpty(r.tag_name))
                .Select(r => new ReleaseHistoryEntry
                {
                    TagName = r.tag_name,
                    Name = r.name,
                    PublishedAt = ParsePublishedAt(r.published_at, r.created_at),
                    Body = r.body ?? "",
                    BodyHtml = r.body_html ?? ""
                })
                .OrderBy(e => e.PublishedAt ?? DateTimeOffset.MinValue)
                .ToList();

            LogService.Write("Update", $"GetReleaseHistoryAsync entries={entries.Count}");
            return entries;
        }
    }

    private static DateTimeOffset? ParsePublishedAt(string? primary, string? fallback)
    {
        foreach (var value in new[] { primary, fallback })
        {
            if (string.IsNullOrEmpty(value)) continue;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
                return parsed;
        }
        return null;
    }

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
                throw;
            }
        }
    }

    public sealed class UpdateCheckResult
    {
        public ReleaseInfo? Release { get; set; }
        public bool HasUpdate { get; set; }
        public bool IsForced { get; set; }
    }

    public static bool IsForcedUpdate(ReleaseInfo release)
    {
        var latest = ExtractVersion(release.tag_name);
        return !DebugPaths.IsDebug() && CheckForceUpdateAsync(latest);
    }

    public static async Task<UpdateCheckResult> CheckUpdateStatusAsync(string channel)
    {
        using (LogService.StartOperation("Update", "CheckUpdateStatusAsync"))
        {
            var result = new UpdateCheckResult();
            try
            {
                var release = await GetReleaseAsync(channel);
                if (release == null || string.IsNullOrEmpty(release.tag_name)) return result;

                var latest = ExtractVersion(release.tag_name);
                bool hasUpdate = latest != null && latest > NormalizeVersion(AppVersion.Version);

                result.Release = release;
                result.HasUpdate = hasUpdate;
                result.IsForced = hasUpdate && IsForcedUpdate(release);
                LogService.Write("Update", $"CheckUpdateStatus End hasUpdate={hasUpdate} isForced={result.IsForced} targetVer={release.tag_name}");
            }
            catch (Exception ex)
            {
                LogService.Write("Update", "CheckUpdateStatus failed", ex);
                throw;
            }
            return result;
        }
    }

    public static async Task StartUpdaterAndWaitAsync(Asset asset, Action<string>? onProgress = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (!UpdatePackageIntegrity.IsValidDigest(asset.digest))
                throw new UpdateVerificationException(Text.T("Update_MissingDigest"));
            string downloadUrl = asset.browser_download_url;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(35));
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            connectionCts.CancelAfter(TimeSpan.FromMinutes(5));

            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = mainUpdaterPath,
                WorkingDirectory = tempDir,
                UseShellExecute = true
            };
            foreach (string argument in new[]
            {
                installDir, downloadUrl, "--sha256", asset.digest!,
                "--verification-error", Text.T("Update_VerificationStopped"),
                "--failure-close-message", Text.T("Update_UpdaterClosePrompt"),
                "--pipe-name", pipeName, "--main-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            })
                psi.ArgumentList.Add(argument);

            using var updaterProcess = Process.Start(psi) ?? throw new InvalidOperationException(Text.T("Update_Failed"));
            LogService.Write("Update", $"StartUpdater Launched: duration={sw.ElapsedMilliseconds}ms pid={updaterProcess.Id}");

            var connectionTask = pipeServer.WaitForConnectionAsync(connectionCts.Token);
            var exitTask = updaterProcess.WaitForExitAsync(connectionCts.Token);
            if (await Task.WhenAny(connectionTask, exitTask) == exitTask && !connectionTask.IsCompletedSuccessfully
                && updaterProcess.ExitCode != 2)
                throw new InvalidOperationException(Text.T("Update_Failed"));
            await connectionTask;
            connectionCts.Cancel();
            LogService.Write("Update", $"PipeConnected: duration={sw.ElapsedMilliseconds}ms");

            using var reader = new StreamReader(pipeServer);
            string? line;
            while ((line = await reader.ReadLineAsync(cts.Token)) != null)
            {
                LogService.Write("Update", $"PipeReceived: {line}");
                if (line == "ERROR HASH_MISMATCH" || line == "ERROR HASH_METADATA_INVALID")
                    throw new UpdateVerificationException(Text.T("Update_VerificationFailed"));
                if (line.StartsWith("ERROR ", StringComparison.Ordinal))
                    throw new InvalidOperationException(Text.T("Update_Failed"));
                onProgress?.Invoke(line);
                if (line == "READY")
                {
                    cts.Cancel();
                    return;
                }
            }
            throw new InvalidOperationException(Text.T("Update_Failed"));
        }
        catch (Exception ex)
        {
            LogService.Write("Update", $"StartUpdater Failed: duration={sw.ElapsedMilliseconds}ms", ex);
            throw;
        }
    }
}
