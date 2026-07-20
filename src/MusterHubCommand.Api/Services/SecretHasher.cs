using System.Security.Cryptography;
using System.Text;

namespace MusterHubCommand.Api.Services;

// Generates and hashes bearer secrets -- device pairing tokens and
// integration API keys alike. A secret is shown once, at issuance, and only
// the SHA-256 hash is stored -- same shape as core's own ApiKeyHasher.
public static class SecretHasher
{
    public static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
