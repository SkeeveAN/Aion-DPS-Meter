import { AION2_CLASS_BY_ID } from "./constants.js";
import { nameOf } from "./content/aion2Servers.js";
import { MAX_SEARCH_BYTES, getNc, ncFetch } from "./portraitService.js";

// A daily check of our own server and class tables against NC's official lists (/api/gameinfo/servers, /api/gameinfo/pcdata): a new server,
// a renamed one or a new class shows up as a difference here instead of through wrong matches weeks later.

type FetchFn = typeof fetch;

const SHARDS = ["eu", "as", "nae", "naw", "la"] as const;
const BASE = "https://aion2.plaync.com/en-us/api/gameinfo";

export type ReferenceReport = { checkedAt: string; servers: number; classes: number; serverProblems: string[]; classProblems: string[] };

/** Our table against NC's server list of one region; the differences as readable lines. */
export function compareServers(shard: string, list: unknown, ours: (id: number) => string | null = nameOf): string[] {
  const problems: string[] = [];
  for (const entry of Array.isArray(list) ? (list as Record<string, unknown>[]) : []) {
    const id = Number(entry?.serverId);
    const name = typeof entry?.serverName === "string" ? entry.serverName : "";
    if (!Number.isInteger(id) || !name) {
      continue;
    }
    const mine = ours(id);
    const label = mine?.replace(/^.* - /, "").replace(/ \[\d+\]$/, "") ?? null;
    if (mine === null || /\[\d+\]$/.test(mine)) {
      problems.push(`${shard}: NC lists server ${id} (${name}), our table has no live server for it`);
    } else if (label !== name) {
      problems.push(`${shard}: server ${id} is "${name}" at NC, "${label}" in our table`);
    }
  }
  return problems;
}

/** Our class table (and the formula (pcId - 1) / 4) against NC's class list. */
export function compareClasses(list: unknown): string[] {
  const problems: string[] = [];
  for (const entry of Array.isArray(list) ? (list as Record<string, unknown>[]) : []) {
    const id = Number(entry?.id);
    const text = typeof entry?.classText === "string" ? entry.classText : "";
    if (!Number.isInteger(id) || id < 1 || !text) {
      continue;
    }
    const mine = AION2_CLASS_BY_ID[Math.floor((id - 1) / 4)];
    if (mine !== text) {
      problems.push(`pcId ${id} is "${text}" at NC, ${mine ? `"${mine}"` : "unknown"} in our table`);
    }
  }
  return [...new Set(problems)];
}

export let lastReport: ReferenceReport | null = null;

/** Fetches NC's lists (six requests) and compares. Network problems throw. */
export async function checkNcReference(fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<ReferenceReport> {
  const serverProblems: string[] = [];
  let servers = 0;
  for (const shard of SHARDS) {
    const { body } = await getNc(fetchFn, new URL(`${BASE}/servers?lang=en-US&region=${shard}`), MAX_SEARCH_BYTES);
    const list = (JSON.parse(body.toString("utf8")) as { serverList?: unknown }).serverList;
    servers += Array.isArray(list) ? list.length : 0;
    serverProblems.push(...compareServers(shard, list));
  }
  const { body } = await getNc(fetchFn, new URL(`${BASE}/pcdata?lang=en-US&region=eu`), MAX_SEARCH_BYTES);
  const classList = (JSON.parse(body.toString("utf8")) as { pcDataList?: unknown }).pcDataList;
  const classProblems = compareClasses(classList);
  lastReport = { checkedAt: new Date(now).toISOString(), servers, classes: Array.isArray(classList) ? classList.length : 0, serverProblems, classProblems };
  return lastReport;
}

/** Off under tests and with NC_MATCH_OFF=1 (like the other NC jobs). */
export function startReferenceCheck(intervalMs = 24 * 3_600_000): void {
  if (process.env.NC_MATCH_OFF === "1" || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = async () => {
    try {
      const report = await checkNcReference();
      if (report.serverProblems.length + report.classProblems.length > 0) {
        console.warn(JSON.stringify({ level: 40, msg: "NC's server or class list differs from ours", problems: [...report.serverProblems, ...report.classProblems].slice(0, 30) }));
      }
    } catch {
      // tomorrow again
    }
  };
  setTimeout(run, 5 * 60_000).unref();
  setInterval(run, intervalMs).unref();
}
