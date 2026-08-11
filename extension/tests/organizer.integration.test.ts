import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const nativeState = vi.hoisted(() => ({
  connect: vi.fn(async () => undefined),
  request: vi.fn(),
  reconnectListener: null as (() => void) | null
}));

vi.mock("../src/native-client", () => ({
  NativeClient: class {
    public status = "connected" as const;
    public error: string | null = null;

    public connect = nativeState.connect;
    public request = nativeState.request;

    public onStatusChanged(): () => void {
      return () => undefined;
    }

    public onReconnected(listener: () => void): () => void {
      nativeState.reconnectListener = listener;
      return () => undefined;
    }
  }
}));

import { DomainWindowOrganizer } from "../src/organizer";
import {
  createEmptyTimingStatistics,
  recordTiming
} from "../src/timing";

type Listener = (...args: any[]) => void;

interface ChromeHarness {
  chrome: typeof chrome;
  tabs: Map<number, chrome.tabs.Tab>;
  windows: Map<number, chrome.windows.Window>;
  localStorage: Record<string, unknown>;
  sessionStorage: Record<string, unknown>;
  listeners: {
    tabUpdated: Listener[];
    windowRemoved: Listener[];
  };
  createdWindowIds: number[];
}

function createEvent() {
  const listeners: Listener[] = [];
  return {
    listeners,
    event: {
      addListener: (listener: Listener) => listeners.push(listener)
    }
  };
}

