// Layout designer of the features page - only loaded on localhost via /features?design (see renderFeatures in app.js).
// Drag a scene's text or a picture, nudge with the arrow keys (Shift = 10 px), then "Save": the offsets go to feature-layout.json.
// Offsets are px measured at a 1920 px wide window; the dashed lines show the 100 px distance to the edge of each scene.

const KEY = "fx-designer-draft";

export function startDesigner(scenes) {
  const layout = JSON.parse(JSON.stringify(window.fxLayout ?? {}));
  try {
    const draft = JSON.parse(localStorage.getItem(KEY) ?? "null");
    if (draft) Object.assign(layout, draft);
  } catch { /* no draft */ }

  const items = []; // { scene, key, node }
  let selected = null;
  let dirty = false;

  const style = document.createElement("style");
  style.textContent = `
    .fxd-bar{position:fixed;z-index:9999;top:12px;right:12px;display:flex;gap:8px;align-items:center;padding:8px 12px;background:#101418ee;border:1px solid #ffba42;border-radius:10px;color:#fff;font:13px system-ui}
    .fxd-bar button{font:inherit;padding:6px 12px;border-radius:6px;border:1px solid #ffba42;background:#2a2112;color:#ffe2a0;cursor:pointer}
    .fxd-bar button.primary{background:#ffba42;color:#1a1206;font-weight:700}
    .fxd-bar .msg{min-width:170px;color:#9fe0a8}
    .fxd-item{outline:1px dashed #4fb4ff88;outline-offset:2px;cursor:move !important;user-select:none;-webkit-user-drag:none;touch-action:none}
    .fxd-item:hover{outline-color:#4fb4ff}
    .fxd-item.sel{outline:2px solid #ffba42}
    .fxd-item img,.fxd-item *{pointer-events:none}
    .fxd-bgbtn{position:absolute;z-index:6;top:8px;left:50%;transform:translateX(-50%);font:12px system-ui;padding:5px 12px;border-radius:6px;border:1px solid #ffba42;background:#101418ee;color:#ffe2a0;cursor:pointer}
    .fxd-guide{position:absolute;top:0;bottom:0;width:0;border-left:1px dashed #ff4f8b;z-index:4;pointer-events:none}
    .fxd-label{position:absolute;top:4px;z-index:4;font:11px system-ui;color:#ff8fb3;pointer-events:none}
    .fx-jump{position:static !important}
  `;
  document.head.appendChild(style);

  const bar = document.createElement("div");
  bar.className = "fxd-bar";
  const msg = document.createElement("span");
  msg.className = "msg";
  const mk = (label, cls, fn) => { const b = document.createElement("button"); b.textContent = label; if (cls) b.className = cls; b.onclick = fn; bar.appendChild(b); return b; };
  bar.appendChild(msg);
  mk("Reset element", "", () => { if (selected) { setPos(selected, [0, 0]); report(selected); } });
  mk("Reset scene", "", () => { if (!selected) return; for (const it of items.filter((i) => i.scene === selected.scene)) setPos(it, [0, 0]); report(selected); });
  mk("Save", "primary", save);
  document.body.appendChild(bar);
  const say = (text, bad) => { msg.textContent = text; msg.style.color = bad ? "#ff8f8f" : "#9fe0a8"; };
  say(`${window.innerWidth} px wide - design at 1920`);

  function setPos(it, pos) {
    pos = [Math.round(pos[0]), Math.round(pos[1])];
    it.node.style.setProperty("--dx", `${pos[0]}px`);
    it.node.style.setProperty("--dy", `${pos[1]}px`);
    const scene = (layout[it.scene] ??= {});
    if (pos[0] === 0 && pos[1] === 0) delete scene[it.key]; else scene[it.key] = pos;
    dirty = true;
    try { localStorage.setItem(KEY, JSON.stringify(layout)); } catch { /* ignore */ }
  }
  const getPos = (it) => layout[it.scene]?.[it.key] ?? [0, 0];
  const report = (it) => say(`${it.scene}/${it.key}: ${getPos(it).join(", ")}`);

  for (const scene of scenes) {
    const id = scene.id.replace("feature-", "");
    // the 100 px guides
    for (const side of ["left", "right"]) {
      const g = document.createElement("div");
      g.className = "fxd-guide";
      g.style[side] = "100px";
      scene.appendChild(g);
    }
    const l = document.createElement("div"); l.className = "fxd-label"; l.style.left = "104px"; l.textContent = "100 px"; scene.appendChild(l);
    const r = document.createElement("div"); r.className = "fxd-label"; r.style.right = "104px"; r.textContent = "100 px"; scene.appendChild(r);
    const alts = window.fxBgAlternatives?.[id];
    if (alts) {
      const btn = document.createElement("button");
      btn.className = "fxd-bgbtn";
      const names = ["", ...alts];
      const label = () => `Background: ${layout[id]?.bg ?? "default"}  (click = next)`;
      btn.textContent = label();
      btn.onclick = () => {
        const cur = names.indexOf(layout[id]?.bg ?? "");
        const next = names[(cur + 1) % names.length];
        const o = (layout[id] ??= {});
        if (next) o.bg = next; else delete o.bg;
        scene.querySelector(".fx-bg").style.background = `url(/images/features/${next ? `alt/${next}` : `scene-${id}`}.webp?v=${window.fxVersion}) center / cover no-repeat`;
        btn.textContent = label();
        dirty = true;
        try { localStorage.setItem(KEY, JSON.stringify(layout)); } catch { /* ignore */ }
      };
      scene.appendChild(btn);
    }
    const nodes = [["copy", scene.querySelector(".fx-copy")], ...[...scene.querySelectorAll(".fx-win")].map((w, n) => [`win${n}`, w])];
    for (const [key, node] of nodes) {
      if (!node) continue;
      const it = { scene: id, key, node };
      node.classList.add("fxd-item");
      node.style.position = node.style.position || "relative";
      items.push(it);
      const pos = layout[id]?.[key] ?? [0, 0];
      node.style.setProperty("--dx", `${pos[0]}px`);
      node.style.setProperty("--dy", `${pos[1]}px`);
      node.addEventListener("dragstart", (e) => e.preventDefault());
      node.addEventListener("pointerdown", (e) => {
        e.preventDefault();
        select(it);
        const start = [e.clientX, e.clientY];
        const from = getPos(it).slice();
        node.setPointerCapture(e.pointerId);
        const move = (ev) => {
          let dx = from[0] + ev.clientX - start[0], dy = from[1] + ev.clientY - start[1];
          if (ev.altKey) { dx = Math.round(dx / 10) * 10; dy = Math.round(dy / 10) * 10; }
          setPos(it, [dx, dy]);
          report(it);
        };
        const up = () => { node.removeEventListener("pointermove", move); node.removeEventListener("pointerup", up); };
        node.addEventListener("pointermove", move);
        node.addEventListener("pointerup", up);
      });
    }
  }

  function select(it) {
    selected?.node.classList.remove("sel");
    selected = it;
    it.node.classList.add("sel");
    report(it);
  }

  document.addEventListener("keydown", (e) => {
    if (!selected || !e.key.startsWith("Arrow")) return;
    e.preventDefault();
    const step = e.shiftKey ? 10 : 1;
    const [x, y] = getPos(selected);
    const d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[e.key];
    setPos(selected, [x + d[0], y + d[1]]);
    report(selected);
  });

  window.addEventListener("beforeunload", (e) => { if (dirty) e.preventDefault(); });

  async function save() {
    try {
      const res = await fetch("/api/dev/feature-layout", { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(layout) });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      dirty = false;
      localStorage.removeItem(KEY);
      say("Saved to feature-layout.json");
    } catch (err) {
      say(`Not saved: ${err.message}`, true);
    }
  }
}
