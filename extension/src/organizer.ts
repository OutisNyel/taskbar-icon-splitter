import { faviconCandidates } from "./domain";
import { NativeClient } from "./native-client";
import {
  buildOrganizationPlan,
  eligibleDomainForTab,
  isEligibleTab,
  type WindowLike
} from "./planning";
import type {
  NativeStageTimings,
  NativeBindingSnapshot,
  PopupStatus,
  StoredWindowBinding
} from "./protocol";
import { TabEventScheduler } from "./scheduler";
import {
  copyTimingStatistics,
  createEmptyTimingStatistics,
  recordTiming,
  restoreTimingStatistics
} from "./timing";

const STORAGE_BINDINGS_KEY = "windowBindings";
const STORAGE_ENABLED_KEY = "enabled";
const STORAGE_TIMINGS_KEY = "timingStatistics";
const BOOTSTRAP_PREFIX = "TIS:";

interface ManagedWindow extends StoredWindowBinding {
  bootstrapTabId?: number;
  desiredState?: chrome.windows.Window["state"];
}
interface BindResponse {
  binding: NativeBindingSnapshot;
  timings?: NativeStageTimings;
}

interface ExtensionTimingContext {
  nativeRoundTripMs: number;
}

export class DomainWindowOrganizer {
  private readonly native = new NativeClient();
  private readonly domainWindows = new Map<string, ManagedWindow>();
  private readonly windowDomains = new Map<number, string>();
  private readonly scheduler = new TabEventScheduler(0, (tabId, epoch) => {
    void this.enqueue(() => this.routeTab(tabId, epoch));
  });
  private queue: Promise<void> = Promise.resolve();
  private timings = createEmptyTimingStatistics();
  private enabled = false;
  private initialized = false;
  private listenersRegistered = false;

  public constructor() {
    this.native.onReconnected(() => {
      if (this.initialized) {
        void this.enqueue(async () => {
          await this.restoreStoredBindings();
          if (this.enabled) {
            await this.organizeAllCore();
          }
        });
      }
    });
  }

  public registerListeners(): void {
    if (this.listenersRegistered) {
      return;
    }
    this.listenersRegistered = true;

    chrome.tabs.onUpdated.addListener((tabId, changeInfo) => {
      if (changeInfo.url) {
        this.scheduler.schedule(tabId, true);
      }
      if (changeInfo.status === "complete" || changeInfo.pinned !== undefined) {
        this.scheduler.schedule(tabId);
      }
    });
    chrome.tabs.onCreated.addListener((tab) => {
      if (typeof tab.id === "number") {
        this.scheduler.schedule(tab.id, true);
      }
    });
    chrome.tabs.onAttached.addListener((tabId) => {
      this.scheduler.schedule(tabId, true);
    });
    chrome.tabs.onRemoved.addListener((tabId, removeInfo) => {
      this.scheduler.remove(tabId);
      if (!removeInfo.isWindowClosing) {
        void this.enqueue(() => this.reconcileWindow(removeInfo.windowId));
      }
    });
    chrome.windows.onRemoved.addListener((windowId) => {
      void this.enqueue(() => this.releaseWindow(windowId));
    });
  }

  public async initialize(): Promise<void> {
    const [storedLocal, storedSession] = await Promise.all([
      chrome.storage.local.get({
        [STORAGE_ENABLED_KEY]: false,
        [STORAGE_TIMINGS_KEY]: null
      }),
      chrome.storage.session.get({ [STORAGE_TIMINGS_KEY]: null })
    ]);
    this.enabled = storedLocal[STORAGE_ENABLED_KEY] === true;
    const persistedTimings = storedLocal[STORAGE_TIMINGS_KEY];
    const legacySessionTimings = storedSession[STORAGE_TIMINGS_KEY];
    this.timings = restoreTimingStatistics(
      persistedTimings ?? legacySessionTimings
    );
    if (persistedTimings === null && legacySessionTimings !== null) {
      await this.persistTimings();
    }

    try {
      await this.native.connect();
      await this.restoreStoredBindings();
      if (this.enabled) {
        await this.organizeAll();
      }
    } catch {
      // The popup exposes the Native Host error and allows retry after install.
    } finally {
      this.initialized = true;
    }
  }

