"""Builds a stand-alone preview page of the "DPS per class on a boss" box plot from a local copy of the database.
Usage: python3 -I build.py <dpsmeter.sqlite> <out.html>
Only aggregates go into the page (per boss and class: the DPS of every counted row, no names). The page is not part of the website."""
import json, sqlite3, sys
db, out = sys.argv[1], sys.argv[2]
c = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
rows = c.execute("""
  select coalesce(b.name_en, b.name), p.class_name, p.total_damage * 1.0 / e.duration_seconds
  from encounter_participants p
  join encounters e on e.id = p.encounter_id
  join bosses b on b.id = e.boss_id
  where p.estimated = 0 and e.duration_seconds >= 10 and p.total_damage > 0 and b.is_trash_mob = 0
""").fetchall()
data = {}
for boss, cls, dps in rows:
    data.setdefault(boss, {}).setdefault(cls, []).append(round(dps))
data = {b: {k: sorted(v) for k, v in cs.items()} for b, cs in data.items() if sum(len(v) for v in cs.values()) >= 20}
html = open(__file__.replace("build.py", "template.html"), encoding="utf-8").read().replace("/*DATA*/null", json.dumps(data, ensure_ascii=False))
open(out, "w", encoding="utf-8").write(html)
print(len(data), "bosses,", sum(len(v) for cs in data.values() for v in cs.values()), "rows ->", out)
