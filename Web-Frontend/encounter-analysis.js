// The analysis panels of the encounter page and the pull comparison, for fights whose uploads carry `analysis` (clients from the combat-analysis release on):
//   - the data-quality badge (reliable / derived / estimated / not available) and its legend
//   - the damage curve: cumulative boss damage of the group over the fight, the damage per 5 seconds under it, the boss's health as a second line
//   - the players table: boss damage and damage to adds, how long each player was alive, deaths
//   - damage taken by source (boss / adds / other) and skill, a breakdown without any rating
//   - how long each effect ran on a player, and how much of that time the player was dead or without action
//   - the pull comparison chart: two cumulative curves on one time axis
// Kept out of app.js; app.js hands in the helpers it already has. Everything here is drawn only from what the server sends; a missing value is "not available", never 0.
const SVG_NS = "http://www.w3.org/2000/svg";
const PALETTE = ["#5aa9ff", "#f0b030", "#4cc38a", "#e8d27a", "#9b7bff", "#ff8a65", "#4dd0e1", "#f06292", "#aed581", "#ba68c8"];
const WIDTH = 1790;
const HEIGHT = 340;
const PAD = { left: 78, right: 54, top: 14, bottom: 30 };

/** Which quality level each figure has (see docs: the table of what the data stream delivers). */
export const QUALITY = {
  bossDamage: "certain",
  addDamage: "certain",
  taken: "certain",
  deaths: "certain",
  alive: "derived",
  bossFocus: "derived",
  buffUsable: "derived",
  curve: "certain",
  bossHp: "certain",
};

function svg(name, attrs = {}, text) {
  const node = document.createElementNS(SVG_NS, name);
  for (const [key, value] of Object.entries(attrs)) {
    node.setAttribute(key, String(value));
  }
  if (text !== undefined) {
    node.textContent = text;
  }
  return node;
}

