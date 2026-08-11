import type {
  TimingMetric,
  TimingStage,
  TimingStatistics
} from "./protocol";

export const TIMING_STAGES: readonly TimingStage[] = [
  "domainResolution",
  "windowOrganization",
  "hwndCorrelation",
  "appUserModelId",
  "iconProcessing"
];

function emptyMetric(): TimingMetric {
  return {
    samples: 0,
    totalMs: 0,
    lastMs: 0,
    maxMs: 0
  };
}

export function createEmptyTimingStatistics(): TimingStatistics {
  return {
    domainResolution: emptyMetric(),
    windowOrganization: emptyMetric(),
    hwndCorrelation: emptyMetric(),
    appUserModelId: emptyMetric(),
    iconProcessing: emptyMetric()
  };
}

export function restoreTimingStatistics(value: unknown): TimingStatistics {
  const restored = createEmptyTimingStatistics();
  if (!value || typeof value !== "object") {
    return restored;
  }

  const source = value as Partial<Record<TimingStage, unknown>>;
  for (const stage of TIMING_STAGES) {
    const candidate = source[stage];
    if (!candidate || typeof candidate !== "object") {
      continue;
    }
    const metric = candidate as Partial<TimingMetric>;
    if (
      isNonNegativeFinite(metric.samples) &&
      Number.isInteger(metric.samples) &&
      isNonNegativeFinite(metric.totalMs) &&
      isNonNegativeFinite(metric.lastMs) &&
      isNonNegativeFinite(metric.maxMs)
    ) {
      restored[stage] = {
        samples: metric.samples,
        totalMs: metric.totalMs,
        lastMs: metric.lastMs,
        maxMs: metric.maxMs
      };
    }
  }
  return restored;
}

export function recordTiming(
  statistics: TimingStatistics,
  stage: TimingStage,
  durationMs: number
): void {
  if (!isNonNegativeFinite(durationMs)) {
    return;
  }

  const metric = statistics[stage];
  metric.samples += 1;
  metric.totalMs += durationMs;
  metric.lastMs = durationMs;
  metric.maxMs = Math.max(metric.maxMs, durationMs);
}

export function copyTimingStatistics(
  statistics: TimingStatistics
): TimingStatistics {
  const copy = createEmptyTimingStatistics();
  for (const stage of TIMING_STAGES) {
    copy[stage] = { ...statistics[stage] };
  }
  return copy;
}

function isNonNegativeFinite(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}