  public getStatus(): PopupStatus {
    return {
      enabled: this.enabled,
      nativeStatus: this.native.status,
      nativeError: this.native.error,
      domainWindowCount: this.domainWindows.size,
      timings: copyTimingStatistics(this.timings)
    };
  }

  public async setEnabled(enabled: boolean): Promise<PopupStatus> {
    this.enabled = enabled;
    await chrome.storage.local.set({ [STORAGE_ENABLED_KEY]: enabled });
    if (enabled) {
      await this.organizeAll();
    } else {
      this.scheduler.clear();
      await this.enqueue(() => this.resetAllWindows());
    }
    return this.getStatus();
  }

  public async organizeAll(): Promise<PopupStatus> {
    await this.enqueue(() => this.organizeAllCore());
    return this.getStatus();
  }

  private enqueue<T>(work: () => Promise<T>): Promise<T> {
    const result = this.queue.then(work, work);
    this.queue = result.then(
      () => undefined,
      () => undefined
    );
    return result;
  }

  private async organizeAllCore(): Promise<void> {
    if (!this.enabled) {
      return;
    }
    await this.native.connect();
    const operationStarted = performance.now();
    const timing: ExtensionTimingContext = { nativeRoundTripMs: 0 };
    let domainResolutionMs = 0;

    try {
      const windows = await chrome.windows.getAll({
        populate: true,
        windowTypes: ["normal"]
      });
      const normalWindows = windows.filter(
        (window) => !window.incognito && typeof window.id === "number"
      );
      const focusedWindow = normalWindows.find((window) => window.focused);
      const activeTabId = focusedWindow?.tabs?.find((tab) => tab.active)?.id;

      const domainStarted = performance.now();
      const plan = buildOrganizationPlan(
        normalWindows as unknown as WindowLike[]
      );
      domainResolutionMs = performance.now() - domainStarted;
      recordTiming(this.timings, "domainResolution", domainResolutionMs);

      for (const [domain, plannedTabs] of plan) {
        const firstTab = plannedTabs[0]?.tab;
        if (!firstTab || typeof firstTab.id !== "number") {
          continue;
        }

        const sourceTab = await chrome.tabs.get(firstTab.id).catch(() => null);
        if (!sourceTab) {
          continue;
        }
        const target = await this.ensureDomainWindow(
          domain,
          sourceTab,
          timing
        );

        const tabIds = plannedTabs
          .filter(
            ({ tab }) =>
              typeof tab.id === "number" && tab.windowId !== target.windowId
          )
          .map(({ tab }) => tab.id!);
        if (tabIds.length > 0) {
          await this.moveTabs(tabIds, target);
        }
      }

      for (const windowId of [...this.windowDomains.keys()]) {
        await this.reconcileWindow(windowId);
      }

      if (typeof activeTabId === "number") {
        await this.focusTab(activeTabId);
      }
    } finally {
      const extensionMs = Math.max(
        0,
        performance.now() -
          operationStarted -
          domainResolutionMs -
          timing.nativeRoundTripMs
      );
      recordTiming(this.timings, "windowOrganization", extensionMs);
      await this.persistTimings();
    }
  }

  private async routeTab(tabId: number, epoch: number): Promise<void> {
    if (!this.enabled || !this.scheduler.isCurrent(tabId, epoch)) {
      return;
    }
    const operationStarted = performance.now();
    const timing: ExtensionTimingContext = { nativeRoundTripMs: 0 };
    let domainResolutionMs = 0;
    let domainMeasured = false;
    let windowWorkStarted = false;

    try {
      const tab = await chrome.tabs.get(tabId).catch(() => null);
      if (!tab) {
        return;
      }
      const window = await chrome.windows.get(tab.windowId).catch(() => null);
      if (!window || window.incognito || window.type !== "normal") {
        return;
      }

      const domainStarted = performance.now();
      const domain = eligibleDomainForTab(tab, false);
      domainResolutionMs = performance.now() - domainStarted;
      domainMeasured = true;
      recordTiming(this.timings, "domainResolution", domainResolutionMs);

      if (!domain) {
        windowWorkStarted = true;
        await this.reconcileWindow(tab.windowId);
        return;
      }
      if (tab.status && tab.status !== "complete") {
        return;
      }
      if (this.windowDomains.get(tab.windowId) === domain) {
        return;
      }

      windowWorkStarted = true;
      const sourceWindowId = tab.windowId;
      const target = await this.ensureDomainWindow(domain, tab, timing);
      const shouldFocusTarget = await this.isFocusedTab(
        tabId,
        sourceWindowId
      );
      if (!this.scheduler.isCurrent(tabId, epoch)) {
        return;
      }
      await this.moveTabs([tabId], target);
      if (shouldFocusTarget) {
        await this.focusTab(tabId);
      }
      await this.reconcileWindow(sourceWindowId);
    } finally {
      if (windowWorkStarted) {
        const extensionMs = Math.max(
          0,
          performance.now() -
            operationStarted -
            domainResolutionMs -
            timing.nativeRoundTripMs
        );
        recordTiming(this.timings, "windowOrganization", extensionMs);
      }
      if (domainMeasured || windowWorkStarted) {
        await this.persistTimings();
      }
    }
  }