function clock(seconds) {
  const s = Math.max(0, Math.round(seconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}

function axisStep(duration) {
  return [5, 10, 15, 30, 60, 120, 300].find((s) => duration / s <= 12) ?? 300;
}

/** A short number for an axis: 1.2 M, 340 K. */
function shortNumber(v) {
  if (v >= 1_000_000) {
    return `${(v / 1_000_000).toFixed(v >= 10_000_000 ? 0 : 1)} M`;
  }
  if (v >= 1000) {
    return `${(v / 1000).toFixed(v >= 10_000 ? 0 : 1)} K`;
  }
  return String(Math.round(v));
}

/** The small badge saying how sure a figure is. `level` is certain, derived, estimated or unavailable. */
export function qualityBadge(level, deps) {
  const { el, t } = deps;
  return el("span", { className: `q-badge q-${level}`, title: t(`analysis.q.${level}Tip`), textContent: t(`analysis.q.${level}`) });
}

/** The explanation of the four levels, shown once under the new panels. */
export function qualityLegend(deps) {
  const { el, t } = deps;
  const levels = ["certain", "derived", "estimated", "unavailable"];
  return el("div", { className: "q-legend" }, [
    el("b", { textContent: t("analysis.q.legend") }),
    ...levels.map((level) => el("span", {}, [qualityBadge(level, deps), ` ${t(`analysis.q.${level}Tip`)}`])),
  ]);
}

/** The cells of a "no data" value: the words and the badge, never a 0. */
export function unavailableCell(deps) {
  return deps.el("span", { className: "q-na" }, [deps.t("analysis.q.unavailableValue"), " ", qualityBadge("unavailable", deps)]);
}

/** Cumulative sums of per-bucket damage as [seconds, total] points (the last point is clamped to the end of the fight). */
export function cumulativePoints(buckets, bucketSeconds, duration) {
  const points = [[0, 0]];
  let total = 0;
  buckets.forEach((value, i) => {
    total += value;
    points.push([Math.min((i + 1) * bucketSeconds, Math.max(duration, i * bucketSeconds + 1)), total]);
  });
  return points;
}

/** The value of a stepwise-linear series at a time. */
function valueAt(points, seconds) {
  let value = points.length > 0 ? points[0][1] : 0;
  for (const [s, v] of points) {
    if (s <= seconds) {
      value = v;
    } else {
      break;
    }
  }
  return value;
}

function lineChart({ duration, curves, hpCurves = [], bars = null, tAxis, yMax }) {
  const root = svg("svg", { viewBox: `0 0 ${WIDTH} ${HEIGHT}`, class: "tl-svg dc-svg", role: "img" });
  const plotW = WIDTH - PAD.left - PAD.right;
  const plotH = HEIGHT - PAD.top - PAD.bottom;
  const x = (seconds) => PAD.left + (Math.min(Math.max(seconds, 0), duration) / duration) * plotW;
  const y = (value) => PAD.top + plotH - (Math.min(value, yMax) / yMax) * plotH;

  // grid and axes
  const step = axisStep(duration);
  for (let s = 0; s <= duration; s += step) {
    root.append(svg("line", { x1: x(s), x2: x(s), y1: PAD.top, y2: PAD.top + plotH, class: "tl-grid" }));
    root.append(svg("text", { x: x(s) - 10, y: HEIGHT - 8, class: "tl-tick" }, clock(s)));
  }
  for (let i = 0; i <= 4; i++) {
    const v = (yMax / 4) * i;
    root.append(svg("line", { x1: PAD.left, x2: PAD.left + plotW, y1: y(v), y2: y(v), class: "tl-grid" }));
    root.append(svg("text", { x: PAD.left - 8, y: y(v) + 4, class: "tl-tick dc-ytick", "text-anchor": "end" }, shortNumber(v)));
    root.append(svg("text", { x: PAD.left + plotW + 8, y: y(v) + 4, class: "tl-tick dc-ytick" }, `${i * 25} %`));
  }

  // the damage per bucket as bars at the foot of the plot: a dip is a short bar
  if (bars) {
    const maxRate = Math.max(...bars.values, 1);
    bars.values.forEach((value, i) => {
      const from = i * bars.seconds;
      const to = Math.min((i + 1) * bars.seconds, duration);
      if (to <= from) {
        return;
      }
      const h = (value / maxRate) * plotH * 0.3;
      const bar = svg("rect", { x: x(from) + 0.5, y: PAD.top + plotH - h, width: Math.max(1, x(to) - x(from) - 1), height: h, class: "dc-bar" });
      bar.append(svg("title", {}, `${clock(from)} – ${clock(to)}`));
      root.append(bar);
    });
  }
  for (const hp of hpCurves) {
    const points = hp.points.map(([s, permille]) => `${x(s).toFixed(1)},${(PAD.top + plotH - (permille / 1000) * plotH).toFixed(1)}`).join(" ");
    root.append(svg("polyline", { points, class: "dc-hp", style: hp.color ? `stroke:${hp.color}` : "" }));
  }
  for (const curve of curves) {
    const points = curve.points.map(([s, v]) => `${x(s).toFixed(1)},${y(v).toFixed(1)}`).join(" ");
    root.append(svg("polyline", { points, class: "dc-line", style: `stroke:${curve.color}` }));
  }
  const cursor = svg("line", { y1: PAD.top, y2: PAD.top + plotH, class: "dc-cursor", visibility: "hidden" });
  root.append(cursor);
  const secondsOf = (event) => {
    const box = root.getBoundingClientRect();
    const px = ((event.clientX - box.left) / box.width) * WIDTH;
    return Math.min(Math.max(((px - PAD.left) / plotW) * duration, 0), duration);
  };
  return {
    root,
    secondsOf,
    showCursor(seconds) {
      cursor.setAttribute("x1", x(seconds));
      cursor.setAttribute("x2", x(seconds));
      cursor.setAttribute("visibility", "visible");
    },
    hideCursor() {
      cursor.setAttribute("visibility", "hidden");
    },
  };
}

/** The group's cumulative boss damage over the fight, the damage per 5 s under it and the boss's health; players can be switched on. Null without analysis. */
export function buildDamageCurve(timeline, deps, estimated = false) {
  const { el, t, formatNumber, classIcon, iconLabel } = deps;
  const analysis = timeline.analysis;
  if (!analysis || !Array.isArray(analysis.groupDamage) || analysis.groupDamage.length === 0) {
    return null;
  }
  const bucket = analysis.bucketSeconds || 5;
  const duration = Math.max(1, timeline.durationSeconds, analysis.groupDamage.length * bucket - bucket + 1);
  const group = cumulativePoints(analysis.groupDamage, bucket, duration);
  const players = (analysis.players ?? []).filter((p) => Array.isArray(p.damage) && p.damage.some((v) => v > 0));
  const shown = new Set();
  const panel = el("div", { className: "meter-panel tl-panel dc-panel" });
  const summary = el("p", { className: "tl-summary", textContent: t("analysis.curve.hint") });
  const chips = el("div", { className: "skill-tabs dc-players", role: "group" });

  const draw = () => {
    const playerCurves = players
      .filter((p) => shown.has(p.participantId))
      .map((p) => ({ color: PALETTE[players.indexOf(p) % PALETTE.length], points: cumulativePoints(p.damage, bucket, duration), name: p.name }));
    const total = group[group.length - 1][1];
    const chart = lineChart({
      duration,
      yMax: Math.max(total, 1),
      curves: [{ color: "var(--color-primary)", points: group }, ...playerCurves],
      hpCurves: timeline.bossHp.length >= 2 ? [{ points: timeline.bossHp }] : [],
      bars: { values: analysis.groupDamage, seconds: bucket },
    });
    const describe = (seconds) => {
      const i = Math.min(Math.floor(seconds / bucket), analysis.groupDamage.length - 1);
      const from = i * bucket;
      const to = Math.min(from + bucket, duration);
      const rate = analysis.groupDamage[i] / Math.max(1, to - from);
      const parts = [t("analysis.curve.at", { from: clock(from), to: clock(to), rate: formatNumber(Math.round(rate)), total: formatNumber(valueAt(group, to)) })];
      const hpPoint = [...timeline.bossHp].reverse().find(([s]) => s <= to);
      if (hpPoint) {
        parts.push(t("analysis.curve.hpAt", { pct: (hpPoint[1] / 10).toFixed(1) }));
      }
      return parts.join(" · ");
    };
    let pinned = null;
    chart.root.addEventListener("pointermove", (event) => {
      if (pinned !== null) {
        return;
      }
      const s = chart.secondsOf(event);
      chart.showCursor(s);
      summary.textContent = describe(s);
    });
    chart.root.addEventListener("pointerleave", () => {
      if (pinned === null) {
        chart.hideCursor();
        summary.textContent = t("analysis.curve.hint");
      }
    });
    chart.root.addEventListener("click", (event) => {
      const s = chart.secondsOf(event);
      if (pinned !== null && Math.abs(pinned - s) < 2) {
        pinned = null;
        return;
      }
      pinned = s;
      chart.showCursor(s);
      summary.textContent = describe(s);
    });
    chart.root.setAttribute("aria-label", t("analysis.curve.heading"));
    const legend = el("div", { className: "tl-legend" }, [
      el("span", {}, [el("i", { className: "tl-key dc-key-line" }), t("analysis.curve.cumulative")]),
      el("span", {}, [el("i", { className: "tl-key dc-key-bar" }), t("analysis.curve.rate")]),
      timeline.bossHp.length >= 2 ? el("span", {}, [el("i", { className: "tl-key dc-key-hp" }), t("analysis.curve.bossHp")]) : null,
    ].filter((x) => x !== null));
    const coverage = analysis.coverage && analysis.coverage.withAnalysis < analysis.coverage.players
      ? el("p", { className: "gear-note", textContent: t("analysis.curve.coverage", { with: analysis.coverage.withAnalysis, n: analysis.coverage.players }) })
      : null;
    panel.replaceChildren(
      el("div", { className: "meter-panel-header" }, [
        el("div", { className: "meter-panel-heading" }, [
          el("span", { className: "meter-panel-title", textContent: t("analysis.curve.heading") }),
          el("span", { className: "meter-panel-subtitle", textContent: t("analysis.curve.subtitle") }),
        ]),
        qualityBadge(estimated ? "estimated" : QUALITY.curve, deps),
      ]),
      chips,
      chart.root,
      summary,
      legend,
      ...(coverage ? [coverage] : []),
    );
    drawChips();
  };
  const drawChips = () => {
    chips.replaceChildren(
      el("span", { className: "dc-chips-label", textContent: t("analysis.curve.players") }),
      ...players.map((p, i) => {
        const b = el("button", { type: "button", className: `skill-tab${shown.has(p.participantId) ? " active" : ""}`, "aria-pressed": String(shown.has(p.participantId)) }, [
          el("i", { className: "dc-dot", style: `background:${PALETTE[i % PALETTE.length]}` }),
          iconLabel(classIcon(p.className), p.name),
        ]);
        b.addEventListener("click", () => {
          if (shown.has(p.participantId)) {
            shown.delete(p.participantId);
          } else {
            shown.add(p.participantId);
          }
          draw();
        });
        return b;
      }),
    );
  };
  draw();
  return panel;
}

/** Boss damage, damage to adds, time alive and deaths per player; the roster gives the boss damage as the page shows it elsewhere. */
export function buildAnalysisPlayers(timeline, roster, deps) {
  const { el, t, formatNumber, classIcon, iconLabel } = deps;
  const analysis = timeline.analysis;
  if (!analysis || !Array.isArray(analysis.players) || analysis.players.length === 0) {
    return null;
  }
  const rosterOf = new Map(roster.map((p) => [p.participantId, p]));
  const rows = analysis.players
    .map((p) => {
      const r = rosterOf.get(p.participantId);
      const boss = r ? r.totalDamage : (p.damage ?? []).reduce((a, b) => a + b, 0);
      return { p, r, boss };
    })
    .sort((a, b) => b.boss - a.boss);
  const pct = (v) => `${Math.round(v * 100)} %`;
  const head = (key, level) => el("th", {}, [t(key), " ", qualityBadge(level, deps)]);
  const body = rows.map(({ p, r, boss }) => {
    const add = typeof p.addDamage === "number" ? p.addDamage : null;
    const share = add !== null && boss + add > 0 ? boss / (boss + add) : null;
    return el("tr", {}, [
      el("td", {}, [iconLabel(classIcon(p.className), p.name)]),
      el("td", {}, [formatNumber(boss), r?.estimated ? el("span", {}, [" ", qualityBadge("estimated", deps)]) : ""]),
      el("td", {}, add === null ? [unavailableCell(deps)] : [formatNumber(add)]),
      el("td", {}, share === null ? [unavailableCell(deps)] : [pct(share)]),
      el("td", { title: typeof p.deadSeconds === "number" ? t("analysis.players.deadFor", { s: formatNumber(Math.round(p.deadSeconds)) }) : "" }, typeof p.aliveShare === "number" ? [pct(p.aliveShare)] : [unavailableCell(deps)]),
      el("td", {}, typeof p.deaths === "number" ? [formatNumber(p.deaths)] : [unavailableCell(deps)]),
    ]);
  });
  const coverage = analysis.coverage && analysis.coverage.withAnalysis < analysis.coverage.players
    ? el("p", { className: "gear-note", textContent: t("analysis.curve.coverage", { with: analysis.coverage.withAnalysis, n: analysis.coverage.players }) })
    : null;
  return el("div", { className: "meter-panel sb-panel" }, [
    el("div", { className: "meter-panel-header" }, [el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("analysis.players.heading") }), el("span", { className: "meter-panel-subtitle", textContent: t("analysis.players.subtitle") })])]),
    el("table", { className: "ranked-table compact skill-table an-table" }, [
      el("thead", {}, [
        el("tr", {}, [
          el("th", { textContent: t("focus.player") }),
          head("analysis.players.bossDamage", QUALITY.bossDamage),
          head("analysis.players.addDamage", QUALITY.addDamage),
          head("analysis.players.bossShare", "derived"),
          head("analysis.players.alive", QUALITY.alive),
          head("analysis.players.deaths", QUALITY.deaths),
        ]),
      ]),
      el("tbody", {}, body),
    ]),
    el("p", { className: "buff-legend", textContent: t("analysis.players.note") }),
    ...(coverage ? [coverage] : []),
  ]);
}

