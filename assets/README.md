# Assets von aioncodex.com

Gesammelt für die spätere UI (Skill-/Klassennamen und -Icons). Alles hier ist Rohmaterial zur
Weiterverarbeitung, kein fertiges, verifiziertes Datenset – siehe Einschränkungen unten, bevor es
im Meter verwendet wird.

## `skills/skills_multilang_4x.json` + `skills/icons/`

- Quelle der Klassifikation (Class/Icon/Slot/Levels):
  `https://aioncodex.com/query.php?a=skills&type=<klasse>&slot=<active|passive|stigma>&l=4x` für
  alle 11 Klassen-Typen (`fighter, knight, assassin, ranger, wizard, elementalist, priest, chanter,
  gunner, bard, rider`).
- 974 eindeutige Skills, 930 Icon-Dateien (`icons/*.png`, direkt von
  `https://aioncodex.com/skills/<dateiname>` geladen).
- **Namen jetzt in drei Sprachen** (`name`=Englisch, `de`, `fr` – jeweils optional, fehlt nur für
  Skills ohne Texttreffer): kommen aus dem Aion-Client selbst, nicht von aioncodex. Grund für den
  Umweg: der `/4x/`-Locale-Bucket auf aioncodex.com ist eine eigenständige, versionsgebundene
  Datenbank – sobald man dort die Sprache wechselt (de/fr/ru/…), landet man auf der **aktuellen**
  (Live-Server-)Datenbank, nicht auf einer 4.x-Version in der jeweiligen Sprache. Skill-IDs/-Namen
  zwischen "4.x-Snapshot" und "aktuelle Version" 1:1 zu verknüpfen wäre riskant (Skills werden über
  die Jahre umbenannt/entfernt/neu vergeben) – deshalb weiterhin bewusst nicht gemacht.
- **Stattdessen aus dem Client selbst extrahiert**, wie am 2026-09-09 vom Nutzer vorgeschlagen:
  `strings/client_strings_skill.xml` (UTF-16) aus `data.pak` (ein gewöhnliches ZIP) unter
  `<AION>/L10N/<lang>/Data/data.pak` – vorhanden für `deu`, `eng`, `fra` (weitere Sprachen wie
  `2_xxx` enthalten nur `dialogs.pak`/`font.pak`, keine Skill-Strings). Jeder `<string>`-Eintrag
  trägt `<id>`, einen sprachunabhängigen internen `<name>`-Key (`STR_SKILL_xxx`) und den
  lokalisierten `<body>`-Text; `<id>`/`<name>` sind über alle drei Sprachdateien hinweg identisch
  (stichprobenverifiziert), nur `<body>` unterscheidet sich – **das** ist also die Zuordnung
  zwischen den drei Sprachen, nicht der numerische `id`-Wert des aioncodex-4x-Datensatzes (andere
  Nummernkreise, s. o.).
- **Verknüpfung mit dem aioncodex-4x-Datensatz**: über den englischen Skill-**Namen** als Text
  (nicht über die ID) – für jeden der 974 Einträge den `<body>`-Text mit exakt gleichem `name`
  gesucht (HTML-Entities aus dem aioncodex-Scrape vorher aufgelöst, z. B. `&#39;` -> `'`), dessen
  `<id>` gemerkt, und darüber die deutsche/französische `<body>` derselben `<id>` aus den anderen
  beiden Sprachdateien übernommen. **974 von 974 Einträgen (100 %) fanden einen eindeutigen
  Treffer.** Origin Aions eigener Client-Meter (siehe Entscheidung unten) geht den entgegengesetzten
  Weg – er leitet die Klasse direkt aus dem `<name>`-Key ab (Substring-Suche nach `_WA_`/`_SC_`/…
  gegen eine fest kodierte Kürzel-Tabelle) statt wie hier über einen kuratierten externen Datensatz;
  das wäre robuster gegenüber Client-Updates, wurde hier aber (noch) nicht übernommen, weil die
  textbasierte Verknüpfung bereits 100 % Abdeckung liefert und Class/Icon/Slot/Levels ohnehin aus
  aioncodex stammen müssen.
- **Korrektur**: Aethertech, Songweaver und Gunslinger gehören laut Nutzer tatsächlich zum
  regulären 4.6-Umfang – `OriginAion` hat sie als privater Server gezielt deaktiviert. Das ist also
  keine Verunreinigung des `/4x/`-Buckets durch eine spätere Version, sondern korrekt für 4.6
  allgemein. Der Meter soll ohnehin nicht nur für `OriginAion` gelten, sondern für 4.6-Server
  generell – die vollständige Klassenliste ist dafür richtig. Trotzdem bleibt der `/4x/`-Bucket
  selbst unbestätigt in Details (exakte Skill-Level/-Werte); vor produktivem Einsatz stichprobenhaft
  ein paar Skillnamen gegen einen echten 4.6-Client vergleichen.
