# Assets von aioncodex.com

Gesammelt für die spätere UI (Skill-/Klassennamen und -Icons). Alles hier ist Rohmaterial zur
Weiterverarbeitung, kein fertiges, verifiziertes Datenset – siehe Einschränkungen unten, bevor es
im Meter verwendet wird.

## `skills/skills_en_4x.json` + `skills/icons/`

- Quelle: `https://aioncodex.com/query.php?a=skills&type=<klasse>&slot=<active|passive|stigma>&l=4x`
  für alle 11 Klassen-Typen (`fighter, knight, assassin, ranger, wizard, elementalist, priest,
  chanter, gunner, bard, rider`).
- 974 eindeutige Skills, 930 Icon-Dateien (`icons/*.png`, direkt von
  `https://aioncodex.com/skills/<dateiname>` geladen).
- **Nur Englisch.** Grund: der `/4x/`-Locale-Bucket ist eine eigenständige, versionsgebundene
  Datenbank – sobald man auf der Seite die Sprache wechselt (de/fr/ru/…), landet man auf der
  **aktuellen** (Live-Server-)Datenbank, nicht auf einer 4.x-Version in der jeweiligen Sprache.
  Skill-IDs/-Namen zwischen "4.x-Snapshot" und "aktuelle Version" 1:1 zu verknüpfen wäre riskant
  (Skills werden über die Jahre umbenannt/entfernt/neu vergeben) – deshalb wurde das bewusst NICHT
  gemacht. Für andere Sprachen bräuchte es entweder eine sprachspezifische 4.x-Version der Seite
  (existiert nicht) oder die Namenstabelle aus dem Client selbst (`L10N/<sprache>/...`, siehe
  Haupt-README).
- **Wichtige Einschränkung, vor Nutzung prüfen**: Das Set enthält Klassen wie *Aethertech* und
  *Songweaver* (inkl. der Vorläuferbezeichnung *Muse*), die laut offizieller Patch-Historie erst
  **nach** Patch 4.6 eingeführt wurden. Der `/4x/`-Bucket ist also vermutlich kein exaktes
  4.6-Archiv, sondern ein breiterer "4.x irgendwann"-Snapshot. Vor produktivem Einsatz stichprobenhaft
  ein paar Skillnamen/-level gegen den echten `OriginAion`-4.6-Client vergleichen (z.B. Tooltip
  eines bekannten Skills im Spiel vs. Eintrag hier).
- Format je Skill: `{id, name, icon, class, slot, levels[]}`. `levels` sind die Charakterlevel, ab
  denen der jeweilige Rang lernbar ist (mehrere Ranks/Level pro Skill-ID zusammengefasst).

## `classes/class_names_multilang.json`

- Klassennamen (nicht Icons) in vier Sprachen: US-Englisch, Deutsch, Französisch, Russisch –
  jeweils aus der **aktuellen** Datenbank (`l=us|de|fr|ru`), da Klassennamen als Vokabular über
  Patches hinweg stabil bleiben (deutlich geringeres Risiko als einzelne Skill-IDs).
- **Die Listen sind alphabetisch sortierte Mengen pro Sprache, NICHT index-weise
  gegeneinander ausgerichtet.** Es gibt noch keine maschinell verifizierte Zuordnung
  "Gladiator" (en) ↔ "Gladiator" (de) ↔ "Gladiateur" (fr) ↔ "Гладиатор" (ru). Bei nur 12 Klassen
  ist das von Hand trivial und unstrittig (öffentliches Allgemeinwissen zu AION-Klassen), aber
  bewusst nicht automatisch verknüpft, um keine falsche Zuordnung als "verifiziert" auszugeben.

## Was fehlt (bewusst nicht gemacht)

- **Klassen-Icons**: aioncodex.com hat dafür keinen sauberen Daten-Endpunkt gefunden (die
  `/book/`-Query liefert Skillbook-**Items**, nicht Klassen-Embleme). Robusterer Weg: Icons direkt
  aus den lokalen `OriginAion`-Client-Assets extrahieren (eigener Client, kein Scraping-Thema).
- Weitere Sprachen für Skillnamen (siehe Einschränkung oben – ohne Client-Datenquelle nicht
  risikofrei möglich).
