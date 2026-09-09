const app = document.getElementById("app");
const breadcrumb = document.getElementById("breadcrumb");

async function fetchJson(url) {
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`${res.status} ${res.statusText}`);
  }
  return res.json();
}

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  Object.assign(node, props);
  for (const child of children) {
    node.append(child);
  }
  return node;
}

function link(text, hash) {
  return el("a", { href: hash, textContent: text });
}

function setBreadcrumb(parts) {
  breadcrumb.replaceChildren();
  parts.forEach((part, i) => {
    if (i > 0) {
      breadcrumb.append(" › ");
    }
    breadcrumb.append(typeof part === "string" ? part : part);
  });
}

function formatNumber(n) {
  return Math.round(n).toLocaleString("de-DE");
}

async function renderInstances() {
  setBreadcrumb(["Instanzen"]);
  app.replaceChildren(el("p", { textContent: "Lade Instanzen…" }));

  const instances = await fetchJson("/api/instances");
  if (instances.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: "Noch keine Instanzen erfasst - lade den ersten Boss-Kampf über den DPS-Meter hoch." }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    instances.map((i) => el("li", {}, [link(i.name, `#/instances/${i.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: "Instanz wählen" }), list);
}

async function renderBosses(instanceId) {
  setBreadcrumb([link("Instanzen", "#/"), "Bosse"]);
  app.replaceChildren(el("p", { textContent: "Lade Bosse…" }));

  const bosses = await fetchJson(`/api/instances/${instanceId}/bosses`);
  if (bosses.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: "Für diese Instanz wurden noch keine Bosse hochgeladen." }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    bosses.map((b) => el("li", {}, [link(b.name, `#/bosses/${b.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: "Boss wählen" }), list);
}

function rosterTable(roster) {
  const rows = roster.map((p) =>
    el("tr", {}, [
      el("td", { textContent: p.playerName }),
      el("td", { textContent: p.className }),
      el("td", { textContent: formatNumber(p.totalDamage) }),
      el("td", { textContent: formatNumber(p.idps) }),
    ]),
  );
  return el("table", {}, [
    el("thead", {}, [
      el("tr", {}, [
        el("th", { textContent: "Spieler" }),
        el("th", { textContent: "Klasse" }),
        el("th", { textContent: "Schaden" }),
        el("th", { textContent: "iDPS" }),
      ]),
    ]),
    el("tbody", {}, rows),
  ]);
}

async function renderLeaderboard(bossId) {
  setBreadcrumb([link("Instanzen", "#/"), "Leaderboard"]);
  app.replaceChildren(el("p", { textContent: "Lade Leaderboard…" }));

  const data = await fetchJson(`/api/bosses/${bossId}/leaderboard`);
  setBreadcrumb([link("Instanzen", "#/"), data.boss.name]);

  if (data.topGroups.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: "Für diesen Boss wurde noch kein Kampf hochgeladen." }));
    return;
  }

  const groupsSection = el("section", {}, [
    el("h2", { textContent: `Top ${data.topGroups.length} Gruppen (iDPS)` }),
    ...data.topGroups.map((g, i) =>
      el("details", { open: i === 0 }, [
        el("summary", { textContent: `#${i + 1} - ${formatNumber(g.groupIDps)} iDPS (${g.roster.length} Spieler, ${g.mergedUploadCount} Uploads)` }),
        rosterTable(g.roster),
      ]),
    ),
  ]);

  const classNames = Object.keys(data.topByClass).sort();
  const classSection = el("section", {}, [
    el("h2", { textContent: "Top 10 je Klasse" }),
    el(
      "div",
      { className: "class-grid" },
      classNames.map((className) => {
        const rows = data.topByClass[className].map((p, i) =>
          el("tr", {}, [
            el("td", { textContent: `${i + 1}.` }),
            el("td", {}, [link(p.playerName, `#/search/${encodeURIComponent(p.playerName)}`)]),
            el("td", { textContent: formatNumber(p.idps) }),
          ]),
        );
        return el("div", { className: "class-block" }, [
          el("h3", { textContent: className }),
          el("table", {}, [el("tbody", {}, rows)]),
        ]);
      }),
    ),
  ]);

  app.replaceChildren(groupsSection, classSection);
}

async function renderPlayerProfile(playerId) {
  setBreadcrumb([link("Instanzen", "#/"), "Spielerprofil"]);
  app.replaceChildren(el("p", { textContent: "Lade Spielerprofil…" }));

  const data = await fetchJson(`/api/players/${playerId}`);
  setBreadcrumb([link("Instanzen", "#/"), data.player.name]);

  if (data.history.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: "Noch keine erfassten Kämpfe für diesen Spieler." }));
    return;
  }

  const rows = data.history.map((h) =>
    el("tr", {}, [
      el("td", { textContent: new Date(h.startedAt).toLocaleString("de-DE") }),
      el("td", {}, [link(h.bossName, `#/bosses/${h.bossId}`)]),
      el("td", { textContent: h.className }),
      el("td", { textContent: formatNumber(h.totalDamage) }),
      el("td", { textContent: formatNumber(h.idps) }),
      el("td", { textContent: `${h.critRatePercent.toFixed(1)}%` }),
    ]),
  );

  app.replaceChildren(
    el("h2", { textContent: data.player.name }),
    el("table", {}, [
      el("thead", {}, [
        el("tr", {}, [
          el("th", { textContent: "Datum" }),
          el("th", { textContent: "Boss" }),
          el("th", { textContent: "Klasse" }),
          el("th", { textContent: "Schaden" }),
          el("th", { textContent: "iDPS" }),
          el("th", { textContent: "Crit%" }),
        ]),
      ]),
      el("tbody", {}, rows),
    ]),
  );
}

async function renderSearchResults(query) {
  setBreadcrumb([link("Instanzen", "#/"), `Suche: ${query}`]);
  app.replaceChildren(el("p", { textContent: "Suche…" }));

  const results = await fetchJson(`/api/players/search?q=${encodeURIComponent(query)}`);
  if (results.length === 1) {
    location.hash = `#/players/${results[0].id}`;
    return;
  }
  if (results.length === 0) {
    app.replaceChildren(el("p", { className: "empty", textContent: `Kein Spieler namens "${query}" gefunden.` }));
    return;
  }

  const list = el(
    "ul",
    { className: "plain" },
    results.map((p) => el("li", {}, [link(p.name, `#/players/${p.id}`)])),
  );
  app.replaceChildren(el("h2", { textContent: "Mehrere Treffer" }), list);
}

async function route() {
  const hash = location.hash.replace(/^#\/?/, "");
  const [section, param] = hash.split("/");

  try {
    if (!section) {
      await renderInstances();
    } else if (section === "instances" && param) {
      await renderBosses(param);
    } else if (section === "bosses" && param) {
      await renderLeaderboard(param);
    } else if (section === "players" && param) {
      await renderPlayerProfile(param);
    } else if (section === "search" && param) {
      await renderSearchResults(decodeURIComponent(param));
    } else {
      await renderInstances();
    }
  } catch (err) {
    app.replaceChildren(el("p", { className: "error", textContent: `Fehler: ${err.message}` }));
  }
}

document.getElementById("search-form").addEventListener("submit", (e) => {
  e.preventDefault();
  const query = document.getElementById("search-input").value.trim();
  if (query) {
    location.hash = `#/search/${encodeURIComponent(query)}`;
  }
});

window.addEventListener("hashchange", route);
route();
