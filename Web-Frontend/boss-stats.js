// The statistics view of a boss page (?view=stats): a box plot of the iDPS per class and the top 10 of every class.
// Kept out of app.js on purpose; app.js hands in the helpers it already has (el, t, the row builders ...).
const SVG_NS = "http://www.w3.org/2000/svg";
const MIN_ROWS_FOR_A_BOX = 3;
const FEW_ROWS = 8;

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

/** The box plot: one row per class (strongest median first), the bar spans the middle half of the rows, the line lowest to highest. */
function buildBoxPlot(boxes, deps, yourDps) {
  const { t, formatNumber, classLabel } = deps;
  const rows = boxes.filter((b) => b.samples >= MIN_ROWS_FOR_A_BOX);
  const width = 860;
  const left = 130;
  const right = 24;
  const rowHeight = 44;
  const top = 26;
  const height = top + rows.length * rowHeight + 30;
  const chart = svg("svg", { viewBox: `0 0 ${width} ${height}`, class: "boxplot-svg", role: "img", "aria-label": t("stats.boxHeading") });
  if (rows.length === 0) {
    return null;
  }

  const max = Math.max(...rows.map((b) => b.max), yourDps) * 1.05;
  const x = (value) => left + (value / max) * (width - left - right);
  for (let i = 0; i <= 4; i++) {
    const value = (max / 4) * i;
    chart.appendChild(svg("line", { x1: x(value), x2: x(value), y1: top - 8, y2: height - 24, class: "boxplot-grid" }));
    chart.appendChild(svg("text", { x: x(value), y: height - 8, "text-anchor": "middle", class: "boxplot-axis" }, formatNumber(Math.round(value))));
  }

  rows.forEach((box, i) => {
    const y = top + i * rowHeight + rowHeight / 2;
    const group = svg("g");
    const title = svg("title", {}, `${classLabel(box.className)}: ${t("stats.min")} ${formatNumber(Math.round(box.min))}, Q1 ${formatNumber(Math.round(box.q1))}, ${t("stats.median")} ${formatNumber(Math.round(box.median))}, Q3 ${formatNumber(Math.round(box.q3))}, ${t("stats.max")} ${formatNumber(Math.round(box.max))} (${box.samples})`);
    group.appendChild(title);
    group.appendChild(svg("text", { x: left - 10, y: y + 4, "text-anchor": "end", class: "boxplot-label" }, `${classLabel(box.className)} (${box.samples})`));
    group.appendChild(svg("line", { x1: x(box.min), x2: x(box.max), y1: y, y2: y, class: "boxplot-whisker" }));
    group.appendChild(svg("rect", { x: x(box.q1), y: y - 10, width: Math.max(2, x(box.q3) - x(box.q1)), height: 20, rx: 3, class: "boxplot-box" }));
    group.appendChild(svg("line", { x1: x(box.median), x2: x(box.median), y1: y - 12, y2: y + 12, class: "boxplot-median" }));
    chart.appendChild(group);
  });

  if (yourDps > 0) {
    chart.appendChild(svg("line", { x1: x(yourDps), x2: x(yourDps), y1: top - 8, y2: height - 24, class: "boxplot-you" }));
    chart.appendChild(svg("text", { x: x(yourDps) + 4, y: top - 10, class: "boxplot-you-label" }, `${t("stats.you")} ${formatNumber(yourDps)}`));
  }

  return chart;
}

/**
 * @param stats the answer of /api/bosses/:id/stats: { boxes, topByClass }
 * @param deps  helpers of app.js: el, t, formatNumber, classLabel, classIcon, iconLabel, playerCell, rankedRow, rankedTable, gp
 * @returns the nodes of the view
 */
export function buildBossStats(stats, deps) {
  const { el, t, classLabel, classIcon, iconLabel, playerCell, rankedRow, rankedTable, gp } = deps;
  const classNames = Object.keys(stats.topByClass ?? {}).sort();
  if ((stats.boxes ?? []).length === 0 && classNames.length === 0) {
    return [el("p", { className: "empty", textContent: t("leaderboard.emptyNoFights") })];
  }

  const plotHolder = el("div", { className: "boxplot-card" });
  const note = el("p", { className: "boxplot-note" });
  const input = el("input", { type: "number", min: "0", inputMode: "numeric", placeholder: t("stats.youPlaceholder"), className: "boxplot-you-input", "aria-label": t("stats.yourDps") });
  const draw = () => {
    const chart = buildBoxPlot(stats.boxes ?? [], deps, Number(input.value) || 0);
    plotHolder.replaceChildren(...(chart ? [chart] : [el("p", { className: "empty", textContent: t("stats.tooFew") })]));
    const thin = (stats.boxes ?? []).filter((b) => b.samples >= MIN_ROWS_FOR_A_BOX && b.samples < FEW_ROWS).length;
    note.textContent = t("stats.boxNote") + (thin ? ` ${t("stats.fewRows", { n: thin, rows: FEW_ROWS })}` : "");
  };
  input.addEventListener("input", draw);
  draw();

  const boxSection = el("section", {}, [
    el("h2", { textContent: t("stats.boxHeading") }),
    el("label", { className: "boxplot-you-field" }, [el("span", { textContent: t("stats.yourDps") }), input]),
    plotHolder,
    note,
  ]);

  const topSection = el("section", {}, [
    el("h2", { textContent: t("leaderboard.topByClassHeading") }),
    el(
      "div",
      { className: "class-grid" },
      classNames.map((className) => {
        const rows = stats.topByClass[className].map((p, i) =>
          rankedRow(i + 1, playerCell(p.faction, className, p.playerName, gp(`/encounters/${p.encounterId}`), p.serverName, p), p.idps, p.totalDamage, p.totalHealing, p.topBuffs, false),
        );
        return el("div", { className: "class-block" }, [el("h3", {}, [iconLabel(classIcon(className), classLabel(className))]), rankedTable(rows, false)]);
      }),
    ),
  ]);

  return [boxSection, topSection];
}
