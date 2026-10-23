// The statistics view of a boss page (?view=stats): three charts and the top 10 of every class, all boxes of one three-column grid.
//   1. the iDPS spread per class (box plot with the real rows as dots, a marker for your own value)
//   2. the kill times (histogram)
//   3. the share of every class among the rows (bars)
// Kept out of app.js on purpose; app.js hands in the helpers it already has (el, t, the row builders ...).
const SVG_NS = "http://www.w3.org/2000/svg";
const MIN_ROWS_FOR_A_BOX = 3;
const FEW_ROWS = 8;
const BOX_WIDTH = 470; // one box of the grid

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

// One tooltip for all charts. Values lead, names follow; everything goes in as text, never as markup.
let tip;
function showTip(event, title, lines) {
  if (!tip) {
    tip = document.createElement("div");
    tip.className = "stats-tip";
    tip.setAttribute("role", "tooltip");
    document.body.appendChild(tip);
  }
  tip.replaceChildren();
  const head = document.createElement("div");
  head.className = "stats-tip-title";
  head.textContent = title;
  tip.appendChild(head);
  for (const [label, value] of lines) {
    const row = document.createElement("div");
    row.className = "stats-tip-row";
    const strong = document.createElement("strong");
    strong.textContent = value;
    const name = document.createElement("span");
    name.textContent = label;
    row.append(strong, name);
    tip.appendChild(row);
  }
  tip.style.display = "block";
  const x = Math.min(event.clientX + 14, window.innerWidth - tip.offsetWidth - 8);
  const y = Math.min(event.clientY + 14, window.innerHeight - tip.offsetHeight - 8);
  tip.style.left = `${Math.max(8, x) + window.scrollX}px`;
  tip.style.top = `${Math.max(8, y) + window.scrollY}px`;
}
function hideTip() {
  if (tip) {
    tip.style.display = "none";
  }
}
function hover(node, title, lines) {
  node.addEventListener("pointermove", (e) => showTip(e, title, lines));
  node.addEventListener("pointerleave", hideTip);
  node.addEventListener("blur", hideTip);
  node.addEventListener("focus", () => {
    const r = node.getBoundingClientRect();
    showTip({ clientX: r.left + r.width / 2, clientY: r.top }, title, lines);
  });
  node.setAttribute("tabindex", "0");
}

function niceTicks(max, count = 4) {
  const raw = max / count;
  const magnitude = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= raw) ?? raw;
  const ticks = [];
  for (let v = 0; v <= max + step * 0.001; v += step) {
    ticks.push(v);
  }
  return ticks;
}

const compact = (n) => (n >= 10000 ? `${Math.round(n / 1000)}K` : n >= 1000 ? `${(n / 1000).toFixed(1).replace(/\.0$/, "")}K` : String(Math.round(n)));
const mmss = (s) => `${Math.floor(Math.round(s) / 60)}:${String(Math.round(s) % 60).padStart(2, "0")}`;
const percentBelow = (values, x) => Math.round((values.filter((v) => v < x).length / Math.max(values.length, 1)) * 100);

