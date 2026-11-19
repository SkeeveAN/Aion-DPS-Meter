import { connect } from "node:net";
import { sqlite } from "../db/client.js";
import { env } from "../env.js";
import { baseNameOf } from "../content/aion2Servers.js";
import { maintenanceNotice, refreshMaintenance, type MaintenanceNotice } from "./maintenance.js";

export type Kind = "login" | "game";
export type State = "up" | "down" | "unknown" | "maintenance";
/** What the server did with an idle connection: kept it open, or reset it right away. */
export type Behavior = "open" | "reset";

export type Region = "eu" | "latam" | "nae" | "naw" | "asia";
/** What an address is: the home server of one world, a server shared by all worlds (dungeons, transcendence, Ludra, Abyss ...), or not (yet) told. */
export type Role = "home" | "instance" | "unknown";

interface Endpoint {
  kind: Kind;
  host: string;
  port: number;
  /** Game addresses only: the region of the players who connected to it (null when unknown). */
  region?: Region | null;
  role?: Role;
  /** Home addresses only: the id of the world (1301 = Siel, Elyos; 2301 = Israphel, Asmodian). */
  serverId?: number | null;
}

interface Sample {
  at: number;
  up: boolean;
  ms: number | null;
  behavior: Behavior | null;
}

export interface EndpointStatus {
  kind: Kind;
  address: string;
  region: Region | null;
  /** Game addresses: home server, shared (instance) server or unknown; login addresses: null. */
  role: Role | null;
  serverId: number | null;
  /** The world's own name (English, as in the database; the site shows the reader's language) and its faction. */
  serverName: string | null;
  faction: "elyos" | "asmodian" | null;
  state: State;
  latencyMs: number | null;
  checkedAt: string | null;
  uptime24h: number | null;
  behavior: Behavior | null;
  /** Newest 60 samples, oldest first: 1 = reachable, 0 = not. */
  recent: number[];
}

const PROBE_INTERVAL_MS = 60_000;
const PROBE_TIMEOUT_MS = 4_000;
const KEEP_SAMPLES = 1_440;
const MAX_GAME_ENDPOINTS = 200;
const HOLD_MS = 1_500;
// The game servers answer on this port and live in these two NCSoft ranges (every address seen in the uploads so far).
// The address in an upload comes from the client, so only an address inside the known ranges is ever probed.
const GAME_PORT = 13328;
const GAME_RANGES = [/^193\.202\.112\.\d{1,3}$/, /^87\.232\.75\.\d{1,3}$/];

// The second digit of a character's server id is the region (aion2.run: 1 NA East, 2 NA West, 3 Europe, 4 Latin America, 5 Asia; only Europe is proven
// by real characters). No address served two regions in the uploads, so the region with the most uploads is taken.
const REGION_DIGITS: Record<string, Region> = { "1": "nae", "2": "naw", "3": "eu", "4": "latam", "5": "asia" };

export function regionOfServerId(id: number | null): Region | null {
  return id === null || id < 1000 || id > 9999 ? null : (REGION_DIGITS[String(id)[1]] ?? null);
}

// European home servers have one fixed address each, counted up with the server id (found in the uploads: for 52 ids the address with most uploads
// belongs to this id alone). Elyos 1301-1318 sit on .94-.111, 1319-1323 on .229-.233; Asmodians 2301-2318 on .112-.129, 2319-2323 on .53-.57.
// These addresses are always checked, also when no upload named them in the last 14 days.
const HOME_BLOCK = "193.202.112.";
export function homeAddressOf(id: number): string | null {
  if (id >= 1301 && id <= 1318) return `${HOME_BLOCK}${93 + id - 1300}`;
  if (id >= 1319 && id <= 1323) return `${HOME_BLOCK}${229 + id - 1319}`;
  if (id >= 2301 && id <= 2318) return `${HOME_BLOCK}${111 + id - 2300}`;
  if (id >= 2319 && id <= 2323) return `${HOME_BLOCK}${34 + id - 2300}`;
  return null;
}
const HOME_IDS = [...Array.from({ length: 23 }, (_, i) => 1301 + i), ...Array.from({ length: 23 }, (_, i) => 2301 + i)];
/** An address that served uploads of this many different worlds is shared by them. */
const SHARED_MIN_WORLDS = 3;

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

/** Game server addresses: the known home addresses plus the ones the clients reported in the last 14 days (shared ones are told by the worlds they served). */
function buildGameEndpoints(): Endpoint[] {
  const rows = sqlite
    .prepare(
      `select game_server addr, client_server_id sid, count(*) n from uploads
       where game_server is not null and received_at >= datetime('now', '-14 days') group by game_server, client_server_id`,
    )
    .all() as { addr: string; sid: number | null; n: number }[];
  // How many different worlds an address ever served (all time: a shared server stays shared when it was quiet for two weeks).
  const worlds = new Map(
    (
      sqlite
        .prepare(`select game_server addr, count(distinct client_server_id) c from uploads where game_server is not null group by game_server`)
        .all() as { addr: string; c: number }[]
    ).map((r) => [r.addr, r.c]),
  );
  const home = new Map<string, number>();
  for (const id of HOME_IDS) {
    const ip = homeAddressOf(id);
    if (ip) home.set(`${ip}:${GAME_PORT}`, id);
  }
  const byAddr = new Map<string, Map<Region, number>>();
  for (const r of rows) {
    const regions = byAddr.get(r.addr) ?? new Map<Region, number>();
    const region = regionOfServerId(r.sid);
    if (region) {
      regions.set(region, (regions.get(region) ?? 0) + r.n);
    }
    byAddr.set(r.addr, regions);
  }
  for (const addr of home.keys()) {
    if (!byAddr.has(addr)) byAddr.set(addr, new Map());
  }
  const out: Endpoint[] = [];
  for (const [addr, regions] of byAddr) {
    const m = /^(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})$/.exec(addr);
    if (!m || Number(m[2]) !== GAME_PORT || !GAME_RANGES.some((re) => re.test(m[1]))) {
      continue;
    }
    const serverId = home.get(addr) ?? null;
    const role: Role = serverId !== null ? "home" : (worlds.get(addr) ?? 0) >= SHARED_MIN_WORLDS ? "instance" : "unknown";
    const region = serverId !== null ? "eu" : ([...regions.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] ?? null);
    out.push({ kind: "game", host: m[1], port: GAME_PORT, region, role, serverId });
  }
  return out.sort((a, b) => a.host.localeCompare(b.host, "en", { numeric: true })).slice(0, MAX_GAME_ENDPOINTS);
}

