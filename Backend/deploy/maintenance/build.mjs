// Builds the static maintenance pages (one per site language) that nginx serves while the backend is down.
// Usage: node build.mjs [outDir]   (default: ./dist)
import { mkdirSync, writeFileSync, copyFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const out = process.argv[2] ?? join(here, "dist");

const T = {
  de: { title: "Wartungsarbeiten", head: "Wir sind gleich wieder da", text: "AionDPS wird gerade aktualisiert. Das dauert in der Regel nur wenige Minuten.", hint: "Diese Seite lädt sich automatisch neu." },
  en: { title: "Maintenance", head: "We'll be right back", text: "AionDPS is being updated right now. This usually only takes a few minutes.", hint: "This page reloads automatically." },
  es: { title: "Mantenimiento", head: "Enseguida volvemos", text: "AionDPS se está actualizando en este momento. Normalmente solo lleva unos minutos.", hint: "Esta página se recarga automáticamente." },
  fr: { title: "Maintenance", head: "Nous revenons tout de suite", text: "AionDPS est en cours de mise à jour. Cela ne prend généralement que quelques minutes.", hint: "Cette page se recharge automatiquement." },
  ja: { title: "メンテナンス中", head: "まもなく復旧します", text: "現在 AionDPS を更新しています。通常は数分で完了します。", hint: "このページは自動的に再読み込みされます。" },
  ko: { title: "점검 중", head: "곧 다시 돌아옵니다", text: "현재 AionDPS를 업데이트하고 있습니다. 보통 몇 분이면 끝납니다.", hint: "이 페이지는 자동으로 새로 고쳐집니다." },
  pt: { title: "Manutenção", head: "Já voltamos", text: "O AionDPS está a ser atualizado neste momento. Normalmente demora apenas alguns minutos.", hint: "Esta página é recarregada automaticamente." },
  ru: { title: "Технические работы", head: "Скоро вернёмся", text: "Сейчас идёт обновление AionDPS. Обычно это занимает всего несколько минут.", hint: "Страница обновится автоматически." },
  "zh-TW": { title: "維護中", head: "我們很快回來", text: "AionDPS 正在更新，通常只需要幾分鐘。", hint: "此頁面會自動重新載入。" },
  zh: { title: "维护中", head: "我们马上回来", text: "AionDPS 正在更新，通常只需要几分钟。", hint: "此页面会自动刷新。" },
};

const css = `:root{color-scheme:dark;--bg:#0e1820;--surface:#152532;--border:#294557;--text:#f2f5f7;--muted:#7e8f9a;--primary:#ffba42}
*{box-sizing:border-box}
body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;padding:24px;background:var(--bg);color:var(--text);font:16px/1.55 system-ui,"Segoe UI",Roboto,"Noto Sans","Noto Sans JP","Noto Sans KR","Noto Sans SC","Noto Sans TC",sans-serif}
.card{width:min(100%,720px);padding:56px 48px;text-align:center;background:var(--surface);border:1px solid var(--border);border-radius:10px;box-shadow:0 12px 40px rgba(0,0,0,.28)}
.logo{width:128px;height:128px;border-radius:18px;margin:0 auto 28px;display:block}
h1{margin:0 0 16px;font-size:38px;line-height:1.25}
p{margin:0 0 8px;font-size:20px}
.bar{height:4px;margin:36px auto 18px;width:260px;border-radius:2px;background:var(--border);overflow:hidden}
.bar::after{content:"";display:block;height:100%;width:40%;background:var(--primary);border-radius:2px;animation:slide 1.6s ease-in-out infinite}
@keyframes slide{0%{transform:translateX(-100%)}100%{transform:translateX(250%)}}
.hint{color:var(--muted);font-size:15px}
@media (prefers-reduced-motion:reduce){.bar::after{animation:none;width:100%;opacity:.5}}
`;

mkdirSync(out, { recursive: true });
writeFileSync(join(out, "maintenance.css"), css);
copyFileSync(join(here, "../../../Web-Frontend/logo.png"), join(out, "maintenance-logo.png"));
for (const [code, t] of Object.entries(T)) {
  writeFileSync(join(out, `maintenance.${code}.html`), `<!doctype html>
<html lang="${code}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex">
<meta http-equiv="refresh" content="20">
<title>AionDPS – ${t.title}</title>
<link rel="stylesheet" href="/_maintenance/maintenance.css">
</head>
<body>
<main class="card">
<img class="logo" src="/_maintenance/maintenance-logo.png" alt="AionDPS" width="128" height="128">
<h1>${t.head}</h1>
<p>${t.text}</p>
<div class="bar" role="presentation"></div>
<p class="hint">${t.hint}</p>
</main>
</body>
</html>
`);
}