/** The box plot: one row per class (strongest median first); bar = middle half, line = lowest to highest, dots = the rows themselves. */
function buildBoxPlot(boxes, deps, yourDps) {
  const { t, formatNumber, classLabel } = deps;
  const rows = boxes.filter((b) => b.samples >= MIN_ROWS_FOR_A_BOX);
  if (rows.length === 0) {
    return null;
  }
  const left = 112;
  const right = 18;
  const rowHeight = 36;
  const top = 22;
  const height = top + rows.length * rowHeight + 30;
  const chart = svg("svg", { viewBox: `0 0 ${BOX_WIDTH} ${height}`, class: "boxplot-svg", role: "img", "aria-label": t("stats.boxHeading") });
  const max = Math.max(...rows.map((b) => b.max), yourDps) * 1.04;
  const x = (value) => left + (value / max) * (BOX_WIDTH - left - right);
  for (const value of niceTicks(max)) {
    chart.appendChild(svg("line", { x1: x(value), x2: x(value), y1: top - 8, y2: height - 24, class: "boxplot-grid" }));
    chart.appendChild(svg("text", { x: x(value), y: height - 8, "text-anchor": "middle", class: "boxplot-axis" }, compact(value)));
  }

  rows.forEach((box, i) => {
    const y = top + i * rowHeight + rowHeight / 2;
    const group = svg("g", { class: "boxplot-row" });
    group.appendChild(svg("rect", { x: 0, y: y - rowHeight / 2, width: BOX_WIDTH, height: rowHeight, class: "boxplot-hit" }));
    group.appendChild(svg("text", { x: left - 10, y: y + 4, "text-anchor": "end", class: "boxplot-label" }, `${classLabel(box.className)} (${box.samples})`));
    group.appendChild(svg("line", { x1: x(box.min), x2: x(box.max), y1: y, y2: y, class: "boxplot-whisker" }));
    group.appendChild(svg("rect", { x: x(box.q1), y: y - 9, width: Math.max(2, x(box.q3) - x(box.q1)), height: 18, rx: 3, class: "boxplot-box" }));
    // the real rows, spread a little up and down so equal values do not hide each other
    (box.values ?? []).forEach((value, k) => group.appendChild(svg("circle", { cx: x(value), cy: y + (((k * 7) % 11) - 5) * 0.9, r: 2, class: "boxplot-dot" })));
    group.appendChild(svg("line", { x1: x(box.median), x2: x(box.median), y1: y - 12, y2: y + 12, class: "boxplot-median" }));
    const lines = [
      [t("stats.max"), formatNumber(Math.round(box.max))],
      ["Q3", formatNumber(Math.round(box.q3))],
      [t("stats.median"), formatNumber(Math.round(box.median))],
      ["Q1", formatNumber(Math.round(box.q1))],
      [t("stats.min"), formatNumber(Math.round(box.min))],
      [t("stats.rows"), String(box.samples)],
    ];
    if (yourDps > 0 && box.values?.length) {
      lines.push([t("stats.you"), t("stats.beatsPct", { p: percentBelow(box.values, yourDps) })]);
    }
    hover(group, classLabel(box.className), lines);
    chart.appendChild(group);
  });

  if (yourDps > 0) {
    chart.appendChild(svg("line", { x1: x(yourDps), x2: x(yourDps), y1: top - 8, y2: height - 24, class: "boxplot-you" }));
    chart.appendChild(svg("text", { x: Math.min(x(yourDps) + 4, BOX_WIDTH - 60), y: top - 10, class: "boxplot-you-label" }, `${t("stats.you")} ${formatNumber(yourDps)}`));
  }
  return chart;
}

/** The numbers behind the box plot, for everyone who cannot or does not want to hover. */
function buildBoxTable(boxes, deps) {
  const { el, t, formatNumber, classLabel } = deps;
  const rows = boxes.filter((b) => b.samples >= MIN_ROWS_FOR_A_BOX);
  const head = ["", t("stats.rows"), t("stats.min"), "Q1", t("stats.median"), "Q3", t("stats.max")];
  return el("details", { className: "boxplot-table" }, [
    el("summary", { textContent: t("stats.showNumbers") }),
    el("table", { className: "ranked-table compact" }, [
      el("thead", {}, [el("tr", {}, head.map((h) => el("th", { textContent: h })))]),
      el(
        "tbody",
        {},
        rows.map((b) => el("tr", {}, [classLabel(b.className), b.samples, ...[b.min, b.q1, b.median, b.q3, b.max].map((v) => formatNumber(Math.round(v)))].map((cell) => el("td", { textContent: String(cell) })))),
      ),
    ]),
  ]);
}

