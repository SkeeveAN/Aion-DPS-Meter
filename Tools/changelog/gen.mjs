// Builds Web-Frontend/changelog.json from the git history (GitHub only keeps the newest 10
// releases, git keeps everything). Run it after the release commit has been made:
//   node Tools/changelog/gen.mjs
// A release is closed by the commit that changes <Version> in Client/AionDPS.csproj; every commit
// since the previous one is an entry of it (commits after the last one are "Unreleased").
// Each entry is tagged by the paths its commit touched: Client, Website (Web-Frontend/),
// Backend (Backend/) and Database (Backend/drizzle, Backend/src/db). No commit convention needed.
// A "Release X.Y.Z: <text>" subject contributes its text; bare version bumps and merges nothing.
import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const out = fileURLToPath(new URL("../../Web-Frontend/changelog.json", import.meta.url));
const git = (...args) => execFileSync("git", args, { encoding: "utf8", maxBuffer: 1 << 28, stdio: ["ignore", "pipe", "ignore"] });

const AREAS = [
  ["Database", /^Backend\/(drizzle|src\/db)\//],
  ["Backend", /^Backend\//],
  ["Website", /^Web-Frontend\//],
  ["Client", /^Client\//],
];
const areasOf = (files) => {
  const set = new Set();
  for (const f of files) {
    if (f === "Web-Frontend/changelog.json") continue;
    for (const [name, re] of AREAS) {
      if (re.test(f)) { set.add(name); if (name === "Database") continue; break; }
    }
  }
  return ["Client", "Website", "Backend", "Database"].filter((a) => set.has(a));
};

// overrides.json: {"<commit hash prefix>": "<text shown instead>" | null (= leave the commit out)}.
// For commits whose subject must not be shown as written (e.g. it names something that is not public).
const overrides = JSON.parse(readFileSync(fileURLToPath(new URL("./overrides.json", import.meta.url)), "utf8"));
const overrideFor = (hash) => Object.entries(overrides).find(([prefix]) => hash.startsWith(prefix));

const SEP = "\u0001";
const raw = git("log", "--reverse", "--name-only", `--format=${SEP}%H%x09%ad%x09%s`, "--date=short");
const commits = raw.split(SEP).filter(Boolean).map((block) => {
  const [head, ...files] = block.split("\n");
  const [hash, date, ...s] = head.split("\t");
  return { hash, date, subject: s.join("\t"), files: files.filter(Boolean) };
});

const versionAt = (hash) => /<Version>([^<]+)<\/Version>/.exec((() => { try { return git("show", `${hash}:Client/AionDPS.csproj`); } catch { return ""; } })())?.[1] ?? null;

const releases = [];
let current = null;
let pending = [];
for (const c of commits) {
  const touchesCsproj = c.files.includes("Client/AionDPS.csproj");
  const v = touchesCsproj || current === null ? versionAt(c.hash) : current;
  const rel = /^Release (\d+\.\d+\.\d+)(?:: (.+))?$/.exec(c.subject);
  const noise = /^(Merge |Bump (the )?version|Version \d|Release workflow|Changelog)/.test(c.subject);
  const override = overrideFor(c.hash);
  const text = override ? override[1] : rel ? rel[2] : noise ? null : c.subject.replace(/;? ?version \d+\.\d+\.\d+$/i, "");
  const areas = areasOf(c.files);
  if (text && areas.length && current !== null) {
    pending.push({ text, areas });
  }
  if (v && v !== current) {
    current = v;
    if (pending.length) {
      releases.push({ version: v, date: c.date, items: pending });
    }
    pending = [];
  }
}
if (pending.length) {
  releases.push({ version: "Unreleased", date: commits.at(-1).date, items: pending });
}
const key = (v) => (v === "Unreleased" ? [1e9, 0, 0] : v.split(".").map(Number));
releases.sort((a, b) => { const x = key(a.version), y = key(b.version); return y[0] - x[0] || y[1] - x[1] || y[2] - x[2]; });
writeFileSync(out, JSON.stringify(releases, null, 1) + "\n");
console.log(`${releases.length} releases, ${releases.reduce((n, r) => n + r.items.length, 0)} entries -> ${out}`);
