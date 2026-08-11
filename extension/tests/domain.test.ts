import { describe, expect, it } from "vitest";
import { domainKeyFromUrl, faviconCandidates } from "../src/domain";

describe("domainKeyFromUrl", () => {
  it.each([
    ["https://www.github.com/a", "github.com"],
    ["https://gist.github.com:8443/a", "github.com"],
    ["https://news.bbc.co.uk/", "bbc.co.uk"],
    ["https://www.bücher.de/", "xn--bcher-kva.de"],
    ["http://127.0.0.1:8080/", "127.0.0.1"],
    ["http://[2001:db8::1]:8080/", "2001:db8::1"],
    ["http://localhost:3000/", "localhost"],
    ["https://tenant.blogspot.com/", "tenant.blogspot.com"]
  ])("maps %s to %s", (url, expected) => {
    expect(domainKeyFromUrl(url)).toBe(expected);
  });

  it.each([
    undefined,
    "",
    "not a URL",
    "edge://newtab/",
    "chrome-extension://abc/page.html",
    "file:///C:/test.html",
    "ftp://example.com/file"
  ])("rejects an ineligible URL: %s", (url) => {
    expect(domainKeyFromUrl(url)).toBeNull();
  });
});

describe("faviconCandidates", () => {
  it("prefers the registrable-domain favicon and removes duplicates", () => {
    expect(
      faviconCandidates("example.com", "https://example.com/favicon.ico")
    ).toEqual(["https://example.com/favicon.ico"]);
  });

  it("keeps the Edge-reported favicon as the second choice", () => {
    expect(
      faviconCandidates("example.com", "data:image/png;base64,AA==")
    ).toEqual([
      "https://example.com/favicon.ico",
      "data:image/png;base64,AA=="
    ]);
  });
});