- Format je Skill: `{id, name, icon, class, slot, levels[], de?, fr?}`. `levels` sind die
  Charakterlevel, ab denen der jeweilige Rang lernbar ist (mehrere Ranks/Level pro Skill-ID
  zusammengefasst). `id`/`icon`/`class`/`slot`/`levels` bleiben aioncodex-Nummerierung, unabhängig
  vom Client-eigenen `id`-Nummernkreis aus `client_strings_skill.xml`.
- **Neu erzeugen** (wenn aioncodex nachzieht oder weitere Sprachen dazukommen sollen):
  `client_strings_skill.xml` aus dem jeweiligen `L10N/<lang>/Data/data.pak` entpacken
  (`unzip -j data.pak strings/client_strings_skill.xml`), von UTF-16 nach UTF-8 konvertieren
  (`iconv -f UTF-16LE -t UTF-8`), `<string><id>/<body></string>`-Paare einlesen, pro Zeile aus
  `skills_en_4x.json`-artigen Rohdaten den `name` (HTML-entity-entschärft) exakt gegen die
  englische `<body>`-Tabelle matchen (bei fehlendem Treffer ersatzweise den in
  `Data/SkillDatabase.cs` (`RankSuffix`-Regex `\s+[IVXLCDM]+$`) beschriebenen Rang-Suffix
  abschneiden und erneut versuchen), dann `de`/`fr` aus der jeweils gleichen `<id>` übernehmen.

## `classes/class_names_multilang.json`

- Klassennamen (nicht Icons) in vier Sprachen: US-Englisch, Deutsch, Französisch, Russisch –
  jeweils aus der **aktuellen** Datenbank (`l=us|de|fr|ru`), da Klassennamen als Vokabular über
  Patches hinweg stabil bleiben (deutlich geringeres Risiko als einzelne Skill-IDs).
- **Die Listen sind alphabetisch sortierte Mengen pro Sprache, NICHT index-weise
  gegeneinander ausgerichtet.** Es gibt noch keine maschinell verifizierte Zuordnung
  "Gladiator" (en) ↔ "Gladiator" (de) ↔ "Gladiateur" (fr) ↔ "Гладиатор" (ru). Bei nur 12 Klassen
  ist das von Hand trivial und unstrittig (öffentliches Allgemeinwissen zu AION-Klassen), aber
  bewusst nicht automatisch verknüpft, um keine falsche Zuordnung als "verifiziert" auszugeben.

## `i18n/ui_strings.json`

- Die GUI-Übersetzungstabelle: pro Schlüssel ein Objekt `{en, de, fr, es, ru, pl, tr, zh}`,
  geladen von `Ui/Localization.cs` (`LocalizationManager`) und über die WPF-MarkupExtension
  `{local:Loc SchlüsselName}` in `Ui/MainWindow.xaml` und `Ui/SettingsWindow.xaml` gebunden.
  Sprachauswahl unabhängig von der Chat.log-Sprache (siehe `ChatLog/ChatLogParser.cs`) – ein
  deutscher Spieler kann einen englischen Client spielen oder umgekehrt.
- **Bewusst NICHT übersetzt**: Klassen- und Fraktionsnamen (Gladiator, Elyos, …) bleiben in ihrer
  spielkanonischen englischen Form. `classes/class_names_multilang.json` (siehe oben) ist
  ausdrücklich NICHT index-verknüpft – eine automatische Übersetzung dieser Eigennamen wäre
  entweder eine ungeprüfte Vermutung oder bräuchte die gleiche Handverifizierung, die dort bewusst
  aufgeschoben wurde. Ebenso bewusst ausgelassen: die langen erklärenden ToolTip-Texte (nur
  sichtbare Menüs/Buttons/Labels/Spaltenüberschriften sind übersetzt) – Umfang und Fehlerrisiko
  bei ~30 langen Sätzen × 8 Sprachen stand in keinem Verhältnis zum Nutzen einer Hover-Erklärung.
  `Ui/PlayerDatabaseWindow.xaml` und `Ui/PlayerDetailsWindow.xaml` sind ebenfalls noch nicht
  angebunden (bleiben Englisch) – ein Folge-Schritt, keine vergessene Datei.
