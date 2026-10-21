import { test } from "node:test";
import assert from "node:assert/strict";
import { localizeZones, applyEnglishCategories, categoryByKeywords, cleanSummary, cleanTitle, latestPosted, ncLangFor, parseList, resetStore, mergeItems, announcementList, statusOf, type Announcement } from "./announcements.js";

test("keywords: the titles seen at NC in every language", () => {
  const cases: [string, string][] = [
    ["[Notice] Temporary Maintenance | Oct 8 PDT / Oct 9 CEST (Completed)", "maintenance"],
    ["[Mitteilung] Wartungsarbeiten | 6 Oktober (PDT) (Abgeschlossen)", "maintenance"],
    ["[Annouce] Maintenance | 6 octobre (Terminé)", "maintenance"],
    ["[Aviso] Mantenimiento temporal | 8 de octubre", "maintenance"],
    ["[Aviso] Manutenção temporária", "maintenance"],
    ["[お知らせ] 2026年10月9日(金) 臨時メンテナンスのご案内", "maintenance"],
    ["October 11 Yustiel Server Outage (Asia)", "outage"],
    ["[Notice] EU Servers Affected by Cloud Service Outage (Resolved)", "outage"],
    ["[お知らせ] 「ユスティエル」サーバーの接続不安定について", "outage"],
    ["October 8 EU Service Outage Compensation", "compensation"],
    ["Entschädigung für den Serviceausfall vom 8. Oktober (EU)", "compensation"],
    ["Compensación por la interrupción del servicio del 8 de octubre (EU)", "compensation"],
    ["New Servers Opening (Europe)", "newServers"],
    ["Neue Server bereitgestellt! (Europa)", "newServers"],
    ["Ouverture de nouveaux serveurs ! (Europe)", "newServers"],
    ["[お知らせ] 新サーバー「ナニア/ハダル」追加のご案内", "newServers"],
    ["[Notice] Server Transfer Q&A", "serverTransfer"],
    ["[Hinweis] Servertransfer-Artikelinformation 13 Oktober", "serverTransfer"],
    ["[Notificaciones] Información sobre productos de transferencia de servidor", "serverTransfer"],
    ["[お知らせ] サーバー移動に関するQ&Aのご案内", "serverTransfer"],
    ["Account Restrictions: Operation Policy Violations and Temporary Security Locks", "account"],
    ["Konto-Einschränkungen: Verstöße gegen die Ablaufsicherheitsrichtlinie", "account"],
    ["[お知らせ] 不正利用に対する利用制限措置のご報告 (2次)", "account"],
    ["Advanced Access: Global Twitch Drops (Last Chance!)", "twitchDrops"],
    ["Twitch Drops Account Linking Guide", "twitchDrops"],
    ["Global Twitch Drops: September 30 - October 16", "twitchDrops"],
    ["Launch Known Issues (Updated: 10/9)", "knownIssues"],
    ["Advanced Access Known Issues (Updated: 10/4)", "knownIssues"],
    ["Bekannte Probleme zum Start", "knownIssues"],
    ["Lancement: Erreurs connues (Mise à jour du 9/10)", "knownIssues"],
    ["Problèmes connus (mis à jour)", "knownIssues"],
    ["[お知らせ] 既知の不具合について", "knownIssues"],
    ["AION 2 Producer’s Letter: Welcome to Atreia!", "other"],
  ];
  for (const [title, expected] of cases) {
    assert.equal(categoryByKeywords(title), expected, title);
  }
});

test("status: only a closing word in the last brackets counts", () => {
  for (const t of ["x (Completed)", "x (Resolved)", "x (Abgeschlossen)", "x (Behoben)", "x (Terminée)", "x (résolu)", "x (Finalizado)", "x (Concluído)", "x (normalizada)", "x（完了）"]) {
    assert.equal(statusOf(t), "done", t);
  }
  for (const t of ["x", "x (Asia)", "Oct 8 PDT / Oct 9 CEST", "[お知らせ] 臨時メンテナンスのご案内(10/8 19:40修正)", "Wartung (Completed) heute"]) {
    assert.equal(statusOf(t), null, t);
  }
});

