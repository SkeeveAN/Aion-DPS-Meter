import { createHash } from "node:crypto";

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
 *
 * The same identity is the RECEIPT the server hands back with every upload answer (a sha256 of it, not guessable without the uploader's hash): with it the
 * client can ask how its fight stands (`GET /api/uploads/receipt/:receipt`) and does not have to send it a second time to find out.
 */

export const MAX_COPIES = 2;
const KEEP_MS = 24 * 3_600_000;
const MAX_KEYS = 5_000;

export type Outcome = { state: "filed" | "turned_away"; encounterId: number | null };
type Entry = { copies: number; lastAt: number; outcome?: Outcome };

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

  /** What became of the fight: undefined while it is not decided (or not known to this process). */
  outcomeOf(key: string): Outcome | undefined {
    return this.seen.get(key)?.outcome;
  }

  /** Is the fight known here (taken before)? */
  knows(key: string): boolean {
    return this.seen.has(key);
  }

  /** Remembers the result of the work on the fight (a copy that waited for NC or review has none yet). */
  record(key: string, outcome: Outcome, now: number): void {
    const entry = this.seen.get(key) ?? { copies: 1, lastAt: now };
    entry.outcome = outcome;
    entry.lastAt = now;
    this.seen.set(key, entry);
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

/** The receipt of a fight: sha256 of uploader, boss and exact start and end time. */
export const repeatKey = (ipHash: string, boss: string | number | undefined, startedAt: string, endedAt: string): string =>
  createHash("sha256").update(`${ipHash}|${boss ?? ""}|${startedAt}|${endedAt}`).digest("hex");
