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

## `classes/icons/` + `races/icons/`

- **Klassen-Icons** (13 Stück, 64×64 RGBA): `https://myaion.eu/Images/Classes/<Klasse>.png`.
- **Fraktions-Wappen** (Elyos/Asmodian, 32×32 RGBA): `https://aioncodex.com/images/elyos.png` bzw.
  `.../asmo.png`. **Nicht** von myaion.eu, obwohl es dort auch welche gibt: die sind nur 16×16 und
  wurden auf 18px hochskaliert sichtbar matschig – vom Nutzer gemeldet („auf dem dunklen
  Hintergrund schwer zu erkennen"). Beide Wappen sind hell (Elyos rgb(144,217,243),
  Asmodian rgb(249,189,79)) und haben Alpha, stehen also auf dem dunklen UI-Hintergrund.
  Achtung bei aioncodex: die meisten naheliegenden Dateinamen (`icon_race_light.png`,
  `asmodian.png`, `race_dark.png` …) liefern **200 mit einem Platzhalterbild**, nicht 404 – nur
  `elyos.png` und `asmo.png` sind echt. Beim Nachladen also den Bildinhalt prüfen, nicht den
  HTTP-Status.
- **Alle Icons sind echte PNGs.** Das war nicht immer so: die ursprünglich eingecheckten Dateien
  waren **WebP mit `.png`-Endung**. WPF kann WebP nicht von sich aus dekodieren – es funktionierte
  nur, weil Windows 11 einen WebP-Codec in WIC mitbringt. Auf einem System ohne diesen Codec wären
  sämtliche Icons stillschweigend unsichtbar geblieben. Beim Nachladen darauf achten, dass wirklich
  PNG ankommt (`file <datei>`).
- Bewusst nur Icons, keine weiteren Bilddaten – die Loot-Liste braucht Namen, kein Bild.

## `items/items_origincdx_4x.json`

- **Quelle der Wahrheit: Origin Codex** (`https://origincdx.com/item_basic_lookup.json`, die
  JSON-Tabelle, aus der die Seite selbst ihre Item-Suche speist). Grund: Origin Codex beschreibt
  **genau den gespielten Server**, nicht Retail-4.x – es kennt OriginAion-eigene Items, die in
  keinem Retail-Abzug stehen. Jeder Eintrag liefert `item_id`, `display_name` und `quality`
  als Klartext (`JUNK`/`COMMON`/`RARE`/`LEGEND`/`UNIQUE`/`EPIC`/`MYTHIC`).
- **Ergänzt aus dem alten aioncodex-Abzug** (`query.php?a=items&l=4x`), aber nur dort, wo Origin
  Codex eine ID gar nicht kennt: 6.149 von 91.739 Einträgen. Das ist kein Kompromiss bei der
  Wahrheit, sondern reine Lückenfüllung – auf den 85.343 IDs, die **beide** Quellen beschreiben,
  stimmen die Qualitätsstufen **ausnahmslos** überein (0 Abweichungen). Ohne diese Ergänzung
  würden 6.149 bekannte Items im Loot-Grid wieder zu „Item #ID" werden.
- Format je Eintrag: `{id, name, quality}`. Bewusst OHNE Icons – bei über 90.000 Items wäre das
  Herunterladen aller Icon-Dateien unverhältnismäßig, und die Loot-Liste braucht nur den Namen.
- Grund für diese Datenbank: Chat.log-Loot-Zeilen ("You have acquired [item:ID;...].", gefunden von
  terminal_windows) enthalten NUR die numerische Item-ID im `[item:...]`-Tag, nirgends einen
  lesbaren Namen im sichtbaren Text.
- **Damit gelöst**: die beiden IDs `186000936` und `186000938`, die der alte Abzug nicht kannte und
  die hier früher als „ungeklärt fehlend" dokumentiert waren. Es sind **Cosmic Fragment** und
  **Eternity Comet**, beides OriginAion-Währungen – gefallen in einem echten Sauro-Run.
  `ItemDatabase.DisplayName` fällt für jeden weiterhin unbekannten Fall auf `"Item #<ID>"` zurück.
- **Stufen-Namen kommen von Origin Codex, Farben weiter von aioncodex.** Das `ItemGrade`-Enum hieß
  vorher Common/Rare/**Hero**/Unique/**Legendary**/**Ultimate** – das waren Erfindungen dieses
  Projekts. Der Server nennt dieselben Stufen LEGEND/UNIQUE/EPIC/MYTHIC, und das Loot-Grid zeigte
  dadurch das falsche Wort (ein EPIC-Item stand als „Legendary" da). Die Hex-Farben stammen
  unverändert aus `https://aioncodex.com/css/aioncodex.min.css`, wo sie nach Stufen-Nummer und
  nicht nach Namen sortiert sind: `item_grade_0/1`=`#fff` (Junk+Common/Weiß), `_2`=`#69e15e`
  (Rare/Grün), `_3`=`#4ccfff` (Legend/Blau), `_4`=`#f0b71c` (Unique/Gold), `_5`=`#f08033`
  (Epic/Orange), `_6`=`#8f39ce` (Mythic/Lila). Grade 7–9 existieren in der CSS, kommen aber in
  keinem Item vor.
- **Neu erzeugen** (wenn Origin Codex nachzieht): `item_basic_lookup.json` laden, pro Eintrag
  `{item_id, display_name, quality}` übernehmen, die 6.149 nur-aioncodex-IDs aus dem alten Abzug
  anhängen, nach ID sortiert als JSON-Array schreiben. Origin Codex enthält 6.691 Schlüssel, die
  auf eine bereits vorhandene `item_id` zeigen (Groß-/Kleinschreibungs-Varianten desselben
  internen Keys); die sind inhaltlich identisch und werden zusammengefasst – geprüft, 0 Konflikte.

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