/** Damage taken by source (boss, adds, other) and skill. A breakdown, never a rating: a tank is meant to take damage. */
export function buildTakenSources(timeline, deps) {
  const { el, t, formatNumber, classIcon, iconLabel } = deps;
  const players = (timeline.analysis?.players ?? []).filter((p) => Array.isArray(p.taken) && p.taken.length > 0);
  if (players.length === 0) {
    return null;
  }
  const kinds = ["boss", "add", "other"];
  const totalOf = (p) => p.taken.reduce((sum, r) => sum + r.total, 0);
  const sorted = [...players].sort((a, b) => totalOf(b) - totalOf(a));
  const items = sorted.map((p) => {
    const total = Math.max(1, totalOf(p));
    const by = Object.fromEntries(kinds.map((k) => [k, p.taken.filter((r) => r.from === k).reduce((s, r) => s + r.total, 0)]));
    const bar = el("div", { className: "tk-bar" }, kinds.filter((k) => by[k] > 0).map((k) => el("i", { className: `tk-${k}`, style: `width:${(by[k] / total) * 100}%`, title: `${t(`analysis.taken.${k}`)} ${formatNumber(by[k])}` })));
    const rows = [...p.taken].sort((a, b) => b.total - a.total).map((r) =>
      el("tr", {}, [
        el("td", {}, [el("span", { className: `tk-tag tk-${r.from}`, textContent: t(`analysis.taken.${r.from}`) })]),
        el("td", { textContent: r.skill }),
        el("td", { textContent: formatNumber(r.hits) }),
        el("td", { textContent: formatNumber(r.max) }),
        el("td", { textContent: formatNumber(r.total) }),
        el("td", { textContent: `${((r.total / total) * 100).toFixed(1)} %` }),
      ]),
    );
    return el("details", { className: "tk-item" }, [
      el("summary", {}, [
        el("span", { className: "tk-name" }, [iconLabel(classIcon(p.className), p.name)]),
        bar,
        el("b", { className: "num", textContent: formatNumber(total) }),
      ]),
      el("table", { className: "ranked-table compact skill-table" }, [
        el("thead", {}, [el("tr", {}, ["analysis.taken.source", "analysis.taken.skill", "analysis.taken.hits", "analysis.taken.max", "analysis.taken.total", "analysis.taken.share"].map((k) => el("th", { textContent: t(k) })))]),
        el("tbody", {}, rows),
      ]),
    ]);
  });
  return el("div", { className: "meter-panel sb-panel" }, [
    el("div", { className: "meter-panel-header" }, [
      el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("analysis.taken.heading") }), el("span", { className: "meter-panel-subtitle", textContent: t("analysis.taken.subtitle") })]),
      qualityBadge(QUALITY.taken, deps),
    ]),
    el("div", { className: "tk-legend" }, kinds.map((k) => el("span", {}, [el("i", { className: `tl-key tk-${k}`, style: "width:14px;height:8px" }), t(`analysis.taken.${k}`)]))),
    ...items,
    el("p", { className: "buff-legend", textContent: t("analysis.taken.note") }),
  ]);
}

