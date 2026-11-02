/**
 * One uploader sending the same fight again and again.
 *
 * A client that does not get its answer in time (or loops by mistake) sends one fight over and over. Every copy was merged into the same encounter, and
 * each merge of a crowded world boss took longer than the one before (9 s, 13 s, 22 s, 33 s ...), until the backend stood still and ran out of memory
 * (2.11.2026: one client, one Argo fight, 86 copies with identical start and end time).
 *
 * The fight is identified by what the client itself writes into it: the uploader, the boss and the exact start and end time. The first copies are taken
 * (a corrected second upload is legitimate); from the third on the answer is "duplicate" without any work, which also tells the client to stop.
 * Kept in memory only: a restart forgets it, then at most MAX_COPIES more are taken.
 */

export const MAX_COPIES = 2;
const KEEP_MS = 24 * 3_600_000;
const MAX_KEYS = 5_000;

type Entry = { copies: number; lastAt: number };

export class RepeatGuard {
  private readonly seen = new Map<string, Entry>();

  constructor(private readonly maxCopies = MAX_COPIES) {}

  /** True when this copy may be worked on; false when the uploader has sent the same fight often enough already (it is then answered as a duplicate). */
  admit(key: string, now: number): boolean {
    this.prune(now);
    const entry = this.seen.get(key);
    if (!entry) {
      this.seen.set(key, { copies: 1, lastAt: now });
      return true;
    }
    entry.lastAt = now;
    if (entry.copies >= this.maxCopies) {
      return false;
    }
    entry.copies += 1;
    return true;
  }

  get size(): number {
    return this.seen.size;
  }

  private prune(now: number): void {
    if (this.seen.size < MAX_KEYS) {
      return;
    }
    for (const [key, entry] of this.seen) {
      if (now - entry.lastAt > KEEP_MS) {
        this.seen.delete(key);
      }
    }
    if (this.seen.size >= MAX_KEYS) {
      this.seen.clear(); // flooded with distinct keys: forgetting is better than growing
    }
  }
}

export const repeatKey = (ipHash: string, boss: string | number | undefined, startedAt: string, endedAt: string): string => `${ipHash}|${boss ?? ""}|${startedAt}|${endedAt}`;