  private async ensureDomainWindow(
    domain: string,
    sourceTab: chrome.tabs.Tab,
    timing: ExtensionTimingContext
  ): Promise<ManagedWindow> {
    const existing = this.domainWindows.get(domain);
    if (existing) {
      const window = await chrome.windows
        .get(existing.windowId)
        .catch(() => null);
      if (window && !window.incognito) {
        return existing;
      }
      this.removeMapping(existing.windowId);
    }

    const sourceWindow = await chrome.windows
      .get(sourceTab.windowId)
      .catch(() => null);
    const token = crypto.randomUUID();
    const createData: chrome.windows.CreateData = {
      url: chrome.runtime.getURL(
        `bootstrap.html?token=${encodeURIComponent(token)}`
      ),
      focused: false,
      type: "normal"
    };
    if (sourceWindow?.state === "normal") {
      createData.left = sourceWindow.left;
      createData.top = sourceWindow.top;
      createData.width = sourceWindow.width;
      createData.height = sourceWindow.height;
    }

    const created = await chrome.windows.create(createData);
    if (!created || typeof created.id !== "number") {
      throw new Error("Edge did not create a bootstrap window");
    }
    const bootstrapTabId = created.tabs?.[0]?.id;
    const candidates = faviconCandidates(domain, sourceTab.favIconUrl);

    try {
      const nativeStarted = performance.now();
      let response: BindResponse | undefined;
      try {
        response = await this.native.request<BindResponse>(
          "bind_window",
          {
            edgeWindowId: created.id,
            token: `${BOOTSTRAP_PREFIX}${token}`,
            domain,
            faviconCandidates: candidates
          },
          10_000
        );
      } finally {
        timing.nativeRoundTripMs += performance.now() - nativeStarted;
      }
      if (!response?.binding) {
        throw new Error("Native Host returned no window binding");
      }
      this.recordNativeTimings(response.timings);

      const managed: ManagedWindow = {
        windowId: created.id,
        domain,
        faviconCandidates: candidates,
        binding: response.binding,
        bootstrapTabId,
        desiredState:
          sourceWindow?.state && sourceWindow.state !== "normal"
            ? sourceWindow.state
            : undefined
      };
      this.domainWindows.set(domain, managed);
      this.windowDomains.set(created.id, domain);
      await this.persistBindings();
      return managed;
    } catch (error) {
      await chrome.windows.remove(created.id).catch(() => null);
      throw error;
    }
  }

  private async moveTabs(
    tabIds: number[],
    target: ManagedWindow
  ): Promise<void> {
    await chrome.tabs.move(tabIds, {
      windowId: target.windowId,
      index: -1
    });

    if (typeof target.bootstrapTabId === "number") {
      const bootstrapTabId = target.bootstrapTabId;
      target.bootstrapTabId = undefined;
      await chrome.tabs.remove(bootstrapTabId).catch(() => null);
      if (target.desiredState) {
        await chrome.windows
          .update(target.windowId, { state: target.desiredState })
          .catch(() => null);
        target.desiredState = undefined;
      }
    }
  }

