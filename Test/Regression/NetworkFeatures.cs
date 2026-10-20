using EricGameLauncher;
using System.Net;
using System.Text.Json;

internal static class NetworkFeatures
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        check(UpdateService.NormalizeVersion("1.2") == new Version(1, 2, 0, 0), "two-part version is normalized");
        check(UpdateService.NormalizeVersion("broken") == new Version(0, 0, 0, 0), "invalid version normalizes safely");
        check(UpdateService.ExtractVersion("v1.2.3.4-preview") == new Version(1, 2, 3, 4) && UpdateService.ExtractVersion("invalid") == null, "tag version extraction supports fourth component and invalid tags");
        var releases = new[]
        {
            new UpdateService.ReleaseInfo { tag_name = "v999.0.2", name = "Preview", prerelease = true, body = "preview body", published_at = "2026-10-19T00:00:00Z" },
            new UpdateService.ReleaseInfo { tag_name = "v999.0.1", name = "Stable", prerelease = false, body = "stable body", published_at = "2026-10-18T00:00:00Z" },
            new UpdateService.ReleaseInfo { tag_name = "v999.0.0", name = "Older", prerelease = false, body = "older body", published_at = "2026-10-17T00:00:00Z" }
        };
        UpdateService.ConfigureTransport(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) }));
        var stable = await UpdateService.GetReleaseAsync("stable");
        var latest = await UpdateService.GetReleaseAsync("latest");
        check(stable?.tag_name == "v999.0.1" && latest?.tag_name == "v999.0.2", "stable excludes prerelease and latest includes it");
        check(stable!.body.Contains("older body") && !stable.body.Contains("preview body"), "stable merged notes contain only newer stable versions");
        check(latest!.body.Contains("preview body") && latest.body.Contains("stable body") && latest.body.Contains("older body"), "latest merged notes include all pending versions");
        check((await UpdateService.CheckUpdateStatusAsync("stable")).HasUpdate, "newer release reports available update");
        UpdateService.ConfigureTransport(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        check(!(await UpdateService.CheckUpdateStatusAsync("latest")).HasUpdate, "empty release list is no update");
        UpdateService.ConfigureTransport(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { new UpdateService.ReleaseInfo { tag_name = "v" + AppVersion.Version } })) }));
        check(!(await UpdateService.CheckUpdateStatusAsync("stable")).HasUpdate, "same version is no update");
        UpdateService.ConfigureTransport(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("invalid json") }));
        bool malformed = false;
        try { await UpdateService.GetReleaseHistoryAsync(); } catch (UpdateFetchException) { malformed = true; }
        check(malformed, "malformed network payload is an error");
        UpdateService.ConfigureTransport(new Handler(_ => new(HttpStatusCode.ServiceUnavailable)));
        bool unavailable = false;
        try { await UpdateService.GetReleaseHistoryAsync(); } catch (UpdateFetchException) { unavailable = true; }
        check(unavailable, "HTTP server failure is not no-update success");
        int calls = 0;
        ConfigService.GitHubToken = "ghp_invalid_fixture";
        UpdateService.ConfigureTransport(new Handler(request =>
        {
            calls++;
            return request.Headers.Authorization != null ? new(HttpStatusCode.Unauthorized) : new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        await UpdateService.GetReleaseHistoryAsync();
        check(calls == 2 && ConfigService.GitHubToken == "", "current invalid token clears and request retries anonymously");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
