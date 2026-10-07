// Builds Web-Frontend/changelog.json from the "Release X.Y.Z: <what changed>" commits of this
// repository (GitHub only keeps the newest 10 releases, git keeps all of them). Run it after
// the release commit has been made:  node Tools/changelog/gen.mjs
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const out = fileURLToPath(new URL("../../Web-Frontend/changelog.json", import.meta.url));
const log = execFileSync("git", ["log", "--format=%ad%x09%s", "--date=short"], { encoding: "utf8", maxBuffer: 1 << 26 });

const releases = [];
for (const line of log.split("\n")) {
  const m = /^(\d{4}-\d{2}-\d{2})\tRelease (\d+\.\d+\.\d+): (.+)$/.exec(line);
  if (m) {
    releases.push({ version: m[2], date: m[1], text: m[3].trim() });
  }
}
const key = (v) => v.split(".").map(Number);
releases.sort((a, b) => { const x = key(a.version), y = key(b.version); return y[0] - x[0] || y[1] - x[1] || y[2] - x[2]; });
writeFileSync(out, JSON.stringify(releases, null, 1) + "\n");
console.log(`${releases.length} releases -> ${out}`);