- **Sprachliste identisch mit der von `ChatLogParser.cs`** (siehe dessen Klassenkommentar) – acht
  Sprachen, nicht mehr: en/de/fr/es (aus echten Chat.log-Sessions bestätigt) plus ru/pl/tr/zh
  (2026-09-09 aus den echten Client-Templates in `L10N/<sprache>/Data/data.pak` ergänzt, siehe
  dort für die "ita"=Polnisch/"plk"=Russisch-Ordnernamen-Verwechslung).
- Format je Schlüssel: `"Bereich.Name": {"en": "...", "de": "...", ...}`. Fehlt ein Schlüssel oder
  eine Sprache darin, fällt `LocalizationManager` zuerst auf Englisch, dann auf den rohen Schlüssel
  selbst zurück (sichtbar falsch statt leer oder abstürzend) – nie eine Ausnahme.

## Aion-Chat-Glyphen für Item-Stufen (keine Datei, Konstanten in `Ui/MainWindow.xaml.cs`)

Die Loot-Chat-Zusammenfassung (`.loot` bzw. der „String"-Knopf) benutzt Aions **eigene**
Chat-Symbole, damit die Zeile im Spiel bunt statt textlastig aussieht:

| Stufe (Origin Codex) | Farbe | Codepoint |
|---|---|---|
| LEGEND | blau | `U+E036` |
| UNIQUE | gold | `U+E038` |
| EPIC | orange | `U+E03E` |
| MYTHIC | lila | `U+E033` |

- Das sind **Private-Use-Area-Zeichen**: Sie werden nur von der Client-Schriftart gerendert. Außerhalb
  von Aion – Editor, Zwischenablage-Viewer, Git-Diff, dieser Chat hier – sind sie **unsichtbar**.
  Genau das führte zur Fehlmeldung „der String-Knopf kopiert nichts": kopiert wurde korrekt, nur
  war das Ergebnis außerhalb des Spiels nicht zu sehen.
- Im Code deshalb als `"\ue036"`-Escapes geschrieben, nicht als literale Zeichen. Ein literales
  PUA-Zeichen überlebt weder Copy-Paste durch beliebige Werkzeuge noch eine Pipeline, die
  Steuerzeichen filtert.
- **So kommt man an sie heran**, falls sie je neu ermittelt werden müssen: im Spiel je einen
  Gegenstand der Stufe in den Chat verlinken, die Zeile in eine Textdatei einfügen, speichern und
  die Bytes auslesen (`xxd`). Über einen Chat/Ticket verschicken funktioniert **nicht** – die
  Zeichen werden unterwegs verworfen.
- Historie als Warnung: `U+E038` (gold) und `U+E03E` (episch) standen ursprünglich falsch im Code
  (`U+E02E` bzw. `U+E02C`) und zeigten damit fremde Glyphen. Aufgefallen ist das erst, als die
  Zeichen einmal Byte für Byte gegen die echte Client-Ausgabe geprüft wurden – im Diff und in jedem
  Editor sahen beide Varianten identisch (nämlich leer) aus.

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

- **Erledigt seit 2026-09-09**: Deutsch/Französisch für Skillnamen (siehe
  `skills/skills_multilang_4x.json` oben) – der damals fehlende Baustein war eine
  Client-Datenquelle, die jetzt über `L10N/<lang>/Data/data.pak` verfügbar ist.
- **Korrektur/erledigt seit 2026-09-09**: Der Satz "kein `L10N/rus/`-Ordner gefunden" oben war
  richtig, aber die Schlussfolgerung falsch – es gibt keinen Ordner NAMENS `rus`, aber es gibt
  echtes Russisch: im Ordner `L10N/plk/` (der Name ist irreführend, der Inhalt ist Russisch, nicht
  Polnisch – siehe den Abschnitt `skills/skills_multilang_4x.json` oben für die volle Geschichte
  der `ita`/`plk`-Ordnernamen-Verwechslung). `skills_multilang_4x.json` enthält bislang trotzdem
  nur de/fr (die beiden zuerst gebauten Sprachen) – Russisch (und Polnisch, aus dem `ita`-Ordner)
  ließen sich mit demselben Verfahren ergänzen, wurden aber noch nicht nachgezogen; nur der
  Chat.log-Parser (`ChatLog/ChatLogParser.cs`) und die GUI (`i18n/ui_strings.json`, siehe unten)
  wurden bereits auf alle acht echten Sprachen erweitert.
- Der von OriginAion selbst genutzte Ansatz, die Klasse direkt aus dem `<name>`-Key abzuleiten
  (Kürzel-Substring-Suche wie `_WA_`/`_SC_`/… gegen eine feste Kürzel→Klassen-Tabelle, siehe
  Abschnitt oben) – robuster gegen Client-Patches als der hier gewählte Namens-Textabgleich, aber
  bislang nicht nötig, da Letzterer bereits 974/974 (100 %) trifft.
