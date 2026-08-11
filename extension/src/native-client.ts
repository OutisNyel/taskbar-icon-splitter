import {
  NATIVE_HOST_NAME,
  type NativeResponse
} from "./protocol";

type NativeStatus = "connecting" | "connected" | "disconnected";

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: Error) => void;
  timer: ReturnType<typeof setTimeout>;
}

export class NativeClient {
  private port: chrome.runtime.Port | null = null;
  private connectPromise: Promise<void> | null = null;
  private connectReject: ((reason: Error) => void) | null = null;
  private pending = new Map<string, PendingRequest>();
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private reconnectDelayMs = 1000;
  private everConnected = false;
  private stopped = false;
  private statusValue: NativeStatus = "disconnected";
  private errorValue: string | null = null;
  private readonly statusListeners = new Set<() => void>();
  private readonly reconnectListeners = new Set<() => void>();

  public get status(): NativeStatus {
    return this.statusValue;
  }

  public get error(): string | null {
    return this.errorValue;
  }

  public onStatusChanged(listener: () => void): () => void {
    this.statusListeners.add(listener);
    return () => this.statusListeners.delete(listener);
  }

  public onReconnected(listener: () => void): () => void {
    this.reconnectListeners.add(listener);
    return () => this.reconnectListeners.delete(listener);
  }

  public async connect(): Promise<void> {
    if (this.statusValue === "connected" && this.port) {
      return;
    }
    if (this.connectPromise) {
      return this.connectPromise;
    }

    this.stopped = false;
    this.setStatus("connecting", null);
    this.connectPromise = new Promise<void>((resolve, reject) => {
      this.connectReject = reject;
      let port: chrome.runtime.Port;
      try {
        port = chrome.runtime.connectNative(NATIVE_HOST_NAME);
      } catch (error) {
        const message = this.errorMessage(error);
        this.failConnection(message);
        reject(new Error(message));
        return;
      }

      this.port = port;
      port.onMessage.addListener((message: NativeResponse) => {
        this.handleMessage(message);
      });
      port.onDisconnect.addListener(() => {
        const message =
          chrome.runtime.lastError?.message ?? "Native Host connection closed";
        this.handleDisconnect(message);
      });

      const requestId = crypto.randomUUID();
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        const message = "Native Host handshake timed out";
        this.closePort();
        this.failConnection(message);
        reject(new Error(message));
      }, 5000);
      this.pending.set(requestId, {
        resolve: () => {
          const wasReconnect = this.everConnected;
          this.everConnected = true;
          this.reconnectDelayMs = 1000;
          this.setStatus("connected", null);
          resolve();
          if (wasReconnect) {
            for (const listener of this.reconnectListeners) {
              listener();
            }
          }
        },
        reject,
        timer
      });
      try {
        port.postMessage({ type: "hello", requestId });
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(requestId);
        const message = this.errorMessage(error);
        this.closePort();
        this.failConnection(message);
        reject(new Error(message));
      }
    }).finally(() => {
      this.connectPromise = null;
      this.connectReject = null;
    });

    return this.connectPromise;
  }

  public async request<T>(
    type: string,
    payload: Record<string, unknown> = {},
    timeoutMs = 7000
  ): Promise<T> {
    await this.connect();
    if (!this.port) {
      throw new Error("Native Host is not connected");
    }
    const port = this.port;

    const requestId = crypto.randomUUID();
    return new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        reject(new Error(`Native request timed out: ${type}`));
      }, timeoutMs);
      this.pending.set(requestId, {
        resolve: (value) => resolve(value as T),
        reject,
        timer
      });
      try {
        port.postMessage({ type, requestId, ...payload });
      } catch (error) {
        clearTimeout(timer);
        this.pending.delete(requestId);
        reject(
          new Error(
            `Could not send native request ${type}: ${this.errorMessage(error)}`
          )
        );
      }
    });
  }

  public stop(): void {
    this.stopped = true;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.closePort();
    this.failAllPending("Native Host client stopped");
    this.setStatus("disconnected", null);
  }

  private handleMessage(message: NativeResponse): void {
    const pending = this.pending.get(message.requestId);
    if (!pending) {
      return;
    }
    clearTimeout(pending.timer);
    this.pending.delete(message.requestId);
    if (message.ok) {
      pending.resolve(message.data);
    } else {
      pending.reject(new Error(message.error ?? "Native Host request failed"));
    }
  }

  private handleDisconnect(message: string): void {
    this.port = null;
    this.failAllPending(message);
    this.connectReject?.(new Error(message));
    this.setStatus("disconnected", message);
    if (!this.stopped) {
      this.scheduleReconnect();
    }
  }

  private failConnection(message: string): void {
    this.port = null;
    this.setStatus("disconnected", message);
    if (!this.stopped) {
      this.scheduleReconnect();
    }
  }

  private closePort(): void {
    const port = this.port;
    this.port = null;
    try {
      port?.disconnect();
    } catch {
      // The port may already be closed.
    }
  }

  private failAllPending(message: string): void {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(new Error(message));
    }
    this.pending.clear();
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) {
      return;
    }
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      void this.connect().catch(() => undefined);
    }, this.reconnectDelayMs);
    this.reconnectDelayMs = Math.min(this.reconnectDelayMs * 2, 30_000);
  }

  private setStatus(status: NativeStatus, error: string | null): void {
    this.statusValue = status;
    this.errorValue = error;
    for (const listener of this.statusListeners) {
      listener();
    }
  }

  private errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
  }
}
