import { describe, expect, it } from "vitest";
import {
  createEmptyTimingStatistics,
  recordTiming,
  restoreTimingStatistics
} from "../src/timing";

describe("timing statistics", () => {
  it("tracks the latest, total and maximum duration", () => {
    const statistics = createEmptyTimingStatistics();

    recordTiming(statistics, "domainResolution", 1.25);
    recordTiming(statistics, "domainResolution", 3.5);

    expect(statistics.domainResolution).toEqual({
      samples: 2,
      totalMs: 4.75,
      lastMs: 3.5,
      maxMs: 3.5
    });
  });

  it("restores valid session metrics and rejects malformed values", () => {
    const statistics = restoreTimingStatistics({
      domainResolution: {
        samples: 3,
        totalMs: 6,
        lastMs: 1,
        maxMs: 4
      },
      windowOrganization: {
        samples: -1,
        totalMs: Number.NaN,
        lastMs: 0,
        maxMs: 0
      }
    });

    expect(statistics.domainResolution.samples).toBe(3);
    expect(statistics.windowOrganization.samples).toBe(0);
  });

  it("ignores invalid duration samples", () => {
    const statistics = createEmptyTimingStatistics();

    recordTiming(statistics, "iconProcessing", Number.NaN);
    recordTiming(statistics, "iconProcessing", -1);

    expect(statistics.iconProcessing.samples).toBe(0);
  });
});
