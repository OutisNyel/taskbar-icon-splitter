namespace TaskbarIconSplitter.Native.Icons;

internal static class IconCandidatePolicy
{
    internal static bool TryCreateNetworkUri(
        string candidate,
        out Uri uri)
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
            IsAllowedNetworkUri(parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    internal static bool IsAllowedNetworkUri(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp ||
            uri.Scheme == Uri.UriSchemeHttps;
    }

    internal static bool TryDecodeDataImage(
        string candidate,
        int maximumBytes,
        out byte[] bytes)
    {
        bytes = [];
        if (!candidate.StartsWith(
                "data:image/",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = candidate.IndexOf(',');
        if (comma < 0 ||
            !candidate[..comma].Contains(
                ";base64",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var encodedLength = candidate.Length - comma - 1;
        if (encodedLength > ((long)maximumBytes + 2) / 3 * 4)
        {
            return false;
        }

        try
        {
            var decoded = Convert.FromBase64String(candidate[(comma + 1)..]);
            if (decoded.Length > maximumBytes)
            {
                return false;
            }

            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
