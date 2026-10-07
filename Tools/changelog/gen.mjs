// Builds Web-Frontend/changelog.json from the git history of the Windows client (GitHub only keeps
// the newest 10 releases, git keeps everything). Run it after the release commit has been made:
//   node Tools/changelog/gen.mjs
// The client's version is <Version> in Client/AionDPS.csproj. Walking the commits that touch
// Client/ from oldest to newest, every commit that changes that version closes a release; the
// commits since the previous one are what went into it. A "Release X.Y.Z: <text>" commit counts
// with its own text; bare version bumps and merges add nothing.
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const out = fileURLToPath(new URL("../../Web-Frontend/changelog.json", import.meta.url));
const git = (...args) => execFileSync("git", args, { encoding: "utf8", maxBuffer: 1 << 26 });

const commits = git("log", "--reverse", "--format=%H%x09%ad%x09%s", "--date=short", "--", "Client")
  .split("\n").filter(Boolean).map((l) => { const [hash, date, ...s] = l.split("\t"); return { hash, date, subject: s.join("\t") }; });

const versionAt = (hash) => /<Version>([^<]+)<\/Version>/.exec(git("show", `${hash}:Client/AionDPS.csproj`))?.[1] ?? null;

const releases = [];
let current = null;
let pending = [];
for (const c of commits) {
  const v = versionAt(c.hash);
  const rel = /^Release (\d+\.\d+\.\d+)(?:: (.+))?$/.exec(c.subject);
  const noise = /^(Merge |Bump (the )?version|Version \d|Release workflow)/.test(c.subject);
  let text = rel ? rel[2] : noise ? null : c.subject.replace(/;? ?version \d+\.\d+\.\d+$/i, "");
  if (text) {
    pending.push(text);
  }
  if (v && v !== current) {
    current = v;
    if (pending.length) {
      releases.push({ version: v, date: c.date, items: pending });
    }
    pending = [];
  }
}
const key = (v) => v.split(".").map(Number);
releases.sort((a, b) => { const x = key(a.version), y = key(b.version); return y[0] - x[0] || y[1] - x[1] || y[2] - x[2]; });
writeFileSync(out, JSON.stringify(releases, null, 1) + "\n");
console.log(`${releases.length} releases, ${releases.reduce((n, r) => n + r.items.length, 0)} entries -> ${out}`);