/** Histogram of the kill times: columns of one hue, the median marked and labelled; hover gives range and count. */
function buildTimeChart(durations, deps) {
  const { t } = deps;
  if (durations.length < 5) {
    return null;
  }
  const sorted = [...durations].sort((a, b) => a - b);
  const lo = sorted[Math.floor(sorted.length * 0.02)];
  const hi = sorted[Math.ceil(sorted.length * 0.98) - 1];
  const bins = Math.min(12, Math.max(5, Math.round(Math.sqrt(sorted.length))));
  const span = Math.max(hi - lo, 1);
  const width = span / bins;
  const counts = new Array(bins).fill(0);
  for (const d of sorted) {
    if (d >= lo && d <= hi) {
      counts[Math.min(bins - 1, Math.floor((d - lo) / width))]++;
    }
  }
  const median = sorted[Math.floor(sorted.length / 2)];
  const left = 34;
  const right = 12;
  const top = 26;
  const plotH = 170;
  const height = top + plotH + 34;
  const chart = svg("svg", { viewBox: `0 0 ${BOX_WIDTH} ${height}`, class: "boxplot-svg", role: "img", "aria-label": t("stats.timeHeading") });
  const maxCount = Math.max(...counts);
  const x = (v) => left + ((v - lo) / span) * (BOX_WIDTH - left - right);
  const y = (c) => top + plotH - (c / maxCount) * plotH;
  for (const tick of niceTicks(maxCount, 3)) {
    chart.appendChild(svg("line", { x1: left, x2: BOX_WIDTH - right, y1: y(tick), y2: y(tick), class: "boxplot-grid" }));
    chart.appendChild(svg("text", { x: left - 6, y: y(tick) + 4, "text-anchor": "end", class: "boxplot-axis" }, String(tick)));
  }
  const barW = Math.min(24, (BOX_WIDTH - left - right) / bins - 2);
  counts.forEach((count, i) => {
    const from = lo + i * width;
    const cx = x(from + width / 2);
    const group = svg("g");
    group.appendChild(svg("rect", { x: x(from), y: top, width: (BOX_WIDTH - left - right) / bins, height: plotH, class: "boxplot-hit" }));
    if (count > 0) {
      group.appendChild(svg("path", { d: `M${cx - barW / 2},${top + plotH} V${y(count) + 4} a4,4 0 0 1 4,-4 H${cx + barW / 2 - 4} a4,4 0 0 1 4,4 V${top + plotH} Z`, class: "hist-bar" }));
    }
    hover(group, `${mmss(from)} – ${mmss(from + width)}`, [[t("stats.runs"), String(count)]]);
    chart.appendChild(group);
  });
  for (let i = 0; i <= 4; i++) {
    const v = lo + (span / 4) * i;
    chart.appendChild(svg("text", { x: x(v), y: height - 10, "text-anchor": i === 4 ? "end" : i === 0 ? "start" : "middle", class: "boxplot-axis" }, mmss(v)));
  }
  chart.appendChild(svg("line", { x1: x(median), x2: x(median), y1: top - 6, y2: top + plotH, class: "boxplot-median hist-median" }));
  chart.appendChild(svg("text", { x: Math.min(x(median) + 5, BOX_WIDTH - 90), y: top - 9, class: "boxplot-label" }, `${t("stats.median")} ${mmss(median)}`));
  return chart;
}

/** The share of every class among the rows: bars from one baseline, the value at the tip. */
function buildShareChart(boxes, deps) {
  const { t, formatNumber, classLabel } = deps;
  const total = boxes.reduce((sum, b) => sum + b.samples, 0);
  const rows = [...boxes].sort((a, b) => b.samples - a.samples);
  if (rows.length === 0) {
    return null;
  }
  const left = 112;
  const right = 64;
  const rowHeight = 30;
  const height = rows.length * rowHeight + 8;
  const chart = svg("svg", { viewBox: `0 0 ${BOX_WIDTH} ${height}`, class: "boxplot-svg", role: "img", "aria-label": t("stats.shareHeading") });
  const max = rows[0].samples;
  rows.forEach((box, i) => {
    const y = 4 + i * rowHeight;
    const w = Math.max(6, (box.samples / max) * (BOX_WIDTH - left - right));
    const group = svg("g");
    group.appendChild(svg("rect", { x: 0, y, width: BOX_WIDTH, height: rowHeight, class: "boxplot-hit" }));
    group.appendChild(svg("text", { x: left - 10, y: y + rowHeight / 2 + 4, "text-anchor": "end", class: "boxplot-label" }, classLabel(box.className)));
    group.appendChild(svg("path", { d: `M${left},${y + 5} H${left + w - 4} a4,4 0 0 1 4,4 V${y + rowHeight - 9} a4,4 0 0 1 -4,4 H${left} Z`, class: "hist-bar" }));
    group.appendChild(svg("text", { x: left + w + 6, y: y + rowHeight / 2 + 4, class: "boxplot-label" }, `${Math.round((box.samples / total) * 100)} %`));
    hover(group, classLabel(box.className), [[t("stats.rows"), formatNumber(box.samples)], [t("stats.share"), `${((box.samples / total) * 100).toFixed(1)} %`]]);
    chart.appendChild(group);
  });
  return chart;
}

