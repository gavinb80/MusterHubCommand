using System.Security.Cryptography;
using System.Text;

namespace MusterHubCommand.Api.Services;

// Generates and hashes device pairing tokens. A token is shown once, at
// pairing time, and only the SHA-256 hash is stored -- same shape as core's
// own ApiKeyHasher and Rota/Skills' integration keys.
public static class DeviceTokenHasher
{
    public static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