// The list changes slowly; the snapshot (one request per visitor) and every probe round read it, so it is built at most every five minutes.
let gameCache: { at: number; list: Endpoint[] } | null = null;
function gameEndpoints(): Endpoint[] {
  if (!gameCache || Date.now() - gameCache.at > 300_000) {
    gameCache = { at: Date.now(), list: buildGameEndpoints() };
  }
  return gameCache.list;
}

function endpoints(): Endpoint[] {
  return [...loginEndpoints(), ...gameEndpoints()];
}

/** One TCP connect: the server is "up" when it accepts the connection. Nothing is sent; the connection is then held briefly to see whether the server resets it. */
export function probeEndpoint(host: string, port: number): Promise<Sample> {
  return new Promise((resolve) => {
    const start = Date.now();
    const socket = connect({ host, port });
    let ms: number | null = null;
    const done = (up: boolean, behavior: Behavior | null) => {
      socket.destroy();
      resolve({ at: Date.now(), up, ms: up ? ms : null, behavior });
    };
    socket.setTimeout(PROBE_TIMEOUT_MS, () => done(ms !== null, ms !== null ? "open" : null));
    socket.once("connect", () => {
      ms = Date.now() - start;
      socket.setTimeout(0);
      const hold = setTimeout(() => done(true, "open"), HOLD_MS);
      socket.once("error", () => {
        clearTimeout(hold);
        done(true, "reset");
      });
      socket.once("close", () => {
        clearTimeout(hold);
        done(true, "reset");
      });
      socket.resume();
    });
    // An error before the connect event: "refused" or a timeout is not reachable. A reset is the server answering and cutting the connection at once
    // (some servers do it so fast that Node reports it before the connect event), so that one is reachable.
    socket.on("error", (err: NodeJS.ErrnoException) => {
      if (ms !== null) {
        return;
      }
      if (err.code === "ECONNRESET") {
        ms = Date.now() - start;
        done(true, "reset");
      } else {
        done(false, null);
      }
    });
  });
}

async function round(): Promise<void> {
  const list = endpoints();
  const samples = await Promise.all(list.map(async (e) => ({ e, s: await probeEndpoint(e.host, e.port) })));
  // A control connection tells "the servers are down" from "this machine cannot reach the internet / is blocked".
  reachableControl = samples.some((x) => x.s.up) || (await probeEndpoint("1.1.1.1", 443)).up;
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
  const tick = () => {
    void round().catch(() => undefined);
    void refreshMaintenance().catch(() => undefined);
  };
  tick();
  setInterval(tick, PROBE_INTERVAL_MS).unref();
}

export function statusSnapshot(now = Date.now()): {
  checkedAt: string | null;
  intervalSeconds: number;
  loginConfigured: boolean;
  maintenance: MaintenanceNotice | null;
  servers: EndpointStatus[];
} {
  const maintenance = maintenanceNotice(now);
  const dayAgo = Date.now() - 86_400_000;
  const servers = endpoints().map((e): EndpointStatus => {
    const samples = history.get(keyOf(e)) ?? [];
    const last = samples[samples.length - 1];
    const day = samples.filter((s) => s.at >= dayAgo);
    // During the announced maintenance the servers still accept connections but nobody can log in.
    const state: State = !last || !reachableControl ? "unknown" : last.up ? (maintenance?.state === "active" ? "maintenance" : "up") : "down";
    return {
      kind: e.kind,
      address: keyOf(e),
      region: e.region ?? null,
      role: e.kind === "game" ? (e.role ?? "unknown") : null,
      serverId: e.serverId ?? null,
      serverName: e.serverId ? baseNameOf(e.serverId) : null,
      faction: e.serverId ? (e.serverId < 2000 ? "elyos" : "asmodian") : null,
      state,
      latencyMs: last?.up ? last.ms : null,
      checkedAt: last ? new Date(last.at).toISOString() : null,
      uptime24h: day.length >= 5 ? Math.round((day.filter((s) => s.up).length / day.length) * 1000) / 10 : null,
      behavior: last?.behavior ?? null,
      recent: samples.slice(-60).map((s) => (s.up ? 1 : 0)),
    };
  });
  return {
    checkedAt: lastRound ? new Date(lastRound).toISOString() : null,
    intervalSeconds: PROBE_INTERVAL_MS / 1000,
    loginConfigured: loginEndpoints().length > 0,
    maintenance,
    servers,
  };
}
