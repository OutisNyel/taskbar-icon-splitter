import { getDomain } from "tldts";

const MAX_CACHED_HOSTNAMES = 512;
const domainCache = new Map<string, string>();

export function domainKeyFromUrl(rawUrl: string | undefined): string | null {
  if (!rawUrl) {
    return null;
  }

  let url: URL;
  try {
    url = new URL(rawUrl);
  } catch {
    return null;
  }

  if (url.protocol !== "http:" && url.protocol !== "https:") {
    return null;
  }

  const hostname = url.hostname
    .toLowerCase()
    .replace(/\.$/, "")
    .replace(/^\[(.*)]$/, "$1");
  if (!hostname) {
    return null;
  }

  const cached = domainCache.get(hostname);
  if (cached !== undefined) {
    domainCache.delete(hostname);
    domainCache.set(hostname, cached);
    return cached;
  }

  const domain =
    getDomain(hostname, { allowPrivateDomains: true, detectIp: true }) ??
    hostname;
  if (domainCache.size >= MAX_CACHED_HOSTNAMES) {
    const oldest = domainCache.keys().next();
    if (!oldest.done) {
      domainCache.delete(oldest.value);
    }
  }
  domainCache.set(hostname, domain);
  return domain;
}
export function faviconCandidates(
  domain: string,
  reportedFavicon: string | undefined
): string[] {
  const candidates = [`https://${domain}/favicon.ico`, reportedFavicon].filter(
    (value): value is string => Boolean(value)
  );
  return [...new Set(candidates)];
}
