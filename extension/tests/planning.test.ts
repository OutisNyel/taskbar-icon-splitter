import { describe, expect, it } from "vitest";
import {
  buildOrganizationPlan,
  eligibleDomainForTab,
  isEligibleTab,
  type WindowLike
} from "../src/planning";

describe("isEligibleTab", () => {
  it("only accepts normal unpinned HTTP(S) tabs with an id", () => {
    expect(
      isEligibleTab({ id: 1, pinned: false, url: "https://example.com" })
    ).toBe(true);
    expect(
      isEligibleTab({ id: 1, pinned: true, url: "https://example.com" })
    ).toBe(false);
    expect(
      isEligibleTab(
        { id: 1, pinned: false, url: "https://example.com" },
        true
      )
    ).toBe(false);
    expect(
      isEligibleTab({ pinned: false, url: "https://example.com" })
    ).toBe(false);
    expect(
      isEligibleTab({ id: 1, pinned: false, url: "edge://newtab/" })
    ).toBe(false);
  });

  it("returns the resolved domain so callers do not parse the URL twice", () => {
    expect(
      eligibleDomainForTab({
        id: 1,
        pinned: false,
        url: "https://gist.github.com/openai"
      })
    ).toBe("github.com");
    expect(
      eligibleDomainForTab({
        id: 1,
        pinned: true,
        url: "https://github.com/openai"
      })
    ).toBeNull();
  });
});

describe("buildOrganizationPlan", () => {
  it("groups by eTLD+1 and preserves source-window/tab order", () => {
    const windows: WindowLike[] = [
      {
        id: 10,
        incognito: false,
        tabs: [
          {
            id: 2,
            index: 1,
            pinned: false,
            url: "https://gist.github.com/two",
            windowId: 10
          },
          {
            id: 1,
            index: 0,
            pinned: false,
            url: "https://www.github.com/one",
            windowId: 10
          }
        ]
      },
      {
        id: 11,
        incognito: false,
        tabs: [
          {
            id: 3,
            index: 0,
            pinned: false,
            url: "https://github.com/three",
            windowId: 11
          }
        ]
      },
      {
        id: 12,
        incognito: true,
        tabs: [
          {
            id: 4,
            index: 0,
            pinned: false,
            url: "https://example.com/private",
            windowId: 12
          }
        ]
      }
    ];

    const plan = buildOrganizationPlan(windows);

    expect([...plan.keys()]).toEqual(["github.com"]);
    expect(plan.get("github.com")?.map(({ tab }) => tab.id)).toEqual([
      1, 2, 3
    ]);
  });
});