function createHarness(): ChromeHarness {
  let nextWindowId = 100;
  let nextTabId = 1000;
  const tabs = new Map<number, chrome.tabs.Tab>();
  const windows = new Map<number, chrome.windows.Window>();
  const localStorage: Record<string, unknown> = {};
  const sessionStorage: Record<string, unknown> = {};
  const createdWindowIds: number[] = [];

  const tabUpdated = createEvent();
  const tabCreated = createEvent();
  const tabAttached = createEvent();
  const tabRemoved = createEvent();
  const windowRemoved = createEvent();

  const sourceTabs: chrome.tabs.Tab[] = [
    {
      id: 1,
      index: 0,
      pinned: false,
      active: true,
      highlighted: true,
      selected: true,
      incognito: false,
      frozen: false,
      discarded: false,
      autoDiscardable: true,
      groupId: -1,
      windowId: 1,
      status: "complete",
      url: "https://www.github.com/openai",
      favIconUrl: "https://github.githubassets.com/favicons/favicon.svg"
    },
    {
      id: 2,
      index: 1,
      pinned: false,
      active: false,
      highlighted: false,
      selected: false,
      incognito: false,
      frozen: false,
      discarded: false,
      autoDiscardable: true,
      groupId: -1,
      windowId: 1,
      status: "complete",
      url: "https://gist.github.com/openai"
    },
    {
      id: 3,
      index: 2,
      pinned: false,
      active: false,
      highlighted: false,
      selected: false,
      incognito: false,
      frozen: false,
      discarded: false,
      autoDiscardable: true,
      groupId: -1,
      windowId: 1,
      status: "complete",
      url: "https://example.org/"
    },
    {
      id: 4,
      index: 3,
      pinned: true,
      active: false,
      highlighted: false,
      selected: false,
      incognito: false,
      frozen: false,
      discarded: false,
      autoDiscardable: true,
      groupId: -1,
      windowId: 1,
      status: "complete",
      url: "https://pinned.example/"
    }
  ];
  for (const tab of sourceTabs) {
    tabs.set(tab.id!, tab);
  }
  windows.set(1, {
    id: 1,
    focused: true,
    incognito: false,
    alwaysOnTop: false,
    type: "normal",
    state: "normal",
    left: 10,
    top: 20,
    width: 1200,
    height: 800,
    tabs: sourceTabs
  });

  function reindex(windowId: number): void {
    const windowTabs = [...tabs.values()]
      .filter((tab) => tab.windowId === windowId)
      .sort((left, right) => left.index - right.index);
    windowTabs.forEach((tab, index) => {
      tab.index = index;
    });
    const window = windows.get(windowId);
    if (window) {
      window.tabs = windowTabs;
    }
  }

  async function storageGet(
    store: Record<string, unknown>,
    defaults?: string | string[] | Record<string, unknown> | null
  ): Promise<Record<string, unknown>> {
    if (defaults && typeof defaults === "object" && !Array.isArray(defaults)) {
      return { ...defaults, ...store };
    }
    return { ...store };
  }

  const chromeMock = {
    runtime: {
      getURL: (path: string) => `chrome-extension://test/${path}`
    },
    storage: {
      local: {
        get: (defaults: unknown) =>
          storageGet(
            localStorage,
            defaults as Record<string, unknown>
          ),
        set: async (items: Record<string, unknown>) => {
          Object.assign(localStorage, items);
        }
      },
      session: {
        get: (defaults: unknown) =>
          storageGet(
            sessionStorage,
            defaults as Record<string, unknown>
          ),
        set: async (items: Record<string, unknown>) => {
          Object.assign(sessionStorage, items);
        }
      }
    },
    tabs: {
      onUpdated: tabUpdated.event,
      onCreated: tabCreated.event,
      onAttached: tabAttached.event,
      onRemoved: tabRemoved.event,
      get: async (tabId: number) => {
        const tab = tabs.get(tabId);
        if (!tab) {
          throw new Error("No tab");
        }
        return tab;
      },
      move: vi.fn(
        async (
          tabIdOrIds: number | number[],
          move: chrome.tabs.MoveProperties
        ) => {
          const tabIds = Array.isArray(tabIdOrIds)
            ? tabIdOrIds
            : [tabIdOrIds];
          const moved: chrome.tabs.Tab[] = [];
          const sourceWindowIds = new Set<number>();
          const targetWindowId = move.windowId!;
          const targetStartIndex = [...tabs.values()].filter(
            (tab) =>
              tab.windowId === targetWindowId && !tabIds.includes(tab.id!)
          ).length;

          for (const [offset, tabId] of tabIds.entries()) {
            const tab = tabs.get(tabId);
            if (!tab) {
              throw new Error("No tab");
            }
            sourceWindowIds.add(tab.windowId);
            tab.windowId = targetWindowId;
            tab.index = targetStartIndex + offset;
            moved.push(tab);
          }
          for (const sourceWindowId of sourceWindowIds) {
            reindex(sourceWindowId);
          }
          reindex(targetWindowId);
          return Array.isArray(tabIdOrIds) ? moved : moved[0];
        }
      ),
      remove: vi.fn(async (tabId: number) => {
        const tab = tabs.get(tabId);
        if (!tab) {
          return;
        }
        tabs.delete(tabId);
        reindex(tab.windowId);
      }),
      query: async (query: chrome.tabs.QueryInfo) =>
        [...tabs.values()].filter(
          (tab) =>
            query.windowId === undefined || tab.windowId === query.windowId
        ),
      update: vi.fn(
        async (tabId: number, update: chrome.tabs.UpdateProperties) => {
          const tab = tabs.get(tabId);
          if (!tab) {
            throw new Error("No tab");
          }
          if (update.active) {
            for (const candidate of tabs.values()) {
              if (candidate.windowId === tab.windowId) {
                candidate.active = candidate.id === tabId;
              }
            }
          }
          return tab;
        }
      )
    },
    windows: {
      onRemoved: windowRemoved.event,
      getAll: async () => [...windows.values()],
      get: async (windowId: number) => {
        const window = windows.get(windowId);
        if (!window) {
          throw new Error("No window");
        }
        return window;
      },
      create: vi.fn(async (data: chrome.windows.CreateData) => {
        const windowId = nextWindowId++;
        const tabId = nextTabId++;
        const bootstrap: chrome.tabs.Tab = {
          id: tabId,
          index: 0,
          pinned: false,
          active: true,
          highlighted: true,
          selected: true,
          incognito: false,
          frozen: false,
          discarded: false,
          autoDiscardable: true,
          groupId: -1,
          windowId,
          status: "complete",
          url: String(data.url)
        };
        const created: chrome.windows.Window = {
          id: windowId,
          focused: data.focused ?? true,
          incognito: false,
          alwaysOnTop: false,
          type: "normal",
          state: "normal",
          left: data.left,
          top: data.top,
          width: data.width,
          height: data.height,
          tabs: [bootstrap]
        };
        tabs.set(tabId, bootstrap);
        windows.set(windowId, created);
        createdWindowIds.push(windowId);
        return created;
      }),
      update: vi.fn(
        async (windowId: number, update: chrome.windows.UpdateInfo) => {
          const window = windows.get(windowId);
          if (!window) {
            throw new Error("No window");
          }
          if (update.focused) {
            for (const candidate of windows.values()) {
              candidate.focused = candidate.id === windowId;
            }
          }
          if (update.state) {
            window.state = update.state;
          }
          return window;
        }
      ),
      remove: vi.fn(async (windowId: number) => {
        windows.delete(windowId);
        for (const [tabId, tab] of tabs) {
          if (tab.windowId === windowId) {
            tabs.delete(tabId);
          }
        }
      })
    }
  };

  return {
    chrome: chromeMock as unknown as typeof chrome,
    tabs,
    windows,
    localStorage,
    sessionStorage,
    listeners: {
      tabUpdated: tabUpdated.listeners,
      windowRemoved: windowRemoved.listeners
    },
    createdWindowIds
  };
}

