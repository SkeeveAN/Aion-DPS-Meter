// The time panels of the encounter page, for fights whose uploads carry a time axis (clients from 1.9.0 on):
//   1. the group timeline: every skill use of every player on one axis, the boss's health above, a band where a group effect ran on the player, a cross where the player died;
//      drag over a part of it to see what it holds (skill uses, deaths, how far the boss's health fell)
//   2. buffs per skill: for each skill of one player, which effects were on the player at the moments it was used
// Kept out of app.js; app.js hands in the helpers it already has.
const SVG_NS = "http://www.w3.org/2000/svg";
const PALETTE = ["#5aa9ff", "#f0b030", "#4cc38a", "#e8d27a", "#9b7bff", "#ff8a65", "#4dd0e1", "#f06292", "#aed581", "#ba68c8"];
const WIDTH = 1790;
const LABEL = 150;
const ROW = 42;
const HP_ROW = 56;
const AXIS = 28;

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

/** A readable step for the time axis. */
function axisStep(duration) {
  return [5, 10, 15, 30, 60, 120].find((s) => duration / s <= 14) ?? 120;
}

function skillLabel(s, getLocale) {
  return s.names?.[getLocale()] ?? s.names?.en ?? s.name;
}

function groupSpans(player) {
  return player.buffs.filter((b) => b.kind === "group").flatMap((b) => b.spans);
}

/** The union of spans as ordered separate spans, so a band is drawn once. */
function mergeSpans(spans) {
  const merged = [];
  for (const [s, e] of [...spans].sort((a, b) => a[0] - b[0])) {
    if (merged.length > 0 && s <= merged[merged.length - 1][1]) {
      merged[merged.length - 1][1] = Math.max(merged[merged.length - 1][1], e);
    } else {
      merged.push([s, e]);
    }
  }
  return merged;
}

export function buildGroupTimeline(timeline, deps) {
  const { el, t, formatNumber, classIcon, iconLabel, getLocale } = deps;
  const players = timeline.players;
  const last = Math.max(timeline.durationSeconds, ...players.flatMap((p) => p.uses.map((u) => u[0])));
  const duration = Math.max(1, last);
  const x = (seconds) => LABEL + (Math.min(Math.max(seconds, 0), duration) / duration) * (WIDTH - LABEL - 20);
  const rowsTop = HP_ROW;
  const height = rowsTop + players.length * ROW + AXIS;
  const root = svg("svg", { viewBox: `0 0 ${WIDTH} ${height}`, class: "tl-svg", role: "img", "aria-label": t("timeline.heading") });

  // the boss's health
  root.append(svg("text", { x: 0, y: 24, class: "tl-label" }, t("timeline.bossHealth")));
  if (timeline.bossHp.length >= 2) {
    const points = timeline.bossHp.map(([s, permille]) => `${x(s).toFixed(1)},${(HP_ROW - 10 - (permille / 1000) * (HP_ROW - 22)).toFixed(1)}`).join(" ");
    root.append(svg("polyline", { points, class: "tl-hp" }));
  }

  players.forEach((p, i) => {
    const y = rowsTop + i * ROW;
    const color = PALETTE[i % PALETTE.length];
    root.append(svg("rect", { x: LABEL, y: y + 4, width: WIDTH - LABEL - 20, height: ROW - 10, rx: 4, class: "tl-lane" }));
    root.append(svg("text", { x: 0, y: y + ROW / 2 + 4, class: "tl-name" }, p.name));
    for (const [s, e] of mergeSpans(groupSpans(p))) {
      root.append(svg("rect", { x: x(s), y: y + ROW - 11, width: Math.max(1, x(e) - x(s)), height: 4, class: "tl-group" }));
    }
    for (const [at, idx] of p.uses) {
      const s = p.skills[idx];
      const tick = svg("rect", { x: x(at).toFixed(1), y: y + 8, width: 2.5, height: ROW - 22, fill: color });
      tick.append(svg("title", {}, `${skillLabel(s, getLocale)} · ${clock(at)}`));
      root.append(tick);
    }
    for (const d of p.deaths) {
      root.append(svg("text", { x: x(d) - 5, y: y + ROW / 2 + 5, class: "tl-death" }, "✝"));
    }
  });

  const step = axisStep(duration);
  const bottom = rowsTop + players.length * ROW;
  for (let s = 0; s <= duration; s += step) {
    root.append(svg("line", { x1: x(s), x2: x(s), y1: rowsTop - 4, y2: bottom, class: "tl-grid" }));
    root.append(svg("text", { x: x(s) - 10, y: bottom + 18, class: "tl-tick" }, clock(s)));
  }

  // the part picked with the mouse
  const brush = svg("rect", { y: rowsTop - 4, height: bottom - rowsTop + 4, class: "tl-brush", visibility: "hidden" });
  root.append(brush);
  const summary = el("p", { className: "tl-summary", textContent: t("timeline.hint") });
  const toSeconds = (event) => {
    const box = root.getBoundingClientRect();
    const px = ((event.clientX - box.left) / box.width) * WIDTH;
    return ((px - LABEL) / (WIDTH - LABEL - 20)) * duration;
  };
  const hpAt = (seconds) => {
    let value = null;
    for (const [s, permille] of timeline.bossHp) {
      if (s <= seconds) {
        value = permille;
      }
    }
    return value;
  };
  const describe = (from, to) => {
    const uses = players.reduce((sum, p) => sum + p.uses.filter(([at]) => at >= from && at <= to).length, 0);
    const deaths = players.reduce((sum, p) => sum + p.deaths.filter((d) => d >= from && d <= to).length, 0);
    const hpFrom = hpAt(from);
    const hpTo = hpAt(to);
    const parts = [t("timeline.selection", { from: clock(from), to: clock(to), uses: formatNumber(uses) })];
    if (deaths > 0) {
      parts.push(t("timeline.deaths", { n: deaths }));
    }
    if (hpFrom !== null && hpTo !== null) {
      parts.push(t("timeline.bossFell", { from: (hpFrom / 10).toFixed(1), to: (hpTo / 10).toFixed(1) }));
    }
    return parts.join(" · ");
  };
  let anchor = null;
  root.addEventListener("pointerdown", (event) => {
    anchor = toSeconds(event);
    root.setPointerCapture(event.pointerId);
    brush.setAttribute("visibility", "hidden");
  });
  root.addEventListener("pointermove", (event) => {
    if (anchor === null) {
      return;
    }
    const a = Math.min(Math.max(anchor, 0), duration);
    const b = Math.min(Math.max(toSeconds(event), 0), duration);
    const from = Math.min(a, b);
    const to = Math.max(a, b);
    brush.setAttribute("x", x(from));
    brush.setAttribute("width", Math.max(1, x(to) - x(from)));
    brush.setAttribute("visibility", "visible");
    summary.textContent = describe(from, to);
  });
  const stop = () => {
    anchor = null;
  };
  root.addEventListener("pointerup", stop);
  root.addEventListener("pointercancel", stop);
  root.addEventListener("dblclick", () => {
    brush.setAttribute("visibility", "hidden");
    summary.textContent = t("timeline.hint");
  });

  const legend = el(
    "div",
    { className: "tl-legend" },
    [
      el("span", {}, [el("i", { className: "tl-key tl-key-use" }), t("timeline.legendUse")]),
      el("span", {}, [el("i", { className: "tl-key tl-key-group" }), t("timeline.legendGroup")]),
      el("span", {}, ["✝ ", t("timeline.legendDeath")]),
    ],
  );
  return el("div", { className: "meter-panel tl-panel" }, [
    el("div", { className: "meter-panel-header" }, [el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("timeline.heading") }), el("span", { className: "meter-panel-subtitle", textContent: t("timeline.subtitle") })])]),
    root,
    summary,
    legend,
  ]);
}

