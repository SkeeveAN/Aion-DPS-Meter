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
- **Korrektur**: Aethertech, Songweaver und Gunslinger gehören laut Nutzer tatsächlich zum
  regulären 4.6-Umfang – `OriginAion` hat sie als privater Server gezielt deaktiviert. Das ist also
  keine Verunreinigung des `/4x/`-Buckets durch eine spätere Version, sondern korrekt für 4.6
  allgemein. Der Meter soll ohnehin nicht nur für `OriginAion` gelten, sondern für 4.6-Server
  generell – die vollständige Klassenliste ist dafür richtig. Trotzdem bleibt der `/4x/`-Bucket
  selbst unbestätigt in Details (exakte Skill-Level/-Werte); vor produktivem Einsatz stichprobenhaft
  ein paar Skillnamen gegen einen echten 4.6-Client vergleichen.
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

## `classes/icons/` + `races/icons/` (Quelle: myaion.eu)

- Gefunden über die öffentlichen Bosskampf-Session-Reports von myaion.eu (z.B.
  `/PvESession/1716393`, `/PvEPlayerSession/8602573`) – dort werden pro Spieler Rassen-/Klassen-Icon
  direkt referenziert (`/Images/Races/<Rasse>.png`, `/Images/Classes/<Klasse>.png`).
- Rassen: `Elyos.png`, `Asmodian.png` – eindeutig.
- Klassen, bestätigt vorhanden (HTTP 200 direkt abgefragt): `Gladiator, Templar, Assassin,
  Ranger, Sorcerer, Spiritmaster, Cleric, Chanter, Aethertech, Bard, Priest, Gunner, Painter`.
  **Ungeklärt**: `Bard`, `Priest`, `Gunner`, `Painter` passen nicht sauber auf die 12 bekannten
  finalen Klassennamen (kein `Songweaver.png`, `Gunslinger.png`, `Cleric`+`Priest` beide vorhanden
  o.ä. unter den naheliegenden Namen gefunden – ergaben HTTP 400). Vermutung: unterschiedliche
  interne Namenskonvention von myaion.eu (evtl. alte/Beta-Klassennamen oder Basisklassen-Embleme
  vor der finalen Entscheidung), aber nicht verifiziert – die Icons selbst sind gespeichert, ihre
  Zuordnung zu den echten Klassennamen muss noch von jemandem mit Spielkenntnis bestätigt werden.
- Skill-Icons wurden von myaion.eu **nicht** gesammelt (kein browsbarer Datenbank-Bereich
  gefunden, nur einzelne Icons in Session-Tooltips) – aioncodex.com bleibt dafür die Quelle.
- **Keine Mehrsprachigkeit**: myaion.eu hat keinen Sprachumschalter, alle Texte sind Englisch.

## Idee für später: Online-Session-Sharing (aus myaion.eu übernommen)

Der Nutzer fand das Feature von myaion.eu gut, Bosskämpfe als Web-Link teilbar zu machen:
Gruppenansicht (`/PvESession/<id>`: Rangliste pro Spieler mit bossDPS/bossDMG/allDPS/allDMG/Heal)
und Einzelansicht (`/PvEPlayerSession/<id>`: Skill-für-Skill-Aufschlüsselung mit
Uses/Crit/Resist/Dodge/Parry/Block/Min/Max/Avg/Damage, plus eine Zeitleiste mit
Time/Status/Damage/Heal/Target). Als **spätere, große Ausbaustufe** notiert – braucht ein eigenes
Backend (API + Datenbank) und Hosting, ist also kein Teil der aktuellen Windows-Client-Architektur
und kommt erst, wenn der lokale Meter selbst funktioniert.

## Was fehlt (bewusst nicht gemacht)

- Weitere Sprachen für Skillnamen (siehe Einschränkung oben – ohne Client-Datenquelle nicht
  risikofrei möglich; myaion.eu bietet ebenfalls keine anderen Sprachen).
