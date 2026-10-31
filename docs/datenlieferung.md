# Daten-Lieferung vom Server

Der Client bringt einen vollständigen Grunddatensatz mit (`Client/assets`) und läuft damit allein, auch offline. Daten, die der Server neuer hat, nimmt er in einen Ordner seines Datenordners auf; diese Dateien gelten vor den mitgelieferten. Neue Inhalte (eine Karte, eine Instanz, geänderte Tabellen, Namen, Oberflächentexte) brauchen so kein neues Client-Release. Das nächste Release trägt sie als neuen Grunddatensatz.

## Ablauf

- **Quelle ist das Repository selbst**: der Server liefert `Client/assets/aion2/**` und `Client/assets/i18n/**` aus seinem Checkout (Erweiterungen `json`, `png`, `jpg`, `wav`). Es gibt nur eine Kopie. Nichts, dem der Client vertraut (Schlüssel, Icon, Töne), geht diesen Weg.
- `GET /api/data/manifest`: Liste aller Dateien mit Pfad, SHA-256, Größe und Formatnummer. Die Version der Liste ist der ETag (304, wenn nichts neu ist).
- `GET /api/data/file?path=<Pfad>`: eine Datei.
- Der Client fragt beim Start und danach alle zwei Stunden (nur bei eingeschaltetem „Nach Updates suchen“, auch ohne Upload-Einwilligung), vergleicht jede Datei mit der mitgelieferten (SHA-256, einmal je Version berechnet), lädt, was abweicht, prüft Größe und Hash (`.json` muss als JSON lesbar sein) und legt es unter `%AppData%/Aion DPS Meter/assets` ab. Ein Index (`assets-index.json`) führt, was gilt.
- Alle Zugriffe des Clients auf Daten gehen durch `AssetStore.Path("aion2", ...)`: die aufgenommene Datei, wenn der Index sie führt und die Größe stimmt, sonst die mitgelieferte. Eine neu aufgenommene Datei gilt ab dem nächsten Start (die Tabellen werden einmal gelesen).
- Eine Datei, die der Server nicht mehr führt oder die inzwischen dem mitgelieferten Stand entspricht (neues Release), wird aus dem Ordner entfernt.

## Eine Änderung ausliefern

1. Die Dateien unter `Client/assets/aion2/...` (oder `i18n`) ändern bzw. hinzufügen, committen, pushen.
2. `ssh alfahosting dpsmeter_install` – der Server kennt die neue Liste nach spätestens 30 s, die Clients holen sie innerhalb von zwei Stunden.
3. Der Webseiten-Changelog bekommt wie immer seinen Eintrag.

**Neue Karte**: Eintrag in `aion2/maps/maps.json` (`title`, optional `names` je Sprache, `table`, `tile`, `grid`, `scale`, `offsetX/Y`, optional `openZones` mit den Zonen-IDs der Karte), Kacheln unter `aion2/maps/<key>/`, Spawns in `aion2/pets/spawns.json`, Regionen in `aion2/pets/regions.json`. Die Karte erscheint in der Haustier-Einstellung als eigene Gruppe, der Name kommt aus `names`/`title`.

**Neue Instanz**: Namen und Bosse in den Tabellen (`aion2/npcs/boss_npcs.json`, `npc_names.json`, `skill_names*.json` ...); Instanz-Karten in `aion2/maps/instances.json`.

## Wenn sich das Aufbauformat einer Datei ändert

Liest der Client-Code eine Datei in einem neuen Aufbau, bekommt sie im Backend (`DATA_FORMATS` in `Backend/src/dataPack.ts`) eine höhere Formatnummer, und `AssetStore.SupportedFormat` im Client nennt sie erst in dem Release, das den neuen Aufbau liest. Ältere Clients lassen die Datei dann liegen und bleiben bei ihrer mitgelieferten.

## Was Code bleibt

Parser, Frame-Aufbau, Berechnungen und alles, was neues Verhalten braucht, bleiben im Client und brauchen ein Release. Daten ja, Verhalten nein.

## Prüfungen

- Selbsttest `selftest` (Abschnitt „Data from the server“): Testserver mit gleicher, geänderter, neuer, beschädigter und zukünftiger Datei.
- Backend-Tests: `dataPack.test.ts` (Liste, erlaubte Pfade), `privacyTexts.test.ts` (Datenschutztext in Client und Webseite gleich), `assetCopies.test.ts` (Tabellen in Client und Backend gleich).
- `Aion DPS.exe data-refresh` führt einen Lauf gegen den Server aus (`AIONDPS_DATA_DIR`, `AIONDPS_SHIPPED_DIR` für Tests).
