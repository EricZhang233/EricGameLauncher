using System.Security.Cryptography;
using EricGameLauncher;
using Xunit;

namespace EricGameLauncher.Tests;

public sealed class IntegrityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256:")]
    [InlineData("md5:00000000000000000000000000000000")]
    [InlineData("sha256:zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [Trait("Category", "Core")]
    public void InvalidMetadataFailsClosed(string? digest) => Assert.False(UpdatePackageIntegrity.IsValidDigest(digest));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1048577)]
    [Trait("Category", "Core")]
    public async Task ExactBytesPassAndTamperedBytesFail(int size)
    {
        byte[] bytes = new byte[size];
        new Random(42).NextBytes(bytes);
        string digest = "SHA256:" + Convert.ToHexString(SHA256.HashData(bytes));
        Assert.True(await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(bytes), digest));
        byte[] tampered = [.. bytes, 1];
        Assert.False(await UpdatePackageIntegrity.VerifyAsync(new MemoryStream(tampered), digest));
    }

    [Fact]
    [Trait("Category", "Core")]
    public async Task CancellationStopsHashing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdatePackageIntegrity.VerifyAsync(new MemoryStream(new byte[1024]), "sha256:" + new string('0', 64), cancellation.Token));
    }

    [Theory]
    [InlineData("githubToken: ghp_secret_test")]
    [InlineData("{\"githubToken\":\"github_pat_secret_test\"}")]
    [InlineData("--set GITHUBTOKEN=custom_secret_test")]
    [InlineData("Authorization: Bearer ghp_secret_test")]
    [Trait("Category", "Core")]
    public void SecretsAreRedactedInAllContexts(string input) => Assert.DoesNotContain("secret_test", SensitiveText.Redact(input));

    [Fact]
    [Trait("Category", "Core")]
    public void OrdinarySettingsAndDiagnosticsArePreserved()
    {
        Assert.Equal("hello", SensitiveText.SettingValue("appTitle", "hello"));
        Assert.Equal("[redacted]", SensitiveText.SettingValue("GitHubToken", "secret"));
        Assert.Equal("plain diagnostic", SensitiveText.Redact("plain diagnostic"));
    }
}
