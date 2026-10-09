---
name: deploy
description: Die große Variante für aiondps - Typecheck, Commit, Changelog (EN+DE), Push, Server-Deploy, Client-Release falls Client-Dateien geändert sind, Features-Seite prüfen, Verifikation. Nur auf ausdrücklichen Aufruf /deploy.
disable-model-invocation: true
---

`/deploy` ist das ausdrückliche Go des Users für **alles**: Commit, Push, Changelog, Server-Deploy, Client-Release (wenn nötig) und Features-Aktualisierung. Führe die Schritte der Reihe nach aus und lass keinen aus. Antworte auf Deutsch.

1. **Bestand prüfen:** `git status`, `git diff --stat`. Ordne jede Änderung einem Bereich zu: Client (`Client/`, ohne reine Doku/Tools), Website (`Web-Frontend/`), Backend (`Backend/`), Datenbank (`Backend/drizzle`, `Backend/src/db`). Gibt es nichts zu committen und ist `origin/main` gleich HEAD, melde das und stoppe. Fremde, nicht zur Aufgabe gehörende Änderungen nicht mitnehmen, sondern nachfragen.
2. **Prüfen:** `cd Backend && npx tsc --noEmit` und `pnpm test`, falls es das Skript gibt. Ist der Client betroffen, baue ihn mit `/mnt/c/Program Files/dotnet/dotnet.exe build` (siehe Memory dotnet via WSL-Interop). Bei Fehlern stoppen und melden, nichts pushen.
3. **Features-Seite:** Hat sich ein sichtbares Feature geändert (neu, entfernt, umbenannt), aktualisiere `featuresPage()` in `Backend/src/seo/pages.ts`, die Features-Ansicht in `Web-Frontend/app.js` und alle Locale-Dateien (`Web-Frontend/locales/*.js`). Sonst ausdrücklich im Bericht "Features: keine Änderung nötig" schreiben.
4. **Commit:** aussagekräftiger **englischer** Betreff (er wird zum Changelog-Text); private Stats-Seiten (/p/<secret>, /metrics) nie erwähnen. Letzte Zeile: `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
5. **Client-Release, nur wenn Client-Dateien betroffen sind:** Version in `Client/AionDPS.csproj` nach der Regel erhöhen (Patch = Bugfix/klein, Minor = neue sichtbare Funktion mit Patch auf 0, Major nie ohne User), eigener Commit `Release X.Y.Z: <englischer Text>` (der Text muss im Betreff stehen, sonst fehlt der Changelog-Eintrag). Nach dem Push `gh release create vX.Y.Z --prerelease --target main --title ... --notes ...`, auf den Workflow-Lauf warten und die Assets prüfen. Nie lokale Setup-Dateien bauen oder auf den Desktop kopieren. Release-Aufbewahrung: nur die 10 neuesten behalten, ältere samt Tags löschen, nie das aktuelle.
6. **Changelog (immer, bei jedem Push):** nach dem Release-Commit `node Tools/changelog/gen.mjs`, neue Einträge in `Web-Frontend/changelog.de.json` übersetzen (Stil der vorhandenen Einträge, formelles Deutsch), beide JSON-Dateien als eigenen Commit `Changelog: <kurz>` hinzufügen. Commits, deren Betreff Nicht-Öffentliches nennt, in `Tools/changelog/overrides.json` mit Ersatztext oder `null` eintragen.
7. **Push:** `git push origin main` (bei Release auch den Tag).
8. **Server-Deploy:** `ssh alfahosting "dpsmeter_install"`. Bei Schema-/Merge-Änderungen vorher ein DB-Backup per `sqlite3 ... ".backup '...'"` nach `/root/dpsmeter-backups/` (nie `cp`, WAL-Modus).
9. **Prüfen:** `https://aiondps.com/` liefert 200; `/sitemap.xml` ist gültiges XML mit `<loc>`; `/changelog` zeigt den neuen Eintrag; `systemctl is-active aion-dpsmeter-backend` ist active; bei Client-Release hat das GitHub-Release den Installer als Asset.
10. **Bericht:** Commit-Hashes, Version, was deployt wurde, Ergebnis der Prüfungen. Wenn etwas fehlschlug oder übersprungen wurde, sag es offen.

**Grenzen:** Keine Discord-Posts (nur auf ausdrückliche Zusage). Blockiert der Berechtigungsfilter einen Schritt (Push, SSH, Release), nicht umgehen: dem User melden, "ja" abwarten, dann neu versuchen.