/**
 * @param stats the answer of /api/bosses/:id/stats: { boxes, durations, topByClass }
 * @param deps  helpers of app.js: el, t, formatNumber, classLabel, classIcon, iconLabel, playerCell, gp
 * @returns the nodes of the view
 */
export function buildBossStats(stats, deps) {
  const { el, t, formatNumber, classLabel, classIcon, iconLabel, playerCell, gp } = deps;
  const classNames = Object.keys(stats.topByClass ?? {}).sort();
  const boxes = stats.boxes ?? [];
  if (boxes.length === 0 && classNames.length === 0) {
    // With a gear score range chosen the filter stays, so another range can be picked.
    return [...buildGearFilters(stats, deps.gearHref, deps), el("p", { className: "empty", textContent: t(stats.gear?.selected || stats.power?.selected ? "gear.empty" : "leaderboard.emptyNoFights") })];
  }

  const empty = (key) => el("p", { className: "empty", textContent: t(key) });
  const plotHolder = el("div", { className: "boxplot-card" });
  const note = el("p", { className: "boxplot-note" });
  const input = el("input", { type: "number", min: "0", inputMode: "numeric", placeholder: t("stats.youPlaceholder"), className: "boxplot-you-input", "aria-label": t("stats.yourDps") });
  const draw = () => {
    const chart = buildBoxPlot(boxes, deps, Number(input.value) || 0);
    plotHolder.replaceChildren(...(chart ? [chart] : [empty("stats.tooFew")]));
    const thin = boxes.filter((b) => b.samples >= MIN_ROWS_FOR_A_BOX && b.samples < FEW_ROWS).length;
    note.textContent = t("stats.boxNote") + (thin ? ` ${t("stats.fewRows", { n: thin, rows: FEW_ROWS })}` : "");
  };
  input.addEventListener("input", draw);
  draw();

  const block = (title, ...children) => el("div", { className: "class-block boxplot-block" }, [el("h3", {}, [title]), ...children]);
  const boxBlock = block(t("stats.boxHeading"), el("label", { className: "boxplot-you-field" }, [el("span", { textContent: t("stats.yourDps") }), input]), plotHolder, note, buildBoxTable(boxes, deps));
  const timeChart = buildTimeChart(stats.durations ?? [], deps);
  const timeBlock = block(t("stats.timeHeading"), el("div", { className: "boxplot-card" }, [timeChart ?? empty("stats.tooFew")]));
  const shareChart = buildShareChart(boxes, deps);
  const shareBlock = block(t("stats.shareHeading"), el("div", { className: "boxplot-card" }, [shareChart ?? empty("stats.tooFew")]));

  // Three boxes side by side, so each one holds a compact table: rank, player, iDPS (the full row is on the encounter page).
  const compactTable = (rows) =>
    el("table", { className: "ranked-table compact" }, [
      el("thead", {}, [el("tr", {}, [el("th", { textContent: "#" }), el("th", { textContent: t("table.player") }), el("th", { textContent: t("participant.dps") })])]),
      el("tbody", {}, rows),
    ]);
  const classBlocks = classNames.map((className) => {
    const rows = stats.topByClass[className].map((p, i) =>
      el("tr", {}, [
        el("td", { textContent: String(i + 1) }),
        el("td", {}, [playerCell(p.faction, className, p.playerName, gp(`/encounters/${p.encounterId}`), p.serverName, p)]),
        el("td", { textContent: formatNumber(p.idps), title: `${t("table.damage")} ${formatNumber(p.totalDamage)}` }),
      ]),
    );
    return el("div", { className: "class-block" }, [el("h3", {}, [iconLabel(classIcon(className), classLabel(className))]), compactTable(rows)]);
  });

  const skillBlock = buildSkillStats(stats.skills, deps);
  return [el("section", {}, [el("h2", { textContent: t("stats.heading") }), ...buildGearFilters(stats, deps.gearHref, deps), el("div", { className: "class-grid class-grid-3" }, [boxBlock, timeBlock, shareBlock, ...(skillBlock ? [skillBlock] : []), ...classBlocks])])];
}

