import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { TabEventScheduler } from "../src/scheduler";

describe("TabEventScheduler", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("runs only the latest redirect epoch after the debounce", async () => {
    const callback = vi.fn();
    const scheduler = new TabEventScheduler(0, callback);

    scheduler.schedule(7, true);
    const finalEpoch = scheduler.schedule(7, true);

    await vi.advanceTimersByTimeAsync(0);
    expect(callback).toHaveBeenCalledOnce();
    expect(callback).toHaveBeenCalledWith(7, finalEpoch);
  });

  it("cancels work for a closed tab", () => {
    const callback = vi.fn();
    const scheduler = new TabEventScheduler(0, callback);

    scheduler.schedule(8, true);
    scheduler.remove(8);
    vi.runAllTimers();

    expect(callback).not.toHaveBeenCalled();
  });

  it("recognizes stale event versions", () => {
    const scheduler = new TabEventScheduler(0, () => undefined);
    const first = scheduler.bump(9);
    const second = scheduler.bump(9);

    expect(scheduler.isCurrent(9, first)).toBe(false);
    expect(scheduler.isCurrent(9, second)).toBe(true);
  });
});