/** How long each effect ran on a player and how much of that time the player was dead or without action. Null when no effect carries the figures. */
export function buildBuffUsage(timeline, deps) {
  const { el, t, formatNumber, classIcon, iconLabel, getLocale } = deps;
  const players = timeline.players.filter((p) => p.buffs.some((b) => b.usable && b.usable.seconds > 0));
  if (players.length === 0) {
    return null;
  }
  const panel = el("div", { className: "meter-panel sb-panel" });
  const secs = (v) => `${formatNumber(Math.round(v))} s`;
  const draw = (current) => {
    const tabs = el(
      "div",
      { className: "skill-tabs", role: "group" },
      players.map((p) => {
        const b = el("button", { type: "button", className: `skill-tab${p === current ? " active" : ""}` }, [iconLabel(classIcon(p.className), p.name)]);
        b.addEventListener("click", () => draw(p));
        return b;
      }),
    );
    const list = current.buffs.filter((b) => b.usable && b.usable.seconds > 0).sort((a, b) => b.usable.seconds - a.usable.seconds);
    const rows = list.map((b) => {
      const u = b.usable;
      const label = b.names?.[getLocale()] ?? b.names?.en ?? b.name;
      const holder = el("span", { className: "sb-ico" });
      if (b.icon && b.iconKind) {
        const img = el("img", { src: `/images/aion2/icons/${b.iconKind}/${b.icon}.webp`, alt: "", loading: "lazy", width: 28, height: 28 });
        img.addEventListener("error", () => img.remove());
        holder.append(img);
      }
      const part = (v) => `${(Math.max(0, v) / Math.max(1, u.seconds)) * 100}%`;
      const bar = el("div", { className: "bu-bar", title: t("analysis.buff.barTip") }, [
        el("i", { className: "bu-active", style: `width:${part(u.active)}` }),
        el("i", { className: "bu-idle", style: `width:${part(u.idle)}` }),
        el("i", { className: "bu-dead", style: `width:${part(u.dead)}` }),
      ]);
      return el("tr", {}, [
        el("td", {}, [el("div", { className: "skill-name" }, [holder, el("span", { textContent: label })])]),
        el("td", { textContent: secs(u.seconds) }),
        el("td", { textContent: secs(u.dead) }),
        el("td", { textContent: secs(u.idle) }),
        el("td", { textContent: secs(u.active) }),
        el("td", { className: "bd-cell" }, [bar]),
      ]);
    });
    panel.replaceChildren(
      el("div", { className: "meter-panel-header" }, [
        el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("analysis.buff.heading") }), el("span", { className: "meter-panel-subtitle", textContent: t("analysis.buff.subtitle") })]),
        qualityBadge(QUALITY.buffUsable, deps),
      ]),
      tabs,
      el("table", { className: "ranked-table compact skill-table" }, [
        el("thead", {}, [el("tr", {}, ["analysis.buff.effect", "analysis.buff.ran", "analysis.buff.dead", "analysis.buff.idle", "analysis.buff.used", "analysis.buff.when"].map((k) => el("th", { textContent: t(k) })))]),
        el("tbody", {}, rows),
      ]),
      el("p", { className: "buff-legend", textContent: t("analysis.buff.note") }),
    );
  };
  draw(players[0]);
  return panel;
}

