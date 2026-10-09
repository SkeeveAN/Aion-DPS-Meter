import { connect } from "node:net";
import { sqlite } from "../db/client.js";
import { env } from "../env.js";

export type Kind = "login" | "game";
export type State = "up" | "down" | "unknown";

interface Endpoint {
  kind: Kind;
  host: string;
  port: number;
}

interface Sample {
  at: number;
  up: boolean;
  ms: number | null;
}

export interface EndpointStatus {
  kind: Kind;
  address: string;
  state: State;
  latencyMs: number | null;
  checkedAt: string | null;
  uptime24h: number | null;
  /** Newest 60 samples, oldest first: 1 = reachable, 0 = not. */
  recent: number[];
}

const PROBE_INTERVAL_MS = 60_000;
const PROBE_TIMEOUT_MS = 4_000;
const KEEP_SAMPLES = 1_440;
const MAX_GAME_ENDPOINTS = 60;
// The game servers answer on this port and live in these two NCSoft ranges (every address seen in the uploads so far).
// The address in an upload comes from the client, so only an address inside the known ranges is ever probed.
const GAME_PORT = 13328;
const GAME_RANGES = [/^193\.202\.112\.\d{1,3}$/, /^87\.232\.75\.\d{1,3}$/];

const history = new Map<string, Sample[]>();
let started = false;
let lastRound = 0;
let reachableControl = true;

const keyOf = (e: { host: string; port: number }) => `${e.host}:${e.port}`;

/** Login servers are not known from any capture yet: the operator lists them in LOGIN_SERVERS ("host:port,host:port"). */
function loginEndpoints(): Endpoint[] {
  return env.LOGIN_SERVERS.split(",")
    .map((s) => s.trim())
    .filter(Boolean)
    .flatMap((s) => {
      const m = /^([A-Za-z0-9.-]{1,100}):(\d{1,5})$/.exec(s);
      return m && Number(m[2]) > 0 && Number(m[2]) < 65536 ? [{ kind: "login" as const, host: m[1], port: Number(m[2]) }] : [];
    });
}

/** Game server addresses the clients reported in the last 14 days. Which world sits behind an address is not known, so none is claimed. */
function gameEndpoints(): Endpoint[] {
  const rows = sqlite
    .prepare(
      `select game_server addr from uploads
       where game_server is not null and received_at >= datetime('now', '-14 days') group by game_server order by count(*) desc limit 300`,
    )
    .all() as { addr: string }[];
  const out: Endpoint[] = [];
  for (const r of rows) {
    const m = /^(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})$/.exec(r.addr);
    if (!m || Number(m[2]) !== GAME_PORT || !GAME_RANGES.some((re) => re.test(m[1]))) {
      continue;
    }
    out.push({ kind: "game", host: m[1], port: GAME_PORT });
  }
  return out.sort((a, b) => a.host.localeCompare(b.host, "en", { numeric: true })).slice(0, MAX_GAME_ENDPOINTS);
}

function endpoints(): Endpoint[] {
  return [...loginEndpoints(), ...gameEndpoints()];
}

/** One TCP connect: the server is "up" when it accepts the connection. Nothing is sent. */
function probe(host: string, port: number): Promise<Sample> {
  return new Promise((resolve) => {
    const start = Date.now();
    const socket = connect({ host, port });
    const done = (up: boolean) => {
      socket.destroy();
      resolve({ at: Date.now(), up, ms: up ? Date.now() - start : null });
    };
    socket.setTimeout(PROBE_TIMEOUT_MS, () => done(false));
    socket.once("connect", () => done(true));
    socket.once("error", () => done(false));
  });
}

async function round(): Promise<void> {
  const list = endpoints();
  const samples = await Promise.all(list.map(async (e) => ({ e, s: await probe(e.host, e.port) })));
  // A control connection tells "the servers are down" from "this machine cannot reach the internet / is blocked".
  reachableControl = samples.some((x) => x.s.up) || (await probe("1.1.1.1", 443)).up;
  for (const { e, s } of samples) {
    const list2 = history.get(keyOf(e)) ?? [];
    list2.push(s);
    if (list2.length > KEEP_SAMPLES) {
      list2.splice(0, list2.length - KEEP_SAMPLES);
    }
    history.set(keyOf(e), list2);
  }
  lastRound = Date.now();
}

export function startStatusProbe(): void {
  if (started) {
    return;
  }
  started = true;
  const tick = () => void round().catch(() => undefined);
  tick();
  setInterval(tick, PROBE_INTERVAL_MS).unref();
}

export function statusSnapshot(): { checkedAt: string | null; intervalSeconds: number; loginConfigured: boolean; servers: EndpointStatus[] } {
  const dayAgo = Date.now() - 86_400_000;
  const servers = endpoints().map((e): EndpointStatus => {
    const samples = history.get(keyOf(e)) ?? [];
    const last = samples[samples.length - 1];
    const day = samples.filter((s) => s.at >= dayAgo);
    const state: State = !last || !reachableControl ? "unknown" : last.up ? "up" : "down";
    return {
      kind: e.kind,
      address: keyOf(e),
      state,
      latencyMs: last?.up ? last.ms : null,
      checkedAt: last ? new Date(last.at).toISOString() : null,
      uptime24h: day.length >= 5 ? Math.round((day.filter((s) => s.up).length / day.length) * 1000) / 10 : null,
      recent: samples.slice(-60).map((s) => (s.up ? 1 : 0)),
    };
  });
  return {
    checkedAt: lastRound ? new Date(lastRound).toISOString() : null,
    intervalSeconds: PROBE_INTERVAL_MS / 1000,
    loginConfigured: loginEndpoints().length > 0,
    servers,
  };
}