async function waitForQueue(organizer: DomainWindowOrganizer): Promise<void> {
  await (organizer as unknown as { queue: Promise<void> }).queue;
}

describe("DomainWindowOrganizer integration", () => {
  let harness: ChromeHarness;

  beforeEach(() => {
    vi.useFakeTimers();
    vi.clearAllMocks();
    nativeState.request.mockImplementation(
      async (type: string, payload: Record<string, unknown>) => {
        if (type === "bind_window") {
          return {
            binding: {
              hwnd: String(5000 + Number(payload.edgeWindowId)),
              originalAppId: "Microsoft.MicrosoftEdge.Stable",
              originalIcons: { small: "1", big: "2", small2: "3" }
            },
            timings: {
              hwndCorrelationMs: 4,
              appUserModelIdMs: 2,
              iconProcessingMs: 8
            }
          };
        }
        return undefined;
      }
    );
    harness = createHarness();
    vi.stubGlobal("chrome", harness.chrome);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("splits domains, merges subdomains, preserves order and final focus", async () => {
    const organizer = new DomainWindowOrganizer();

    const status = await organizer.setEnabled(true);

    expect(status.domainWindowCount).toBe(2);
    expect(status.timings.domainResolution.samples).toBe(1);
    expect(status.timings.windowOrganization.samples).toBe(1);
    expect(status.timings.hwndCorrelation).toEqual({
      samples: 2,
      totalMs: 8,
      lastMs: 4,
      maxMs: 4
    });
    expect(status.timings.appUserModelId.samples).toBe(2);
    expect(status.timings.iconProcessing.samples).toBe(2);
    expect(harness.localStorage.timingStatistics).toEqual(status.timings);
    expect(harness.createdWindowIds).toHaveLength(2);
    expect(harness.chrome.tabs.move).toHaveBeenCalledTimes(2);
    expect(harness.chrome.tabs.move).toHaveBeenCalledWith(
      [1, 2],
      expect.objectContaining({ index: -1 })
    );
    expect(nativeState.request).toHaveBeenCalledWith(
      "bind_window",
      expect.objectContaining({ domain: "github.com" }),
      10_000
    );
    expect(nativeState.request).toHaveBeenCalledWith(
      "bind_window",
      expect.objectContaining({ domain: "example.org" }),
      10_000
    );

    const githubWindowId = harness.tabs.get(1)!.windowId;
    const exampleWindowId = harness.tabs.get(3)!.windowId;
    expect(githubWindowId).not.toBe(exampleWindowId);
    expect(
      [...harness.tabs.values()]
        .filter((tab) => tab.windowId === githubWindowId)
        .map((tab) => tab.id)
    ).toEqual([1, 2]);
    expect(harness.tabs.get(4)!.windowId).toBe(1);
    expect(harness.windows.get(githubWindowId)?.focused).toBe(true);
    expect(harness.tabs.get(1)?.active).toBe(true);
  });

  it("moves a tab after cross-domain navigation and resets on pause", async () => {
    const organizer = new DomainWindowOrganizer();
    organizer.registerListeners();
    await organizer.setEnabled(true);

    const exampleWindowId = harness.tabs.get(3)!.windowId;
    const githubWindowId = harness.tabs.get(2)!.windowId;
    const navigated = harness.tabs.get(2)!;
    navigated.url = "https://www.example.org/moved";
    navigated.status = "complete";
    harness.listeners.tabUpdated[0]?.(2, { url: navigated.url }, navigated);
    harness.listeners.tabUpdated[0]?.(
      2,
      { status: "complete" },
      navigated
    );

    await vi.runAllTimersAsync();
    await waitForQueue(organizer);

    expect(harness.tabs.get(2)!.windowId).toBe(exampleWindowId);
    expect(harness.tabs.get(1)!.windowId).toBe(githubWindowId);
    expect(harness.createdWindowIds).toHaveLength(2);
    expect(harness.windows.get(githubWindowId)?.focused).toBe(true);
    expect(harness.tabs.get(1)?.active).toBe(true);
    expect(harness.tabs.get(2)?.active).toBe(false);

    const beforePause = [...harness.tabs.values()].map((tab) => [
      tab.id,
      tab.windowId
    ]);
    const paused = await organizer.setEnabled(false);
    expect(paused.domainWindowCount).toBe(0);
    expect([...harness.tabs.values()].map((tab) => [tab.id, tab.windowId]))
      .toEqual(beforePause);
    expect(
      nativeState.request.mock.calls.filter(
        ([type]) => type === "reset_window"
      )
    ).toHaveLength(2);
  });

  it("focuses a newly split window when the viewed tab moves there", async () => {
    const organizer = new DomainWindowOrganizer();
    organizer.registerListeners();
    await organizer.setEnabled(true);

    const githubWindowId = harness.tabs.get(1)!.windowId;
    const navigated = harness.tabs.get(1)!;
    navigated.url = "https://chat.openai.com/";
    navigated.status = "complete";
    harness.listeners.tabUpdated[0]?.(1, { url: navigated.url }, navigated);
    harness.listeners.tabUpdated[0]?.(
      1,
      { status: "complete" },
      navigated
    );

    await vi.runAllTimersAsync();
    await waitForQueue(organizer);

    const openaiWindowId = harness.tabs.get(1)!.windowId;
    expect(openaiWindowId).not.toBe(githubWindowId);
    expect(harness.createdWindowIds).toHaveLength(3);
    expect(harness.tabs.get(1)?.active).toBe(true);
    expect(harness.windows.get(openaiWindowId)?.focused).toBe(true);
    expect(harness.windows.get(githubWindowId)?.focused).toBe(false);
  });

  it("releases the native binding when a managed Edge window closes", async () => {
    const organizer = new DomainWindowOrganizer();
    organizer.registerListeners();
    await organizer.setEnabled(true);
    const windowId = harness.tabs.get(1)!.windowId;

    harness.windows.delete(windowId);
    harness.listeners.windowRemoved[0]?.(windowId);
    await waitForQueue(organizer);

    expect(nativeState.request).toHaveBeenCalledWith("release_window", {
      edgeWindowId: windowId
    });
  });

  it("migrates session timing statistics into persistent local storage", async () => {
    const legacyTimings = createEmptyTimingStatistics();
    recordTiming(legacyTimings, "domainResolution", 1.5);
    harness.localStorage.enabled = false;
    harness.sessionStorage.timingStatistics = legacyTimings;
    const organizer = new DomainWindowOrganizer();

    await organizer.initialize();

    expect(organizer.getStatus().timings).toEqual(legacyTimings);
    expect(harness.localStorage.timingStatistics).toEqual(legacyTimings);
  });

  it("starts paused on a fresh install and does not move existing tabs", async () => {
    const organizer = new DomainWindowOrganizer();

    await organizer.initialize();

    expect(organizer.getStatus().enabled).toBe(false);
    expect(harness.createdWindowIds).toHaveLength(0);
    expect(harness.chrome.tabs.move).not.toHaveBeenCalled();
  });

  it("preserves an existing enabled preference during an update", async () => {
    harness.localStorage.enabled = true;
    const organizer = new DomainWindowOrganizer();

    await organizer.initialize();

    expect(organizer.getStatus().enabled).toBe(true);
    expect(organizer.getStatus().domainWindowCount).toBe(2);
  });
});