// The gear score filter above the rankings and statistics: runs whose known gear scores all lie in one range. A link per range, so the
// choice is part of the address (shareable, back button works). A second row does the same for the combat power NC lists for the characters
// (known for far more players than the gear score; a range of it starts with "p" in the address).
const GEAR_KINDS = {
  score: {
    label: "gear.label",
    note: "gear.note",
    ranges: [
      { key: "all", range: null },
      { key: "low", range: "-1500", text: (t, f) => t("gear.upTo", { max: f(1500) }) },
      { key: "mid", range: "1500-2000", text: (t, f) => t("gear.between", { min: f(1500), max: f(2000) }) },
      { key: "high", range: "2000-", text: (t, f) => t("gear.from", { min: f(2000) }) },
    ],
  },
  power: {
    label: "power.label",
    note: "power.note",
    ranges: [
      { key: "all", range: null },
      { key: "low", range: "p-75000", text: (t, f) => t("gear.upTo", { max: f(75000) }) },
      { key: "mid", range: "p75000-120000", text: (t, f) => t("gear.between", { min: f(75000), max: f(120000) }) },
      { key: "high", range: "p120000-", text: (t, f) => t("gear.from", { min: f(120000) }) },
    ],
  },
};

/**
 * @param gear   { runs, known, selected } from the API: how many runs have a known value and the range in use
 * @param hrefFor function(range or null) -> the address that shows this range
 * @param kind   "score" (gear score) or "power" (combat power)
 * @returns the filter row, or null when no run has a value at all
 */
export function buildGearFilter(gear, hrefFor, deps, kind = "score") {
  const { el, t, formatNumber } = deps;
  if (!gear || gear.known === 0) {
    return null;
  }
  const spec = GEAR_KINDS[kind];
  const prefix = kind === "power" ? "p" : "";
  const selectedKey = gear.selected ? `${prefix}${gear.selected.min > 0 ? gear.selected.min : ""}-${gear.selected.max < Number.MAX_SAFE_INTEGER ? gear.selected.max : ""}` : null;
  const seg = el(
    "div",
    { className: "lb-seg", role: "group" },
    spec.ranges.map(({ key, range, text }) => {
      const a = el("a", { href: hrefFor(range), textContent: key === "all" ? t("gear.all") : text(t, formatNumber) });
      a.setAttribute("aria-selected", String(range === selectedKey));
      return a;
    }),
  );
  return el("div", { className: "gear-filter" }, [
    el("div", { className: "lb-bar" }, [el("span", { className: "lb-bar-label", textContent: t(spec.label) }), seg]),
    el("span", { className: "gear-note", textContent: t(spec.note, { known: formatNumber(gear.known), runs: formatNumber(gear.runs) }) }),
  ]);
}

/** Both filter rows (gear score, combat power) of an API answer that carries `gear` and `power`; rows without any known value are left out. */
export function buildGearFilters(data, hrefFor, deps) {
  return [buildGearFilter(data.gear, hrefFor, deps, "score"), buildGearFilter(data.power, hrefFor, deps, "power")].filter((x) => x !== null);
}

