using System.Security.Cryptography;
using System.Text;

namespace TaskbarIconSplitter.Native.Core;

public static class DomainIdentity
{
    private const string Prefix = "Outis.TaskbarIconSplitter.Edge.";

    public static string ComputeAppUserModelId(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(domain.ToLowerInvariant()));
        return Prefix + Convert.ToHexString(hash.AsSpan(0, 12));
    }

    public static string ComputeCacheKey(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(domain.ToLowerInvariant())));
    }
}
