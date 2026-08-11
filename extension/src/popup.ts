import type {
  PopupStatus,
  TimingMetric,
  TimingStage
} from "./protocol";

const connection = document.querySelector<HTMLParagraphElement>("#connection")!;
const enabled = document.querySelector<HTMLInputElement>("#enabled")!;
const count = document.querySelector<HTMLParagraphElement>("#count")!;
const organize = document.querySelector<HTMLButtonElement>("#organize")!;
const message = document.querySelector<HTMLParagraphElement>("#message")!;
const timingOutputs = new Map<TimingStage, HTMLElement>();
document.querySelectorAll<HTMLElement>("[data-timing]").forEach((element) => {
  timingOutputs.set(element.dataset.timing as TimingStage, element);
});

async function send<T>(payload: Record<string, unknown>): Promise<T> {
  const response = (await chrome.runtime.sendMessage(payload)) as
    | T
    | { error?: unknown };
  if (
    response &&
    typeof response === "object" &&
    "error" in response &&
    typeof response.error === "string"
  ) {
    throw new Error(response.error);
  }
  return response as T;
}

function render(status: PopupStatus): void {
  enabled.checked = status.enabled;
  organize.disabled = !status.enabled || status.nativeStatus !== "connected";
  count.textContent = `已管理 ${status.domainWindowCount} 个域名窗口`;

  if (status.nativeStatus === "connected") {
    connection.textContent = "Native Host 已连接";
    connection.className = "status connected";
  } else if (status.nativeStatus === "connecting") {
    connection.textContent = "正在连接 Native Host…";
    connection.className = "status";
  } else {
    connection.textContent = status.nativeError
      ? `Native Host 未连接：${status.nativeError}`
      : "Native Host 未连接";
    connection.className = "status disconnected";
  }

  for (const [stage, output] of timingOutputs) {
    output.textContent = formatTiming(status.timings[stage]);
  }
}

function formatTiming(metric: TimingMetric): string {
  if (metric.samples === 0) {
    return "暂无样本";
  }
  const average = metric.totalMs / metric.samples;
  return `${formatMilliseconds(metric.lastMs)} / ` +
    `${formatMilliseconds(average)} / ` +
    `${formatMilliseconds(metric.maxMs)} · ${metric.samples} 次`;
}

function formatMilliseconds(value: number): string {
  const digits = value < 10 ? 2 : value < 100 ? 1 : 0;
  return `${value.toFixed(digits)} ms`;
}

async function refresh(): Promise<void> {
  try {
    render(await send<PopupStatus>({ type: "get_status" }));
  } catch (error) {
    connection.textContent = "无法读取扩展状态";
    connection.className = "status disconnected";
    message.textContent = error instanceof Error ? error.message : String(error);
  }
}

enabled.addEventListener("change", async () => {
  message.textContent = "";
  enabled.disabled = true;
  try {
    render(
      await send<PopupStatus>({
        type: "set_enabled",
        enabled: enabled.checked
      })
    );
  } catch (error) {
    message.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    enabled.disabled = false;
  }
});

organize.addEventListener("click", async () => {
  message.textContent = "正在整理…";
  organize.disabled = true;
  try {
    render(await send<PopupStatus>({ type: "organize_now" }));
    message.textContent = "整理完成";
  } catch (error) {
    message.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    organize.disabled = false;
  }
});

void refresh();