/** The skill table of one class at this boss, with one tab per class. */
function buildSkillStats(classes, deps) {
  const { el, t, formatNumber, classLabel, classIcon, iconLabel, getLocale } = deps;
  if (!classes || classes.length === 0) {
    return null;
  }
  const block = el("div", { className: "class-block skill-stats" });
  const draw = (className) => {
    const current = classes.find((c) => c.className === className) ?? classes[0];
    const tabs = el(
      "div",
      { className: "skill-tabs", role: "group" },
      classes.map((c) => {
        const b = el("button", { type: "button", className: `skill-tab${c.className === current.className ? " active" : ""}` }, [iconLabel(classIcon(c.className), classLabel(c.className))]);
        b.addEventListener("click", () => draw(c.className));
        return b;
      }),
    );
    const top = current.skills[0]?.damageShare ?? 1;
    const percent = (v) => `${Math.round(v * 100)} %`;
    const row = (s) => {
      const name = s.names?.[getLocale()] ?? s.skill;
      const tile = el("span", { className: "skill-ico" });
      if (s.icon) {
        const img = el("img", { src: `/images/aion2/icons/skill/${s.icon}.webp`, alt: "", loading: "lazy", width: 28, height: 28 });
        img.addEventListener("error", () => img.remove());
        tile.append(img);
      }
      return el("tr", {}, [
        el("td", { className: "skill-name" }, [tile, el("span", { textContent: name })]),
        el("td", { className: "skill-share" }, [el("i", { style: `width:${top > 0 ? (s.damageShare / top) * 80 : 0}%` }), el("b", { textContent: percent(s.damageShare) })]),
        el("td", { textContent: percent(s.runShare) }),
        el("td", { textContent: formatNumber(Math.round(s.hitsPerRun)) }),
        el("td", { textContent: percent(s.critRate) }),
        el("td", { textContent: formatNumber(Math.round(s.averageHit)) }),
        el("td", { textContent: s.chainRate === null || s.chainRate === undefined ? "–" : percent(s.chainRate) }),
        el("td", { textContent: s.avgLevel === null || s.avgLevel === undefined ? "–" : formatNumber(s.avgLevel) }),
      ]);
    };
    const head = ["stats.skillColumn", "stats.skillShare", "stats.skillUsed", "stats.skillHits", "stats.skillCrit", "stats.skillAverage", "stats.skillChain", "stats.skillLevel"];
    block.replaceChildren(
      el("h3", {}, [t("stats.skillsHeading")]),
      tabs,
      el("table", { className: "ranked-table compact skill-table" }, [el("thead", {}, [el("tr", {}, head.map((k) => el("th", { textContent: t(k) })))]), el("tbody", {}, current.skills.map(row))]),
      el("p", { className: "boxplot-note", textContent: t("stats.skillNote", { n: formatNumber(current.samples) }) }),
    );
  };
  draw(classes[0].className);
  return block;
}

/**
 * The statistics of a dungeon: every boss with its runs, best group iDPS, average kill time and the median iDPS of its classes (each boss links to its own
 * statistics), and the share of the classes over the whole dungeon.
 * @param data the answer of /api/instances/:id/statistics: { instance, bosses, classShare }
 * @param deps helpers of app.js: el, t, formatNumber, classLabel, displayName, formatDuration, gp
 */
export function buildInstanceStats(data, deps) {
  const { el, t, formatNumber, classLabel, classIcon, displayName, formatDuration, gp } = deps;
  const fought = data.bosses.filter((b) => b.runCount > 0);
  if (fought.length === 0) {
    return [...buildGearFilters(data, deps.gearHref, deps), el("p", { className: "empty", textContent: t(data.gear?.selected || data.power?.selected ? "gear.empty" : "leaderboard.emptyNoFights") })];
  }
  const header = ["stats.bossColumn", "stats.runs", "stats.bestDps", "stats.avgTime", "stats.medianPerClass"];
  const table = el("table", { className: "ranked-table compact" }, [
    el("thead", {}, [el("tr", {}, header.map((key) => el("th", { textContent: t(key) })))]),
    el(
      "tbody",
      {},
      data.bosses.map((b) =>
        el("tr", {}, [
          el("td", {}, [b.slug ? el("a", { href: gp(`/bosses/${b.slug}/statistics`), textContent: displayName(b) }) : displayName(b)]),
          el("td", { textContent: formatNumber(b.runCount) }),
          el("td", { textContent: b.bestIdps ? formatNumber(b.bestIdps) : "–" }),
          el("td", { textContent: b.avgDurationSeconds ? formatDuration(b.avgDurationSeconds) : "–" }),
          // the class as its icon, its name and the figure in the tooltip
          el("td", {}, [
            el(
              "span",
              { className: "class-medians" },
              b.classes.slice(0, 6).map((c) => el("span", { className: "class-median", title: `${classLabel(c.className)}: ${formatNumber(Math.round(c.median))}` }, [classIcon(c.className), el("span", { textContent: formatNumber(Math.round(c.median)) })])),
            ),
          ]),
        ]),
      ),
    ),
  ]);
  const block = (title, ...children) => el("div", { className: "class-block boxplot-block" }, [el("h3", {}, [title]), ...children]);
  const share = buildShareChart(data.classShare, deps);
  return [
    el("section", {}, [
      el("h2", { textContent: t("stats.heading") }),
      ...buildGearFilters(data, deps.gearHref, deps),
      el("div", { className: "class-grid class-grid-2" }, [block(t("stats.bossTable"), el("div", { className: "table-scroll" }, [table])), block(t("stats.shareHeading"), el("div", { className: "boxplot-card" }, [share ?? el("p", { className: "empty", textContent: t("stats.tooFew") })]))]),
    ]),
  ];
}
