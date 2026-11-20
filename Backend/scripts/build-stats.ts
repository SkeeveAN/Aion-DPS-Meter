/**
 * Trial run of "What do the best use?" on any copy of the database (read only, nothing is written):
 *   tsx scripts/build-stats.ts <database file> <classId 1-8> [group top10|top25|pct10|all] [playerId]
 * With a player id the character is compared with the group as well.
 * Prints how many characters stand behind every group and the most frequent answers, so the numbers can be judged before the job runs on the server.
 */
import Database from "better-sqlite3";
import { compareToGroup, computeClass, optimalCharacter, readAnswer, storeClass, GROUPS, type Group } from "../src/buildStats.js";

const [file, classArg, groupArg, playerArg] = process.argv.slice(2);
if (!file || !classArg) {
  console.error("usage: tsx scripts/build-stats.ts <database file> <classId> [group]");
  process.exit(1);
}
const classId = Number(classArg);
const group = (groupArg ?? "top25") as Group;
// A throw-away in-memory copy of the two result tables: the database file itself is opened read only.
const src = new Database(file, { readonly: true });
const mem = new Database(":memory:");
mem.exec(`create table build_stats (class_id integer, metric text, grp text, topic text, key text, option text, n integer, w real, base_n integer, base_w real, detail text,
          primary key(class_id, metric, grp, topic, key, option)) without rowid;
          create table build_stats_meta (class_id integer, metric text, grp text, size integer, weight real, built_at text, primary key(class_id, metric, grp)) without rowid;`);
const started = Date.now();
const result = await computeClass(src, classId, { patchDate: process.env.BUILD_STATS_PATCH_DATE || null });
storeClass(mem, classId, result);
console.log(`class ${classId}: ${result.rows.length} stored cells in ${((Date.now() - started) / 1000).toFixed(1)} s`);
for (const g of GROUPS) {
  const a = readAnswer(mem, classId, g)!;
  const by = (t: string) => a.questions.filter((q) => q.topic === t && !q.tooFew).length;
  console.log(`  ${g.padEnd(5)} ${String(a.size).padStart(6)} characters${a.tooFew ? " (too little)" : ""}; answerable: skills ${by("skill_level")}, stones ${by("skill_stone")}, boards ${by("board_node")}, gear slots ${by("gear_item")}, godstones ${by("gear_godstone")}, mana stones ${by("gear_manastone")}, pantheon ${by("pantheon")}`);
}
const opt = optimalCharacter(readAnswer(mem, classId, group)!);
console.log(`\n"optimal character" of ${group}:`);
console.log(`  skills (median level): ${opt.skills.slice(0, 12).map((s) => `${s.skill}=${s.level}`).join(" ")}${opt.skills.length > 12 ? " …" : ""}`);
console.log(`  skill stones: ${opt.stones.length} choices${opt.stones[0] ? `, e.g. skill ${opt.stones[0].skill} slot ${opt.stones[0].slot} -> ${opt.stones[0].choice} (${(opt.stones[0].share * 100).toFixed(0)} %)` : ""}`);
console.log(`  gear: ${opt.gear.slice(0, 6).map((g) => `slot ${g.slot}: item ${g.itemId} (${(g.share * 100).toFixed(0)} %)`).join("; ")}`);
for (const b of opt.boards) {
  console.log(`  board ${b.board}: most frequent real build ${b.build ? `${b.build.nodes.length} nodes, ${(b.build.share * 100).toFixed(0)} % of the group` : "none shared by 3 or more"}; ${b.commonNodes.length} nodes held by half or more`);
}

if (playerArg) {
  const c = compareToGroup(src, Number(playerArg), group, "power", mem);
  if (!c) {
    console.log(`\nplayer ${playerArg}: no comparison (no profile or another class)`);
  } else {
    console.log(`\nplayer ${playerArg} against ${group}: rank ${c.player.rank ?? "?"} of ${c.player.ranked}, power ${c.player.power ?? "?"}${c.player.inGroup ? " (is in the group)" : ""}`);
    const worse = c.skills.filter((s) => s.diff < 0).sort((a, b) => a.diff - b.diff).slice(0, 5);
    console.log(`  skills compared: ${c.skills.length}; furthest below the median: ${worse.map((s) => `${s.skill} ${s.own} vs ${s.median}`).join(", ") || "none"}`);
    console.log(`  gear slots compared: ${c.gear.length}, same piece as most of the group in ${c.gear.filter((g) => g.same).length}`);
    for (const b of c.boards) {
      console.log(`  board ${b.board}: ${b.ownNodes} nodes, lacks ${b.missing.length} of ${b.commonNodes} common ones, ${b.rare.length} rare ones${b.typicalOverlap !== null ? `, ${(b.typicalOverlap * 100).toFixed(0)} % of the typical build` : ""}`);
    }
  }
}