test("title and summary are cleaned", () => {
  assert.equal(cleanTitle("[Annouce] Maintenance  | 6 octobre"), "Maintenance | 6 octobre");
  assert.equal(cleanTitle("[お知らせ]「AION2」の盛り上がり"), "「AION2」の盛り上がり");
  assert.equal(cleanTitle("New Servers Opening (NA East)"), "New Servers Opening (NA East)");
  assert.equal(cleanSummary("&nbsp;Greetings, Daevas,&nbsp;To keep &amp; &#39;fair&#39;​"), "Greetings, Daevas, To keep & 'fair'");
  assert.equal(cleanSummary("a".repeat(400)).length, 280);
});

const item = (lang: Announcement["lang"], id: string, posted: number, title: string): Announcement => ({
  id, lang, contentId: posted, title, summary: "", posted, category: categoryByKeywords(title), status: statusOf(title),
});

test("a translation takes the category of its English twin, otherwise its own keywords", () => {
  const items = [
    item("en", "a".repeat(24), 1000, "Producer’s Letter"),
    item("de", "b".repeat(24), 1010, "Wartungsarbeiten Brief"),
    item("de", "c".repeat(24), 900_000, "Wartungsarbeiten"),
  ];
  applyEnglishCategories(items);
  assert.equal(items[1].category, "other");
  assert.equal(items[2].category, "maintenance");
  // two English announcements in the same minute with different kinds: no clear twin, the translation keeps its own result
  const tie = [item("en", "d".repeat(24), 5000, "Launch Known Issues"), item("en", "e".repeat(24), 5000, "Launch Into AION 2 Now!"), item("ja", "f".repeat(24), 5000, "正式サービス開始のご案内")];
  applyEnglishCategories(tie);
  assert.equal(tie[2].category, "other");
});

test("parseList skips what does not look right; the stored history keeps old items and the list is per language", () => {
  const json = { contentList: [
    { id: "a".repeat(24), title: "[Notice] Server Transfer Q&A", summary: "x", timestamps: { postedEpoch: 2000 }, snow: { contentId: 5 } },
    { id: "short", title: "bad", timestamps: { postedEpoch: 1 } },
    { id: "b".repeat(24), title: "", timestamps: { postedEpoch: 1 } },
  ] };
  const parsed = parseList("en", json);
  assert.equal(parsed.length, 1);
  assert.equal(parsed[0].category, "serverTransfer");
  resetStore();
  assert.equal(latestPosted(), null);
  mergeItems(parsed);
  mergeItems([item("en", "c".repeat(24), 3000, "New Servers Opening"), item("de", "d".repeat(24), 3000, "Neue Server")]);
  const en = announcementList("ru");
  assert.equal(en.lang, "en");
  assert.deepEqual(en.items.map((x) => x.category), ["newServers", "serverTransfer"]);
  assert.equal(announcementList("de").items.length, 1);
  assert.equal(announcementList("de").items[0].url, `https://aion2.plaync.com/de-de/board/notice/view?articleId=${"d".repeat(24)}`);
  assert.equal(latestPosted(), 3000);
  assert.equal(ncLangFor("pt-BR"), "pt");
  assert.equal(ncLangFor("zh-TW"), "en");
  resetStore();
});

test("German texts get MESZ / MEZ for NC's English zone abbreviations, other languages stay", () => {
  assert.equal(localizeZones("Wartung | 6 Oktober (PDT) / 7 Oktober (CEST) und CET", "de"), "Wartung | 6 Oktober (PDT) / 7 Oktober (MESZ) und MEZ");
  assert.equal(localizeZones("Oct 7 (CEST)", "en"), "Oct 7 (CEST)");
  assert.equal(localizeZones("CESTA", "de"), "CESTA");
});

test("restrictedCount: the number in the restriction details of every language", async () => {
  const { restrictedCount } = await import("./announcements.js");
  assert.equal(restrictedCount("<p>■ Date and time: October 20, 18:00 PDT&nbsp;</p><p>■ Accounts restricted: [15,380]&nbsp;</p>"), 15380);
  assert.equal(restrictedCount("■ Eingeschränkte Konten: [4.996]"), 4996);
  assert.equal(restrictedCount("■ Comptes restreints : [10 326]"), 10326);
  assert.equal(restrictedCount("■ Cuentas restringidas: [4996]"), 4996);
  assert.equal(restrictedCount("Hey, Daevas, we restricted a number of accounts."), null);
});
