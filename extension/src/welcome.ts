import type { PopupStatus } from "./protocol";

const connection = document.querySelector<HTMLParagraphElement>("#connection")!;
const installHelp = document.querySelector<HTMLDivElement>("#install-help")!;
const retry = document.querySelector<HTMLButtonElement>("#retry")!;
const consent = document.querySelector<HTMLInputElement>("#consent")!;
const enable = document.querySelector<HTMLButtonElement>("#enable")!;
const message = document.querySelector<HTMLParagraphElement>("#message")!;

let nativeConnected = false;

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

function updateEnableButton(): void {
  enable.disabled = !nativeConnected || !consent.checked;
}

function render(status: PopupStatus): void {
  nativeConnected = status.nativeStatus === "connected";
  const disconnected = status.nativeStatus === "disconnected";
  installHelp.hidden = !disconnected;
  retry.hidden = !disconnected;

  if (nativeConnected) {
    connection.textContent = "Native Host 已连接";
    connection.className = "connection connected";
  } else if (status.nativeStatus === "connecting") {
    connection.textContent = "正在连接 Native Host…";
    connection.className = "connection";
  } else {
    connection.textContent = status.nativeError
      ? `Native Host 未连接：${status.nativeError}`
      : "Native Host 未连接";
    connection.className = "connection disconnected";
  }

  if (status.enabled) {
    consent.checked = true;
    consent.disabled = true;
    enable.disabled = true;
    enable.textContent = "已经启用";
    message.textContent = "可以关闭此页面，扩展会继续自动整理标签。";
  } else {
    updateEnableButton();
  }
}

async function refresh(): Promise<void> {
  try {
    render(await send<PopupStatus>({ type: "get_status" }));
  } catch (error) {
    nativeConnected = false;
    installHelp.hidden = false;
    retry.hidden = false;
    connection.textContent = "无法读取扩展状态";
    connection.className = "connection disconnected";
    message.textContent = error instanceof Error ? error.message : String(error);
    updateEnableButton();
  }
}

consent.addEventListener("change", updateEnableButton);

retry.addEventListener("click", async () => {
  retry.disabled = true;
  message.textContent = "正在重新检查…";
  await refresh();
  retry.disabled = false;
  if (!nativeConnected) {
    message.textContent = "仍未连接。安装 Companion 后，Edge 可能需要几秒钟重新连接。";
  }
});

enable.addEventListener("click", async () => {
  enable.disabled = true;
  message.textContent = "正在整理现有标签页…";
  try {
    render(
      await send<PopupStatus>({
        type: "set_enabled",
        enabled: true
      })
    );
    message.textContent = "整理完成。以后新标签和跨网站跳转也会自动归位。";
  } catch (error) {
    message.textContent = error instanceof Error ? error.message : String(error);
    updateEnableButton();
  }
});

void refresh();
window.setInterval(() => {
  if (!nativeConnected) {
    void refresh();
  }
}, 2000);
