// The pace of all requests towards NC, learned while running: it starts slow, gets shorter in small steps while the answers are fine, and gets
// longer again when server errors (502 and the like) pile up or NC says "too many" (429/403). Measured 2026-10-25 from a home connection: 0.9 requests
// a second per worker and 1.8 in all without a 429/403; a single 502 now and then (0.4-1 %) is normal, a cluster of them is the sign to slow down.

export type PacingOptions = {
  startGapMs: number;
  floorGapMs: number;
  ceilingGapMs: number;
  /** Answers without trouble in a row before the gap is lowered one step. */
  okPerStep: number;
  /** The gap is multiplied by this per step down. */
  stepDown: number;
  /** Server errors within `clusterWindowMs` that count as a cluster, and the factor the gap is raised by then. */
  clusterCount: number;
  clusterWindowMs: number;
  raiseFactor: number;
  /** Pause of every request after a 429/403, and the factor the gap is raised by. */
  limitPauseMs: number;
  limitFactor: number;
};

export const DEFAULT_PACING: PacingOptions = {
  startGapMs: 2000,
  floorGapMs: 500,
  ceilingGapMs: 10_000,
  okPerStep: 100,
  stepDown: 0.9,
  clusterCount: 3,
  clusterWindowMs: 60_000,
  raiseFactor: 1.5,
  limitPauseMs: 60_000,
  limitFactor: 2,
};

export class NcPacing {
  private gap: number;
  private okRun = 0;
  private errorTimes: number[] = [];
  private lastRaise = -Infinity;
  private pausedUntilMs = 0;

  constructor(
    private readonly opts: PacingOptions = DEFAULT_PACING,
    private readonly log: (message: string) => void = (m) => console.log(`[nc-pacing] ${m}`),
  ) {
    this.gap = opts.startGapMs;
  }

  /** The pause between two requests now. */
  gapMs(): number {
    return this.gap;
  }

  /** Until when nothing may be asked (after a 429/403); 0 when there is no pause. */
  pausedUntil(): number {
    return this.pausedUntilMs;
  }

  /** The answer of one request: its HTTP status, or "network" when there was none. */
  note(status: number | "network", now = Date.now()): void {
    if (status === 429 || status === 403) {
      this.gap = Math.min(this.opts.ceilingGapMs, Math.round(this.gap * this.opts.limitFactor));
      this.pausedUntilMs = now + this.opts.limitPauseMs;
      this.okRun = 0;
      this.log(`HTTP ${status}: pause ${this.opts.limitPauseMs / 1000} s, gap raised to ${this.gap} ms`);
      return;
    }
    if (status === "network" || status >= 500) {
      this.errorTimes.push(now);
      while (this.errorTimes.length > 0 && this.errorTimes[0] < now - this.opts.clusterWindowMs) {
        this.errorTimes.shift();
      }
      this.okRun = 0;
      // one raise per window: the errors of one cluster do not raise it again and again
      if (this.errorTimes.length >= this.opts.clusterCount && now - this.lastRaise >= this.opts.clusterWindowMs) {
        this.lastRaise = now;
        this.gap = Math.min(this.opts.ceilingGapMs, Math.round(this.gap * this.opts.raiseFactor));
        this.log(`${this.errorTimes.length} server errors in ${this.opts.clusterWindowMs / 1000} s (last ${status}): gap raised to ${this.gap} ms`);
      }
      return;
    }
    if (++this.okRun >= this.opts.okPerStep && this.gap > this.opts.floorGapMs) {
      this.okRun = 0;
      this.gap = Math.max(this.opts.floorGapMs, Math.round(this.gap * this.opts.stepDown));
      this.log(`gap lowered to ${this.gap} ms`);
    }
  }
}