/** The pull comparison chart: both fights' cumulative boss damage on one time axis (and their boss health, dashed). Null when neither has a curve. */
export function buildCurveCompare(a, b, deps) {
  const { el, t, formatNumber } = deps;
  const bucket = 5;
  const curveOf = (s) => (Array.isArray(s?.damageCurve) && s.damageCurve.length > 0 ? s : null);
  const ca = curveOf(a);
  const cb = curveOf(b);
  if (!ca && !cb) {
    return null;
  }
  const colorA = "#5aa9ff";
  const colorB = "#f0b030";
  const cumulative = (s) => (s ? s.damageCurve.map((v, i) => [Math.min((i + 1) * bucket, Math.max(s.durationSeconds, i * bucket + 1)), v]) : null);
  const pa = cumulative(ca);
  const pb = cumulative(cb);
  const duration = Math.max(1, ca?.durationSeconds ?? 0, cb?.durationSeconds ?? 0);
  const total = Math.max(1, ...[pa, pb].filter(Boolean).map((p) => p[p.length - 1][1]));
  const curves = [];
  const hpCurves = [];
  if (pa) {
    curves.push({ color: colorA, points: [[0, 0], ...pa] });
  }
  if (pb) {
    curves.push({ color: colorB, points: [[0, 0], ...pb] });
  }
  if (Array.isArray(a?.bossHp) && a.bossHp.length >= 2) {
    hpCurves.push({ points: a.bossHp, color: colorA });
  }
  if (Array.isArray(b?.bossHp) && b.bossHp.length >= 2) {
    hpCurves.push({ points: b.bossHp, color: colorB });
  }
  const chart = lineChart({ duration, yMax: total, curves, hpCurves });
  const summary = el("p", { className: "tl-summary", textContent: t("analysis.curve.hint") });
  chart.root.addEventListener("pointermove", (event) => {
    const s = chart.secondsOf(event);
    chart.showCursor(s);
    const at = (p) => (p ? formatNumber(valueAt([[0, 0], ...p], s)) : t("analysis.q.unavailableValue"));
    summary.textContent = t("compare.pull.at", { time: clock(s), a: at(pa), b: at(pb) });
  });
  chart.root.addEventListener("pointerleave", () => {
    chart.hideCursor();
    summary.textContent = t("analysis.curve.hint");
  });
  chart.root.setAttribute("aria-label", t("compare.pull.chartHeading"));
  return el("div", { className: "meter-panel tl-panel dc-panel" }, [
    el("div", { className: "meter-panel-header" }, [
      el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("compare.pull.chartHeading") }), el("span", { className: "meter-panel-subtitle", textContent: t("compare.pull.chartSubtitle") })]),
      qualityBadge(QUALITY.curve, deps),
    ]),
    chart.root,
    summary,
    el("div", { className: "tl-legend" }, [
      el("span", {}, [el("i", { className: "tl-key", style: `width:18px;height:4px;background:${colorA}` }), t("compare.runA")]),
      el("span", {}, [el("i", { className: "tl-key", style: `width:18px;height:4px;background:${colorB}` }), t("compare.runB")]),
      el("span", {}, [el("i", { className: "tl-key dc-key-hp" }), t("analysis.curve.bossHp")]),
    ]),
  ]);
}