  private async isFocusedTab(
    tabId: number,
    windowId: number
  ): Promise<boolean> {
    const [tab, window] = await Promise.all([
      chrome.tabs.get(tabId).catch(() => null),
      chrome.windows.get(windowId).catch(() => null)
    ]);
    return (
      tab?.windowId === windowId &&
      tab.active === true &&
      window?.focused === true
    );
  }

  private async focusTab(tabId: number): Promise<void> {
    const tab = await chrome.tabs
      .update(tabId, { active: true })
      .catch(() => null);
    if (!tab) {
      return;
    }
    await chrome.windows
      .update(tab.windowId, { focused: true })
      .catch(() => null);
  }

  private async reconcileWindow(windowId: number): Promise<void> {
    const domain = this.windowDomains.get(windowId);
    if (!domain) {
      return;
    }

    const window = await chrome.windows.get(windowId).catch(() => null);
    if (!window) {
      await this.releaseWindow(windowId);
      return;
    }
    const tabs = await chrome.tabs.query({ windowId }).catch(() => []);
    const hasManagedTab = tabs.some((tab) => isEligibleTab(tab, false));
    if (hasManagedTab) {
      return;
    }

    await this.native
      .request("reset_window", { edgeWindowId: windowId })
      .catch(() => undefined);
    this.removeMapping(windowId);
    await this.persistBindings();
  }

  private async resetAllWindows(): Promise<void> {
    for (const windowId of [...this.windowDomains.keys()]) {
      await this.native
        .request("reset_window", { edgeWindowId: windowId })
        .catch(() => undefined);
      this.removeMapping(windowId);
    }
    await this.persistBindings();
  }

  private async releaseWindow(windowId: number): Promise<void> {
    if (!this.windowDomains.has(windowId)) {
      return;
    }
    await this.native
      .request("release_window", { edgeWindowId: windowId })
      .catch(() => undefined);
    this.removeMapping(windowId);
    await this.persistBindings();
  }

  private removeMapping(windowId: number): void {
    const domain = this.windowDomains.get(windowId);
    if (domain) {
      this.domainWindows.delete(domain);
    }
    this.windowDomains.delete(windowId);
  }

  private async persistBindings(): Promise<void> {
    const bindings: StoredWindowBinding[] = [...this.domainWindows.values()].map(
      ({ windowId, domain, faviconCandidates: candidates, binding }) => ({
        windowId,
        domain,
        faviconCandidates: candidates,
        binding
      })
    );
    await chrome.storage.session.set({ [STORAGE_BINDINGS_KEY]: bindings });
  }

  private recordNativeTimings(timings: NativeStageTimings | undefined): void {
    if (!timings) {
      return;
    }
    recordTiming(this.timings, "hwndCorrelation", timings.hwndCorrelationMs);
    recordTiming(this.timings, "appUserModelId", timings.appUserModelIdMs);
    recordTiming(this.timings, "iconProcessing", timings.iconProcessingMs);
  }

  private async persistTimings(): Promise<void> {
    await chrome.storage.local.set({
      [STORAGE_TIMINGS_KEY]: copyTimingStatistics(this.timings)
    });
  }

  private async restoreStoredBindings(): Promise<void> {
    const stored = await chrome.storage.session.get({
      [STORAGE_BINDINGS_KEY]: []
    });
    const bindings = Array.isArray(stored[STORAGE_BINDINGS_KEY])
      ? (stored[STORAGE_BINDINGS_KEY] as StoredWindowBinding[])
      : [];

    this.domainWindows.clear();
    this.windowDomains.clear();
    for (const binding of bindings) {
      if (
        !binding ||
        typeof binding.windowId !== "number" ||
        typeof binding.domain !== "string"
      ) {
        continue;
      }
      const window = await chrome.windows
        .get(binding.windowId)
        .catch(() => null);
      if (!window || window.incognito || window.type !== "normal") {
        continue;
      }
      try {
        await this.native.request("restore_window", {
          edgeWindowId: binding.windowId,
          domain: binding.domain,
          faviconCandidates: binding.faviconCandidates,
          binding: binding.binding
        });
        this.domainWindows.set(binding.domain, binding);
        this.windowDomains.set(binding.windowId, binding.domain);
      } catch {
        // A stale HWND or a window that is no longer Edge is intentionally dropped.
      }
    }
    await this.persistBindings();
  }
}
