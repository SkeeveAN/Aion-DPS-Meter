# Aion-2-Skilldaten

Rohmaterial für `Aion2SkillNames.cs` und spätere Aion-2-UI (Skillnamen/-icons), analog zum
Vorgehen in `assets/README.md` für Aion 1: Fakten (IDs, Namen, wie der Client sie anzeigt)
übernommen, Interpretation/Berechnung bewusst **nicht**.

## Quelle

Alle Dateien hier stammen aus dem installierten `aion2t.com DPS`-Meter (Tauri/Rust-App,
`C:\Program Files\aion2t.com DPS\_up_\src\data\`, Stand 2026-09-28), das seinerseits laut eigenen
Datei-Headern per Skript (`scripts/rebuild_*_from_client.py`) direkt aus dem Aion-2-Client zieht
(`aion2.db`, u. a. Tabellen `Skill`/`SkillAcquireData`). Sobald ein eigener Extraktor aus dem
Client existiert (vgl. `opcodes.json`-Kalibrierung), sollte der diese Dateien ersetzen.

- `skill_names.json` — Skill-ID → englischer Name (`i18n/skills/en.json`, 16.899 Einträge).
  **Aktiv genutzt** von `Aion2SkillNames.cs`.
- `skill_names_de.json` — dieselbe Zuordnung auf Deutsch (`i18n/skills/de.json`). Noch nicht
  verdrahtet (`Aion2SkillNames` kennt bisher nur eine Sprache); Rohmaterial für eine spätere
  `NameDe`-Erweiterung analog zu `Data/SkillDatabase.cs`.
- `skill_icon_tokens.json` — Skill-ID → interner Icon-Token des Clients (z. B.
  `ICON_EL_SKILL_010`). **Keine Bilddateien** — nur der Name, den der Client intern für die
  jeweilige Icon-Ressource verwendet. Für eine echte Icon-Anzeige (wie
  `Ui/SkillIconConverter.cs` es für Aion 1 tut) fehlen noch die eigentlichen Bilddateien; die
  müssten separat aus den Client-Ressourcen (`.pak`) extrahiert werden, nicht aus dieser Quelle.
- `dot_skill_ids.json` — Liste der Skill-IDs, die einen Damage-over-Time-Effekt auslösen.
- `heal_skill_families.json` — Skill-IDs je Heal-Familie.
- `buff_names_en.json` — Buff/Debuff-ID (hex) → englischer Name.
- `skill_abnormal_types.json` — Skill-/Buff-ID → Abnormal-Type (Buff/DeBuff/Passiv). Laut
  Original-Quelle **"client-authoritative"** (`aion2.db SkillAbnormal.AbnormalType`, nur Lücken
  über INGMeter gefüllt) — anders als `rdps_buffs.json` also primär ein Client-Attribut, keine
  Balance-Interpretation, deshalb hier als Fakt übernommen. 212 Skills mit widersprüchlichen
  Quellen wurden von der Originalquelle bewusst nicht geraten und fehlen entsprechend auch hier.
  Noch kein Consumer im Code (kein Abnormal-Type-Feature in der Aion2-Pipeline).
- `rdps_watchlist.json` — **Keine Werte, nur eine Checkliste.** Enthält aus `rdps_buffs.json` nur
  die strukturellen Fakten (welche 8 Skills party-weite Schadensverstärkung geben, `kind`,
  `sourceRestriction`, `exclusiveGroup`), NICHT die `ampByLevel`-Prozentwerte — die sind
  gestrichen (`null`) und `calibrated: false` gesetzt, exakt wie bei `opcodes.json`. Dient als
  Prioritätenliste: sobald Aion 2 startet, zuerst bei diesen 8 Skills mit/ohne Buff vergleichen
  und die echten %-Werte selbst eintragen.

## Bewusst NICHT übernommen

`rdps_buffs.json`s `ampByLevel`-Werte (rDPS-Verstärkungs-%, Stacking-Patch-Historie) wurden **nicht**
kopiert — die Datei ist im Original explizit als **"Mirror of INGMeter"** gekennzeichnet (einem
koreanischen Drittanbieter-Meter, aion.ing) inkl. Balance-Patch-Historie, die wir nicht selbst
nachvollziehen können. Diese Zahlen sollen aus eigenen Combat-Logs (AionSniffer-Aufzeichnungen,
Prioritätenliste siehe `rdps_watchlist.json`) empirisch hergeleitet und verifiziert werden — analog
zum Boss-HP-Abgleich, der für Aion 1 bereits als Ground-Truth-Methode genutzt wird.