export function buildBuffsPerSkill(timeline, deps) {
  const { el, t, formatNumber, classLabel, classIcon, iconLabel, getLocale } = deps;
  const players = timeline.players.filter((p) => p.skillBuffs.length > 0);
  if (players.length === 0) {
    return null;
  }
  const panel = el("div", { className: "meter-panel sb-panel" });
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
    const buffOf = new Map(current.buffs.map((b) => [b.skillId, b]));
    const tile = (b) => {
      const label = b.names?.[getLocale()] ?? b.names?.en ?? b.name;
      const holder = el("span", { className: "sb-ico" });
      if (b.icon && b.iconKind) {
        const img = el("img", { src: `/images/aion2/icons/${b.iconKind}/${b.icon}.webp`, alt: "", loading: "lazy", width: 22, height: 22 });
        img.addEventListener("error", () => img.remove());
        holder.append(img);
      }
      return { holder, label };
    };
    const rows = current.skillBuffs.map((line) => {
      const skill = current.skills[line.skill];
      const art = el("span", { className: "skill-ico" });
      if (skill.icon) {
        const img = el("img", { src: `/images/aion2/icons/skill/${skill.icon}.webp`, alt: "", loading: "lazy", width: 28, height: 28 });
        img.addEventListener("error", () => img.remove());
        art.append(img);
      }
      const chips = line.buffs
        .map((share) => {
          const b = buffOf.get(share.skillId);
          if (!b) {
            return null;
          }
          const { holder, label } = tile(b);
          return el("span", { className: `sb-chip sb-${b.kind}`, title: label }, [holder, el("span", { textContent: label }), el("b", { textContent: `${Math.round(share.share * 100)} %` })]);
        })
        .filter((c) => c !== null);
      return el("tr", {}, [
        el("td", { className: "skill-name" }, [art, el("span", { textContent: skillLabel(skill, getLocale) })]),
        el("td", { textContent: formatNumber(line.uses) }),
        el("td", { className: "sb-chips" }, chips.length > 0 ? chips : [el("span", { className: "gear-note", textContent: t("skillBuffs.none") })]),
      ]);
    });
    panel.replaceChildren(
      el("div", { className: "meter-panel-header" }, [el("div", { className: "meter-panel-heading" }, [el("span", { className: "meter-panel-title", textContent: t("skillBuffs.heading") }), el("span", { className: "meter-panel-subtitle", textContent: t("skillBuffs.subtitle") })])]),
      tabs,
      el("table", { className: "ranked-table compact skill-table" }, [
        el("thead", {}, [el("tr", {}, ["skillBuffs.skill", "skillBuffs.uses", "skillBuffs.running"].map((k) => el("th", { textContent: t(k) })))]),
        el("tbody", {}, rows),
      ]),
    );
  };
  draw(players[0]);
  return panel;
}
