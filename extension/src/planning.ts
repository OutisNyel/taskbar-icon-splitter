import { domainKeyFromUrl } from "./domain";

export interface TabLike {
  id?: number;
  index: number;
  pinned: boolean;
  active?: boolean;
  url?: string;
  favIconUrl?: string;
  windowId: number;
}
export interface WindowLike {
  id?: number;
  incognito: boolean;
  focused?: boolean;
  tabs?: TabLike[];
}

export interface PlannedTab {
  domain: string;
  tab: TabLike;
}

export function eligibleDomainForTab(
  tab: Pick<TabLike, "id" | "pinned" | "url">,
  incognitoWindow = false
): string | null {
  if (incognitoWindow || tab.pinned || typeof tab.id !== "number") {
    return null;
  }
  return domainKeyFromUrl(tab.url);
}

export function isEligibleTab(
  tab: Pick<TabLike, "id" | "pinned" | "url">,
  incognitoWindow = false
): boolean {
  return eligibleDomainForTab(tab, incognitoWindow) !== null;
}

export function buildOrganizationPlan(
  windows: WindowLike[]
): Map<string, PlannedTab[]> {
  const result = new Map<string, PlannedTab[]>();

  for (const window of windows) {
    if (window.incognito) {
      continue;
    }

    const tabs = [...(window.tabs ?? [])].sort((a, b) => a.index - b.index);
    for (const tab of tabs) {
      const domain = eligibleDomainForTab(tab, false);
      if (!domain) {
        continue;
      }

      const group = result.get(domain) ?? [];
      group.push({ domain, tab });
      result.set(domain, group);
    }
  }

  return result;
}
