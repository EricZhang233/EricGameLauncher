using System.Security.Cryptography;

namespace EricGameLauncher;

public static class UpdatePackageIntegrity
{
    public static bool IsValidDigest(string? digest)
    {
        return digest is { Length: 71 }
            && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;
    }

    public static async Task<bool> VerifyAsync(Stream package, string? digest, CancellationToken cancellationToken = default)
    {
        if (!IsValidDigest(digest)) return false;
        byte[] expected = Convert.FromHexString(digest!.AsSpan(7));
        byte[] actual = await SHA256.HashDataAsync(package, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

public sealed class UpdateVerificationException(string message) : Exception(message);
