export class TabEventScheduler {
  private readonly epochs = new Map<number, number>();
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>();

  public constructor(
    private readonly delayMs: number,
    private readonly callback: (tabId: number, epoch: number) => void
  ) {}

  public bump(tabId: number): number {
    const epoch = (this.epochs.get(tabId) ?? 0) + 1;
    this.epochs.set(tabId, epoch);
    return epoch;
  }

  public schedule(tabId: number, bumpEpoch = false): number {
    const epoch = bumpEpoch ? this.bump(tabId) : (this.epochs.get(tabId) ?? 0);
    const existing = this.timers.get(tabId);
    if (existing) {
      clearTimeout(existing);
    }

    const timer = setTimeout(() => {
      this.timers.delete(tabId);
      if ((this.epochs.get(tabId) ?? 0) === epoch) {
        this.callback(tabId, epoch);
      }
    }, this.delayMs);
    this.timers.set(tabId, timer);
    return epoch;
  }

  public isCurrent(tabId: number, epoch: number): boolean {
    return (this.epochs.get(tabId) ?? 0) === epoch;
  }

  public remove(tabId: number): void {
    const timer = this.timers.get(tabId);
    if (timer) {
      clearTimeout(timer);
    }
    this.timers.delete(tabId);
    this.epochs.delete(tabId);
  }

  public clear(): void {
    for (const timer of this.timers.values()) {
      clearTimeout(timer);
    }
    this.timers.clear();
    this.epochs.clear();
  }
}
