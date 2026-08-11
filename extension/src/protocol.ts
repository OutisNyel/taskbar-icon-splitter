export const NATIVE_HOST_NAME = "com.outis.taskbariconsplitter";

export interface NativeIconSnapshot {
  small: string;
  big: string;
  small2: string;
}

export interface NativeBindingSnapshot {
  hwnd: string;
  originalAppId: string | null;
  originalIcons: NativeIconSnapshot;
  originalRelaunchIconResource?: string | null;
}

export interface StoredWindowBinding {
  windowId: number;
  domain: string;
  faviconCandidates: string[];
  binding: NativeBindingSnapshot;
}

export type TimingStage =
  | "domainResolution"
  | "windowOrganization"
  | "hwndCorrelation"
  | "appUserModelId"
  | "iconProcessing";

export interface TimingMetric {
  samples: number;
  totalMs: number;
  lastMs: number;
  maxMs: number;
}

export type TimingStatistics = Record<TimingStage, TimingMetric>;

export interface NativeStageTimings {
  hwndCorrelationMs: number;
  appUserModelIdMs: number;
  iconProcessingMs: number;
}

export interface NativeResponse<T = unknown> {
  requestId: string;
  ok: boolean;
  error?: string;
  data?: T;
}

export interface PopupStatus {
  enabled: boolean;
  nativeStatus: "connecting" | "connected" | "disconnected";
  nativeError: string | null;
  domainWindowCount: number;
  timings: TimingStatistics;
}
