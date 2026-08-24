# AionSniffer – Netzwerkbasierter DMG-Meter für AION 4.6 (OriginAion)

## Stand der Analyse

Drei bestehende Tools wurden analysiert:

| Tool | Methode |
|---|---|
| UltraKikiMeter 1.29 | .NET/Mono, parst `Chat.log` per Regex (bestätigt via Decompile-Strings) – **kein Netzwerk** |
| rainy.ws (AionRainMeter) | Sagt selbst: "no network data involved" – **Chat-Log-basiert** |
| aiDPSMeter (aioninfo.com) | Laut Doku netzwerkbasiert via Npcap – Quellcode nicht öffentlich einsehbar |
| MyAion DPS Meter (myaion.eu) | Netzwerkbasiert (SharpPcap/PacketDotNet, wie unser Tool), **beim Nutzer installiert und läuft nachweislich korrekt** (Schaden/Heal/AP/GP werden live richtig angezeigt) – Route zur Konstanten-Übernahme ist aber **geschlossen**: die beiden schlanken eigenen DLLs haben gezielt beschädigte .NET-Metadaten, die IL-Decompiler (getestet mit ilspycmd, gegengeprüft an vier funktionierenden Fremd-DLLs im selben Ordner) aussteigen lassen. Kein Obfuskator (Symbolnamen bleiben Klartext), sondern ein gezielter Schutz genau gegen diese Analyse – wird respektiert, nicht umgangen. Wichtig aber: die Krypto-Spur in diesen DLLs (Rijndael/AES, `EncryptedSensitiveData`/`EncryptedPlayerId`, SignalR-Client) ist MyAions **eigene Backend-Telemetrie**, nicht die AION-Protokoll-Verschlüsselung – frühere Blowfish/BouncyCastle-Vermutung dazu war falsch. **Positiv bestätigt**: ein netzwerkbasierter Meter funktioniert auf diesem Server nachweislich, es fehlen nur die konkreten Konstanten (die MyAion offenbar in seiner separaten 212-MB-Bundle-DLL versteckt, die aus demselben Grund nicht angefasst wird).

Für einen echten netzwerkbasierten Meter für **AION 4.6** wurde das Protokoll aus dem
Open-Source-Emulator **Aion-Lightning** (Build-Generation 4.5/4.6, Repo `Rydiik/Aion-Server-4.6`)
rekonstruiert: Crypto-Schema, Paket-Framing und die Felder von `SM_ATTACK_STATUS` / `SM_ATTACK`.
Das ist eine **Hypothese, keine bestätigte Tatsache** – private Server verschieben Opcodes und
Konstanten gerne. Dieses Tool ist deshalb zunächst ein **Kalibrierungswerkzeug**, kein fertiger
Meter: es soll zeigen, ob die Annahmen für OriginAion stimmen, und wenn nicht, die echten Werte
sichtbar machen.

## Aktueller Stand: Netzwerkweg pausiert, Chat-Log-Fallback existiert

Die Aion-Lightning-Hypothese oben wurde gegen echte Mitschnitte geprüft und **widerlegt** – kein
Konstanten-Tausch (auch nicht andere dokumentierte Versionsstände 4.3/4.5/4.7/4.7.5) reproduziert
die echten Bytes. Der SM_KEY-Handshake selbst ist dabei vollständig verstanden (3 unabhängige
Sessions vermessen: 11 Byte, 7 feste Bytes `C9 3A 11 AD 97 9A 98` + 4 Byte Session-Seed, Boot-Key
beginnt `C2 3A`), aber der Body-Stream danach nicht. Eine anschließende Ghidra-Analyse (statisch,
nur Datei, kein laufender Prozess) beider Client-Binaries (32- und 64-Bit) ergab: **beide sind
durch einen kommerziellen Protector gepackt** (64-Bit: `aegisty64.bin`/`SecureEngineSDK64.dll`,
`.aion0`/`.aion1`-Sektionen, `.aion1` ist `rwx`; 32-Bit: zufällig benannte `rwx`-Sektionen, anderer
Packer). Der echte Code – und damit der Schlüssel – existiert nur im laufenden, entpackten
Prozess. Ein Versuch, den 64-Bit-Prozessspeicher per Windows-Bordmitteln auszulesen, kam mit 0
Byte zurück (vom Anti-Cheat blockiert). Aggressivere Wege (Debugger, Injektion) sind aus den
eingangs gesetzten Gründen (Account-/Bann-Risiko) ausgeschlossen, auch testweise.

Damit ist der Netzwerkweg für diesen Client **nicht abgebrochen, sondern an eine harte technische
Grenze gestoßen**, die mit den vereinbarten Regeln nicht überwindbar ist. Der Nutzer hat direkten
Kontakt zum OriginAion-Betreiber und eine Anfrage nach den Krypto-Parametern läuft. Bis (falls)
das etwas ergibt, existiert als Fallback ein **Chat-Log-basierter Parser** (`ChatLog/`) – gegen
eine echte, ~44.000 Zeilen lange OriginAion-`Chat.log` aus einer realen Spielsitzung entwickelt
und verifiziert (nicht nur handverlesene Beispielzeilen: die Regex-Muster wurden vorab in Python
gegen die komplette Datei getestet, um echte Abdeckung statt Vermutung zu messen – siehe
`ChatLogParser.cs`-Kommentare für die genauen Zahlen). Aufruf: `dotnet run -- chatlog
<Pfad-zu-Chat.log>`. Selbsttests: `Combat/SelfCheck.cs`, `RunChatLogParserScenario` (Dedup-Fall)
und `RunChatLogRealWorldPatternsScenario` (Crit/Skill/Incoming/Reflect/Heal-Varianten).

**Wichtiger Fund dabei**: Aion schreibt Zahlen mit `.` als Tausender-, nicht als Dezimaltrennzeichen
(`1.911` = 1911, nicht 1,911). Eine frühere Parser-Version hätte solche Zahlen um den Faktor ~1000
zu niedrig gelesen, ohne dass die Zeile als fehlerhaft aufgefallen wäre.

**Was der Chat-Log-Parser kann:**
- Schaden UND Heal, ausgehend UND eingehend → `DamageEvent`, läuft direkt in die bestehende
  `DpsCalculator`/`LiveAggregator`-Logik (unverändert, die war von Anfang an protokoll-unabhängig
  gebaut). Konkret abgedeckt (alle an der realen Datei verifiziert): normaler Schaden, kritischer
  Schaden (beide real vorkommenden Formulierungen), Skill-Schaden, Schaden durch Reflect,
  eingehender Schaden (beide Formen), DoT-Ticks mit erkennbarer Quelle ("... after you used ..."),
  Heilung an andere, Selbstheilung (für jeden sichtbaren Charakter, nicht nur "You"), Heilung durch
  andere (dritte und erste Person). Macht 95,5% aller "damage"/"HP"-Zeilen in der echten Testdatei
  aus.
- Bewusst NICHT geraten, weil die Zeile keine erkennbare Schadensquelle nennt (nur einen
  Skill-/Effektnamen): DoT-Ticks der Form "X received N damage due to the effect of Skill." –
  könnte der eigene oder ein fremder DoT sein, Raten würde ungefähr so oft falsch wie richtig liegen.
- Namen kommen direkt aus dem Log (`PlayerNameRegistry`) – kein `SM_GROUP_MEMBER_INFO`-Rätselraten
  nötig wie beim Netzwerkweg.
- Erkennt und verwirft zuverlässig Nicht-Kampf-Zeilen (Evade, "too far", Login, Chat-Kanäle,
  Buff-Ansagen ohne konkreten Betrag).
- Dedupliziert exakte Broadcast-Duplikate, wenn zwei Aion-Clients dieselbe `Chat.log`-Datei
  gemeinsam beschreiben (real beobachtet und verifiziert – siehe Klassendokumentation).

**Live-Tailing in der GUI: implementiert.** `Ui/MainWindow` startet beim Öffnen automatisch eine
`ChatLog/ChatLogTailer`-Instanz gegen `<AionInstallFolder>\Chat.log` (aus den Settings, siehe
"Aion-Installationsordner" im UI-Platzhalter-Abschnitt unten) und pollt sie per `DispatcherTimer`
(1s-Intervall). Setzt exakt
die vom Nutzer vorgegebene Regel um: `ChatLogTailer` springt im Konstruktor auf die AKTUELLE
Dateilänge (nie Zeile 1), und `Poll(paused)` rückt die Leseposition auch während Pause weiter
(verworfen, nicht aufgeschoben – ein Resume spielt nie nach, was währenddessen passiert ist, wie
bei einem echten, gerade angehaltenen Tonbandgerät). Neuer Aion-Ordner in den Settings → sofortiger
Neustart des Tailers, wieder ab dessen aktuellem Dateiende. `ChatLogParser.Parse` wurde dafür von
lokalen auf Instanz-Felder für den Dedup-Bucket umgestellt, damit ein Duplikat, das über zwei
Poll-Intervalle verteilt ankommt, weiterhin erkannt wird. Bewusst noch nicht gebaut: die
In-Game-Chat-Befehle (`.pause`/`.resume`/`.clear`/…, siehe unten) als Alternative zu den GUI-Buttons.

**Was noch fehlt (bewusst nicht geraten, siehe Doku in `ChatLogParser.cs`):**
- **Seltene Sonderformen** (unter 0,2% der Zeilen in der Testdatei): zusammengesetzte
  Status-Effekt-Zeilen ("... inflicted N damage AND the rune carve effect on ...") – andere
  Satzstruktur um "on" herum, nicht den Regex-Mehraufwand wert für den Anteil am echten Verkehr.
- **AP/Kinah/Loot**: reale Zeilenformate bestätigt (`"You have gained N Abyss Points."`, `"You have
  earned N Kinah."`, `"You have acquired [item:ID;verV;;;;]."`) – genau die fehlenden Datenquellen
  für den "Relic"-Mode bzw. die Loot-Table-Ansicht (siehe UI-Platzhalter unten), aber noch nicht
  implementiert; kein `DamageEvent`, bräuchte einen eigenen Event-Typ und eigene UI-Anbindung.
- **"You"-Mehrdeutigkeit bei zwei gleichzeitig kämpfenden Clients**: siehe `PlayerNameRegistry`-
  Doku – wenn zwei Clients dieselbe `Chat.log` teilen UND beide gleichzeitig kämpfen, lässt sich
  "You" nicht mehr eindeutig einem der beiden zuordnen. Im aktuellen Setup des Nutzers (ein
  Charakter kämpft, der zweite steht nur für die Gruppenanforderung herum) tritt das nicht auf.
- **`ShugoConsole`**: das vom Nutzer benutzte Tool, um die Client-interne "Basic Chatlog"-Option
  (`builder_dev_dialog` in `L10N/2_eng/data/ui/UI_Game.xml`, ein normalerweise GM-gebundenes
  Debug-Menü) freizuschalten. Undokumentiert, keine öffentliche Analyse – vermutlich ein
  Speicher-Patch im laufenden Prozess, damit potenziell im selben Risikobereich wie der oben
  ausgeschlossene Live-Speicherzugriff. Nicht von diesem Projekt geprüft oder empfohlen.

## Protokoll-Zusammenfassung (Quelle: Aion-Lightning-Emulator)

Jedes Server→Client-Paket auf dem TCP-Stream:

```
[2 Byte LE: Gesamtlänge L] [L-2 Byte: verschlüsselte Payload]
```

Payload (nach Entschlüsselung):

```
[2 Byte: verschleierter Opcode] [1 Byte: 0x43] [2 Byte: ~Opcode (Checksumme)] [Body...]
```

- **Verschlüsselung**: XOR-Stream-Cipher mit Ciphertext-Feedback (selbstsynchronisierend) –
  jedes Byte wird mit einer 64-Byte-Statik-Tabelle, einem rotierenden 8-Byte-Sitzungsschlüssel
  und dem vorherigen Geheimtext-Byte verXORt. Dadurch vollständig aus dem Mitschnitt alleine
  entschlüsselbar, **wenn** der Session-Key bekannt ist.
- **Session-Key**: Das allererste Serverpaket (`SM_KEY`, unverschlüsselt, 11 Byte) enthält einen
  verschleierten `falseKey`; `realKey = (falseKey - 0x3FF2CCCF) XOR 0xCD92E4DD`. Deshalb **muss
  die Aufnahme laufen, bevor sich der Client mit dem Game-Server verbindet** (Login/Channel-Wechsel
  während der Aufnahme provozieren).
- **Opcode-Verschleierung**: `obfuscated = (real + 0xCC) XOR 0xDD`.

Kandidaten-Opcodes (unbestätigt für OriginAion):
- `0x05` = `SM_ATTACK_STATUS` – HP/MP/FP-Tick (Schaden negativ ⇒ DMG, positiv ⇒ **HEAL**/Regen),
  14 Byte Body. Heal-Tracking braucht also keinen eigenen Opcode, nur eine Unterscheidung
  nach `type`/`logId`.
- `0x36` = `SM_ATTACK` – direkter Skill/Attacke mit Trefferliste, Damage-Feld ist das, was ein
  DMG-Meter eigentlich braucht
- `0x19` = `SM_SYSTEM_MESSAGE` – generische lokalisierte Textnachricht (Chat-Fenster-Hinweise).
  Wird u.a. für **AP-/GP-Gewinn** benutzt ("You have earned %num0 Abyss/Glory Points") und für
  eine rein textuelle Kampf-Narration ("You inflicted %num1 damage on %0") – gut als
  menschenlesbarer Gegen-Check zu `SM_ATTACK`. Zahlenwerte stehen hier als UTF-16LE-**Text**
  im Paket, nicht als Binärzahl. Die konkrete `msgCode` für AP/GP-Gewinn ist im Emulator-Quelltext
  selbst uneindeutig (zwei verschiedene Zahlen an zwei Stellen: `1320000`/`1300965` für AP,
  `1402081`/`1402219` für GP) – muss im echten Traffic beobachtet werden.
- `0x0E` = `SM_NPC_INFO` – NPC wird sichtbar: `objectId`, `npcId` (Monster-Template-ID – das Feld,
  auf das eine Boss-Erkennungsliste zielen würde), `nameId`. Fixe Header-Felder, danach folgen
  Ausrüstung/Buffs variabler Länge (nicht geparst).
- `0x16` = `SM_DELETE` – Objekt (NPC oder Spieler) verschwindet aus der Sicht. Kandidat für
  "Kampf/Boss beendet", aber nicht eindeutig von "außer Sichtweite" unterscheidbar ohne
  zusätzlichen Kontext (z.B. HP zuvor auf 0 über `SM_ATTACK_STATUS`).
- `0x5B` = `SM_GROUP_MEMBER_INFO` – Gruppen-Roster-Update: `objectId`, HP/MP/FP, Position,
  `classId`, `level`, und (je Event-Typ) der Spielername als UTF-16LE-Text. **Kein Rassenfeld** –
  AION-Gruppen sind immer einfaktionig, die Rasse ist implizit die des lokalen Spielers. Bester
  Kandidat für Namens-/Klassenauflösung der eigenen Gruppe.

Alle Konstanten stehen zentral in `Crypto/AionCrypt.cs` (Krypto) und `Protocol/Opcodes.cs`
(Opcodes) – dort anpassen, falls sich beim Kalibrieren zeigt, dass sie nicht passen.

## Setup (Windows)

1. **Npcap** installieren: <https://npcap.com/> – im Installer **"Install Npcap in WinPcap
   API-compatible Mode"** aktivieren (Pflicht für SharpPcap).
2. **.NET SDK** installieren: <https://dotnet.microsoft.com/download>
3. Bauen (im Repo-Root):
   ```
   dotnet build
   ```
4. Als Administrator ausführen (Paket-Capture braucht erhöhte Rechte):
   ```
   dotnet run -- <deviceIndex>
   ```
   Ohne Argumente listet das Tool alle Netzwerk-Interfaces mit Index auf.
5. Für das GUI-Grundgerüst (siehe "UI-Platzhalter" unten): `dotnet run -- gui` – öffnet das
   WPF-Hauptfenster, mit "Load Demo Data"-Button zum Prüfen ohne Capture.

## Kalibrierungsablauf

1. AION-Client **schließen**.
2. `AionSniffer` starten (Device-Index wählen – i.d.R. das aktive Ethernet/WLAN-Interface).
3. Client starten und einloggen, während der Sniffer läuft.
4. Beobachten:
   - Kommt `[diag] ...: looks like the Aion game server stream, attaching decoder.` und danach
     `handshake captured, base key derived` → Server-Verbindung korrekt erkannt.
   - Kommen danach laufend `[0x.... len=...]`-Zeilen mit **lesbarem Text im ASCII-Preview**
     (z.B. Spielernamen, Chat, Zonennamen) → Entschlüsselung ist korrekt kalibriert.
   - Kommt stattdessen nur Kauderwelsch oder `checksum mismatch` → die Krypto-Konstanten
     (`StaticServerPacketCode`, `KeySuffix`, die SM_KEY-Formel) passen nicht zu 4.6 und müssen
     nachjustiert werden (typischerweise Off-by-eine-Konstante gegenüber einer benachbarten
     Client-Version).
5. Im Kampf: nach Zeilen mit `0x0005` bzw. `0x0036` suchen. Passen die decodierten Damage-Werte
   zu dem, was tatsächlich im Kampf-Log/auf dem Bildschirm zu sehen war (Größenordnung, Vorzeichen,
   HP%-Werte plausibel)? Falls nicht: mit `grep` nach Paketen suchen, die in der richtigen
   Größenordnung ein plausibles `i32`-Feld enthalten – das sind dann die echten Opcodes für
   OriginAion, `Protocol/Opcodes.cs` entsprechend anpassen.
6. Beim Erhalt von AP (Kill/Objective in der Abyss) bzw. GP (Glory-Arena): nach `0x0019`-Zeilen
   mit `[AP/GP CANDIDATE]`-Markierung suchen und die tatsächliche `code`-Zahl notieren.

## Anforderungen an den fertigen Meter (Stand dieser Session)

- **DMG** (Kern) – über `SM_ATTACK` / `SM_ATTACK_STATUS`
- **DPS vs. iDPS** (finale, in sich konsistente Definition – vom Nutzer durch eigenes Live-Testen
  von MyAion bestätigt, deckt sich mit der Zahlen-Gegenprobe unten):
  - **iDPS** = Schaden an **einem konkreten Ziel** (ein Mob/Boss) ÷ Kampfdauer mit *diesem* Ziel
    (erster Treffer bis Tod), mit **einer gemeinsamen Kampfdauer für die ganze Gruppe** – nicht
    individuell pro Spieler gemessen. Grund für die gemeinsame Dauer: bei unterschiedlichen
    Spielweisen (Zauberer nutzt alle 60s einen großen Skill vs. Gladiator haut permanent zu) wäre
    eine individuelle Pro-Spieler-Zeitmessung unfair – der Zauberer hätte dann eine winzige
    "aktive Zeit" und einen künstlich aufgeblasenen Wert. Bei mehreren Bossen in einer Instanz
    wird daraus ein **gewichteter Durchschnitt**: Summe(Schaden an allen Bossen) ÷ Summe(deren
    Kampfdauern) – die einzelne Zielkampf-Formel verallgemeinert auf mehrere Encounter, kein
    Widerspruch dazu.
  - **DPS** in der "ALL"-/Open-World-Ansicht = Schaden über **beliebig viele, wechselnde Ziele**
    ÷ verstrichene reale Zeit. Hier ist es tatsächlich uneindeutig, ob zeitliche Lücken zwischen
    einzelnen Kills mitgezählt werden (z.B. alle 5s einen Mob für 2s bekämpfen, dazwischen laufen)
    – je nachdem ergeben sich unterschiedliche Werte. **Das war das ursprüngliche
    Gladiator/Zauberer-Beispiel** – es bezog sich also gar nicht auf iDPS, sondern auf genau diese
    Lücken-Frage in der globalen "ALL"-Ansicht.
  - **Auf myaion.eu heißt iDPS in der UI schlicht "DPS"** (`bossDPS`/`allDPS`) – die Webseite zeigt
    für einen einzelnen Bosskampf effektiv iDPS an, nennt es aber nicht so. Das erklärt die
    Zahlen-Gegenprobe unten: bei einem Einzelziel *ist* iDPS = "die eine gemeinsame Kampfdauer",
    kein Sonderfall.
  - Braucht zusätzlich zur Schadens-Erkennung: eine Boss-/Ziel-Erkennung (NPC-Objekt-IDs) und
    Kampf-Start/Ende-Events (erster Treffer / Death-Paket) – siehe "Nächste Schritte".
  - **Vorsicht**: In dieser Session wurden mehrfach ausführliche ChatGPT-generierte Erklärungen
    zu iDPS/Boss-Erkennung eingebracht, die im Kern brauchbar waren, aber auch sehr konkret
    klingende, unbestätigte Details enthielten (exakte NPC-Template-IDs einzelner Bosse,
    Opcode-Namen, C++-Code, eine SQL-Query gegen ein angeblich bekanntes Datenbankschema, ein
    Feature-Vergleich aiDPS-vs-MyAion, und – **inzwischen widerlegt** – die Behauptung "Der Timer
    ist pro Spieler, nicht global" samt erfundenem 120,0s-vs-118,2s-Beispiel). Diese
    Detailbehauptungen wurden **nicht** übernommen. Bosse/Opcodes/IDs für den echten Meter müssen
    aus verifizierten Quellen kommen (eigene Kalibrierung – MyAion-Dekompilierung ist keine
    Option mehr, siehe Tabelle oben), nicht aus unbelegten KI-Antworten.
  - **Zahlen-Gegenprobe an echten myaion.eu-Sessions** (`/PvESession/1716393`,
    `/PvEPlayerSession/8602573`): für alle 6 Spieler ergibt Schaden ÷ angezeigte DPS praktisch
    dieselbe Zeit (~192,4–192,5s, Abweichung nur Rundung der ganzzahligen DPS-Anzeige) – bestätigt
    die gemeinsame Kampfdauer. Kein Vorkommen von "iDPS" als Begriff auf beiden Seiten (passt: die
    UI nennt es einfach "DPS", siehe oben). Zusätzlich aus der Skill-Tabelle der Einzelansicht
    bestätigt: `Damage = Uses × Avg` (z.B. 30 × 287.484 ≈ 8.624.524 ✓), `Crit-% = Crit ÷ Uses`,
    die Prozentangabe hinter dem Skill-Schaden ist der Anteil am **Gesamtschaden des Spielers**
    (nicht am Bossschaden).
- **HEAL** – dieselben Pakete, positiver statt negativer Wert
- **AP (Abyss Points)** und **GP (Glory Points)** – vermutlich über `SM_SYSTEM_MESSAGE`,
  `msgCode` noch zu bestätigen (siehe oben)
- **Rasse (Elyos/Asmodian) und Klasse pro Spieler** – steckt vermutlich im noch nicht
  identifizierten Spawn-Paket (im Emulator `SM_PLAYER_INFO`/`SM_PLAYER_SPAWN` o.ä.), zusammen mit
  dem Namen
- **Skill-Erkennung – ID→Name-Auflösung erledigt**: `Data/SkillDatabase.cs` lädt
  `assets/skills/skills_en_4x.json` (974 Skills, siehe `assets/README.md`) und löst die
  `skillId` aus `SM_ATTACK_STATUS` jetzt direkt in einen Namen auf, statt nur die rohe Zahl
  anzuzeigen. Nur `SM_ATTACK_STATUS` trägt eine `skillId` im Paket – `SM_ATTACK` (der direkte
  Schwung-Schaden) offenbar nicht, siehe Struktur oben. Icon-Auflösung (Dateiname → tatsächliches
  Bild) noch nicht angebunden, das kommt mit der UI.
- **UI** – Live-Overlay (WPF/WinForms) mit Rangliste pro Spieler, DPS/HPS-Verlauf, ähnlich
  UltraKikiMeter/rainy.ws, aber mit Live-Netzwerkdaten statt Chat-Log-Nachlese
  - **Live-Anzeige während der Aufnahme**: DPS (Gesamtschaden ÷ verstrichene Aufnahmezeit, die
    "ALL"-Rate) muss durchgehend berechnet und angezeigt werden, solange aufgenommen wird – immer
    wohldefiniert, unabhängig davon, wie viele verschiedene Ziele getroffen wurden. iDPS wird
    daneben angezeigt, aber nur wenn wohldefiniert: sobald die Aufnahme mehrere unterschiedliche
    Ziele/Encounter mischt (offene Welt, wechselnde Mobs), gibt es keine einzelne gemeinsame
    Kampfdauer mehr, durch die man teilen könnte. In dem Fall statt einer (irreführenden) Zahl
    einen kleinen Hinweis zeigen: "iDPS in der ALL-Ansicht nicht verfügbar – funktioniert nur pro
    Ziel." iDPS wird also nur angezeigt, wenn die aktuelle Ansicht/Aufnahme auf ein einzelnes Ziel
    eingegrenzt ist (oder bisher nur eines getroffen hat).
- **Mehrsprachigkeit** – die *Erkennung* ist bauartbedingt schon sprachunabhängig: auf dem Draht
  stehen nur numerische IDs (`skillId`, `msgCode`, Rassen-/Klassen-Enums), keine lokalisierten
  Texte. Das ist der Kernunterschied zu UltraKikiMeter/rainy.ws, die pro Sprache eigene
  Regex-Parser für den lokalisierten Chat-Text brauchen. Sprachabhängig ist nur die *Anzeige*
  (ID → Name) – dafür bräuchte man die Namenstabelle aus dem Client selbst
  (`L10N/<sprache>/data/data.pak`, proprietäres AION-PAK-Format, aktuell nur `eng` installiert)
  oder eine eigene, von Hand gepflegte ID→Name-Tabelle. Die UI-Texte des Meters selbst (Labels,
  Buttons) sind unabhängig davon triviale eigene i18n-Strings.

## Nächste Schritte (nach erfolgreicher Kalibrierung)

- **Namensauflösung + Klasse**: Kandidat jetzt implementiert (`0x5B`/`SM_GROUP_MEMBER_INFO`,
  `CombatPacketParser.TryDescribeGroupMemberInfo`) – liefert `objectId`, `classId`, `level` und
  (je Event) den Namen für die eigene Gruppe. Muss im Kalibrierungslauf verifiziert werden.
  Deckt nur Gruppenmitglieder ab, keine beliebigen sichtbaren Spieler.
- **Boss-Erkennung für iDPS**: Kandidaten jetzt implementiert – `0x0E`/`SM_NPC_INFO` liefert
  `objectId` + `npcId` (Template-ID) beim Sichtbarwerden, `0x16`/`SM_DELETE` liefert `objectId`
  beim Verschwinden (Tod oder außer Sichtweite – nicht eindeutig unterscheidbar, ggf. mit
  "HP zuvor auf 0%" aus `SM_ATTACK_STATUS` kombinieren). Die Boss-Template-ID-Liste selbst muss
  aus echten Beobachtungen der eigenen Kalibrierung kommen (MyAion-Dekompile ist keine Option
  mehr, siehe Tabelle oben), nicht aus unbestätigten Listen – siehe Warnhinweis oben.
- **Skill-/Klassen-Assets gesammelt**: siehe [`assets/README.md`](assets/README.md) – 974
  Skillnamen+Icons (Englisch, `aioncodex.com`-Bucket `/4x/`) und Klassennamen in EN/DE/FR/RU
  (aus der aktuellen DB, nicht index-verifiziert zwischen den Sprachen). Aethertech/Songweaver/
  Gunslinger gehören laut Nutzer regulär zu 4.6 (nur von `OriginAion` deaktiviert) – der Meter
  zielt ohnehin allgemein auf 4.6-Server, nicht nur `OriginAion`. Klassen-/Rassen-Icons inzwischen
  von `myaion.eu` übernommen (siehe `assets/README.md`), Zuordnung einiger Dateinamen zu den
  finalen Klassennamen aber noch ungeklärt.
- **Online-Session-Sharing** (Idee von `myaion.eu` übernommen): Bosskämpfe als Web-Link teilbar
  machen (Gruppenansicht + Skill-für-Skill-Einzelansicht, siehe `assets/README.md`). Große,
  spätere Ausbaustufe – braucht eigenes Backend/Hosting, kommt erst nach dem lokalen Client.
- **DPS/iDPS-Berechnung implementiert** (`Combat/DpsCalculator.cs`): reine, protokollunabhängige
  Logik nach der oben festgelegten Definition (ALL-Ansicht wallclock/active-only, iDPS pro Ziel
  mit gemeinsamer Kampfdauer, Instanz-iDPS als gewichteter Durchschnitt). Per
  `dotnet run -- selftest` ohne Capture verifizierbar – reproduziert sowohl das
  Gladiator/Zauberer-Gedankenexperiment als auch die echten myaion.eu-Zahlen
  (84.360.277 Schaden / 438.288 iDPS aus `/PvESession/1716393`, exakt getroffen). Von
  `terminal_windows` gebaut+ausgeführt: 0 Warnungen, 0 Fehler, alle Checks bestanden.
  **Nachgebessert nach Rückmeldung**: bei komplett isolierten Einzeltreffern (keine zwei Treffer
  näher als die Idle-Schwelle) hat "active-only" keine sinnvolle Zeitbasis – `AllDpsActiveOnly`
  gibt jetzt `null` zurück statt (wie in der ersten Fassung) die rohe Schadenssumme als
  vermeintliche Rate auszugeben. Grund: eine "250.000 iDPS"-Anzeige ohne Kontext sieht in der
  späteren UI wie ein Bug aus, nicht wie "nicht definiert" – das fiel beim Testen auf den echten
  Zahlen auf. **Jetzt an echte Pakete angebunden**: `AionSession.PacketDecoded` trägt einen
  Zeitstempel (aus dem Capture, `RawCapture.Timeval.Date`) mit, `Combat/LiveAggregator.cs`
  wandelt jedes decodierte `SM_ATTACK` in `DamageEvent`s um und der Kalibrierungslauf zeigt
  live eine Schadens-/DPS-Zeile pro Angreifer-ObjectID. `SM_ATTACK_STATUS` wird dafür bewusst
  NICHT verwendet – dieser Paket-Layout-Stand trägt kein Angreifer-Feld (siehe
  `CombatPacketParser`), HP/MP-Ticks lassen sich also nicht einem Spieler zuordnen. Per
  Selbsttest verifiziert (Objekt-Ebene, ohne Bytes/Capture).
  **Nachgebessert (Fund von `terminal_windows`)**: derselbe "Summe als Rate ausgegeben"-Bug wie
  bei `AllDpsActiveOnly` steckte unverändert in `AllDpsWallClock` – bei genau einem Treffer (dem
  Normalfall für die allererste Zeile jedes echten Laufs, kein Randfall) kam die rohe
  Schadenssumme als vermeintliche DPS-Zahl heraus (z.B. "50000 DPS" für einen einzelnen
  50.000-Schadens-Treffer). Jetzt ebenfalls auf `double?` gezogen, gibt `null`/"n/a" zurück statt
  der Summe. Der Selbsttest prüft jetzt den tatsächlich gerenderten Text der Ausgabe, nicht nur
  die zugrundeliegenden Summen – genau das hatte den Bug beim ersten Mal durchrutschen lassen.
- **Mehrfach-Treffer/Schild-Varianten in `SM_ATTACK` – erledigt**: `CombatPacketParser.TryParseAttack`
  liest jetzt die komplette variable-length Trefferliste (jeder Treffer: Schaden, Status,
  Schild-Typ, plus die vom Schild-Typ abhängigen Zusatzfelder aus `SM_ATTACK.java` – 0/12/28
  Byte je nach Typ), nicht mehr nur den ersten Treffer. Liefert ein strukturiertes `AttackPacket`
  (Attacker/Target/Hits-Liste), nicht nur einen Debug-String – bereit, um später direkt in
  `DamageEvent`s umgewandelt zu werden.
- **AP/GP-`msgCode` bestätigen** (siehe Kalibrierungsablauf Punkt 6) und ggf. `SM_SYSTEM_MESSAGE`
  gezielt danach filtern statt der generischen Textausgabe.
- **Skill-Namen/Icons**: Skill-ID → Name/Icon-Mapping wird eine reine Datentabelle sein, kein
  weiteres Netzwerk-Reverse-Engineering. Der Nutzer hat `aioncodex.com` als mögliche Bildquelle
  genannt – vor dem Scrapen/Hotlinken dort erst die Nutzungsbedingungen der Seite prüfen; robuster
  wäre, Skill-Icons direkt aus den lokalen Client-Assets zu extrahieren (`OriginAion`-Installation
  enthält vermutlich die Original-`.dds`/Sprite-Dateien), das ist rechtlich unproblematischer, da
  es der eigene Client ist.
- **Aggregation**: sobald Rohdaten stimmen, ist die Aggregation (DPS/HPS pro Spieler, Fenster,
  Encounter-Erkennung, AP/GP-Zähler, Export) reine Anwendungslogik ohne weitere
  Reverse-Engineering-Risiken.
- **UI – Grundgerüst steht** (`Ui/`, WPF): Hauptfenster + Settings- und Network-Settings-Dialoge,
  angelehnt an den vom Nutzer geteilten Screenshot der MyAion-DPS-Meter-UI. Startet über
  `dotnet run -- gui`. Noch nicht an die echte Capture-Pipeline angebunden (siehe
  `MainWindow.OnAttackDecoded` – dafür vorbereitet, aber noch nicht aus `Program.cs` aufgerufen);
  über den "Load Demo Data"-Button aber schon jetzt visuell prüfbar. Siehe den eigenen Abschnitt
  unten für alle UI-Platzhalter im Detail.

## UI-Platzhalter (aktueller Stand)

Der Nutzer hat Screenshots der echten MyAion-DPS-Meter-UI geteilt; das WPF-Grundgerüst
(`Ui/MainWindow.xaml` + zugehörige Fenster) übernimmt deren Struktur, aber nur "Damage
Distribution" hat eine echte Datenquelle. Damit nichts als fertig missverstanden wird, hier
explizit aufgelistet, was noch fehlt und was jeweils dafür nötig wäre (auch einzeln als Tooltip
in der jeweiligen UI-Komponente hinterlegt):

- **Linke Icon-Leiste** (5 Ansichten laut Nutzer): nur *Damage Distribution* ist implementiert.
  - *Skill-Rotation*: braucht Tracking der Skill-Cast-Reihenfolge (zeitlich sortierte Skill-IDs
    pro Spieler) – noch nicht gebaut.
  - *Loot-Table*: braucht Dekodierung von Item-Drop-Paketen – noch nicht identifiziert.
  - *Character-Profile*: braucht Ausrüstungs-/Stat-Pakete – noch nicht identifiziert.
  - *Instance-Scores*: braucht Boss-Erkennung + Aggregation über mehrere Bosskämpfe hinweg
    (siehe iDPS-Definition oben) – Boss-Erkennungs-Opcodes sind Kandidaten, aber unbestätigt.
- **"Mode"-Menü**: nur *Damage* funktioniert.
  - *Heal*: `SM_ATTACK_STATUS` (das Heal-Tick-Paket) trägt in diesem Layout-Stand kein
    Angreifer-/Heiler-Feld – Heilung lässt sich damit keinem Spieler zuordnen (siehe oben).
  - *Relic*: keine AP/GP-Paketquelle angebunden (Kandidat `SM_SYSTEM_MESSAGE`, `msgCode`
    unbestätigt, siehe oben).
- **Filter-Dropdown "Source"** (vom Nutzer korrigiert, hieß zwischenzeitlich "Scope"): reiner
  Anzeigefilter, sammelt immer weiter für alle Spieler unabhängig von der Auswahl (z.B. bei einer
  4×6-Allianz zeigt "Group" nur die eigene Gruppe an, verwirft aber nichts). Braucht
  Gruppen-/Allianz-/Rassen-Zuordnung, die wir noch nicht dekodieren (`SM_GROUP_MEMBER_INFO` hat
  kein Rassenfeld, siehe oben).
- **Filter-Dropdown "Mob/Boss"**: implementiert. Listet die tatsächlich getroffenen Ziele dieser
  Session (aus `LiveAggregator.Events` abgeleitet), inkl. echter Filterung: "All" zeigt pro Quelle
  die Gesamtschaden/DPS-Zahl über alle Ziele hinweg (die absichtlich mehrdeutige Wall-Clock-Sicht,
  siehe `DpsCalculator`), die Auswahl eines konkreten Ziels schaltet die Spalte auf echtes
  Ziel-iDPS um (`DpsCalculator.TargetIDps`, gemeinsames Engagement-Fenster) – die Spaltenüberschrift
  wechselt dabei sichtbar zwischen "DPS" und "iDPS". Fehlt weiterhin: Zielnamen kommen aktuell nur
  für die Demo-Daten aus einer manuell gesetzten Tabelle (`MainWindow._targetNames`) – für den
  Netzwerk- oder Chat-Log-Pfad braucht es noch eine echte Namensquelle für NPCs/Bosse.
- **"Session"-Menü**: zeigt aktuell nur "No sessions recorded yet" – keine Session-Persistenz
  vorhanden.
- **"App"-Menü**: nur *Close*, *Load Demo Data* und *Clear sessions* sind echt. Alles andere
  (Load/Save/Export/Validate session, Sessions-/Logs-Ordner öffnen, Reset connection, Check for
  updates, Minimize to system tray) ist deaktiviert, weil dafür Session-Persistenz, ein Log-System
  bzw. ein System-Tray-Icon fehlen. **Bewusst nicht einmal als Platzhalter-Stub implementiert**:
  "Start automatically with Windows" – das würde in die Windows-Autostart-Registry schreiben, eine
  systemweite Änderung, die eine explizite Anfrage braucht, keine UI-Parity-Checkbox.
- **"Settings"-Menü** (neu, aus "App" herausgelöst auf Wunsch des Nutzers – "App" war zu einer
  langen, gemischten Ansammlung aus Session-/Dev-/Settings-Einträgen geworden, ohne festen Platz
  für Einstellungen): *App Settings* und *Always on top* sind echt, *Profile Settings* und
  *Key bindings* weiterhin Platzhalter aus denselben Gründen wie oben.
- **"Network"-Menü**: hier ist die Geräteliste echt (`SharpPcap.CaptureDeviceList`), Auswahl wird
  in `MeterSettings.SelectedCaptureDeviceName` gespeichert – aber das tatsächliche Starten der
  Aufnahme aus der GUI heraus ist noch nicht verdrahtet (`Program.cs`s Konsolen-Einstieg ist
  weiterhin der einzige Weg, eine Aufnahme zu starten).
- **Settings-Dialog**: bewusst nur die Checkboxen übernommen, die zu vorhandenen Datenfeldern
  passen (Target-Bar, Players-List, Targets-List, Theme/Font). Loot-Tabelle, Kinah-Tracking,
  Auto-Upload und Donation-Goal aus der MyAion-Referenz fehlen komplett – dafür bräuchte es
  jeweils eigene Paketquellen bzw. ein eigenes Backend (siehe nächster Punkt).
- **Aion-Installationsordner** (implementiert): eigene Gruppe im Settings-Dialog, angelehnt an die
  vom Nutzer geteilte AionRainMeter/rainy.ws-Referenz-UI ("Log path: select Aion folder ROOT").
  Ordnerauswahl über `Microsoft.Win32.OpenFolderDialog` (kein WinForms nötig), Validierung prüft auf
  `bin64\game.dll`/`AION.bin` ("Wrong path selected!" analog zur Referenz) und meldet separat, ob
  `Chat.log` dort schon existiert. Persistiert in `MeterSettings.AionInstallFolder` und wird von
  `MainWindow` direkt für das Live-Tailing genutzt (siehe oben) – Ändern und Speichern in den
  Settings startet den Tailer sofort neu, wieder ab dem dann aktuellen Dateiende.
- **In-Game-Commands** (vom Nutzer als Referenz geteilt, z. B. `.pause`/`.resume`/`.clear`/`.dmg`/
  `.heal` als Chat-Zeilen statt GUI-Klicks): passt konzeptionell gut zur "keine Vergangenheit
  lesen"-Regel oben (`.pause` würde exakt steuern, was aufgezeichnet wird). Das Live-Tailing, das
  dafür nötig ist, steht jetzt (siehe oben) – es fehlt noch eine Erkennung, welche Chat.log-Zeile die
  eigene, gerade abgeschickte Chat-Nachricht ist.

## Geplant: eigenes Backend + Webseite für Session-Uploads

Der Nutzer möchte perspektivisch ein eigenes Backend mit Webseite, auf der man hochgeladene
Sessions ansehen kann – analog zu myaion.eu's `/PvESession/<id>` (Gruppenansicht) und
`/PvEPlayerSession/<id>` (Skill-für-Skill-Einzelansicht), siehe `assets/README.md` für die dort
schon dokumentierte Feldstruktur. Das ist bewusst als **große, spätere Ausbaustufe** eingeordnet:
braucht ein eigenes Backend (API + Datenbank) und Hosting, und ist nicht Teil der aktuellen
Windows-Client-Architektur. Kommt erst, wenn der lokale Meter selbst zuverlässig funktioniert.

## Rechtlicher Hinweis

Passives Mitlesen des eigenen Netzwerkverkehrs ist technisch unproblematisch, kann aber je nach
Regelwerk des privaten Servers (OriginAion) gegen dessen Nutzungsbedingungen verstoßen. Vor
produktivem Einsatz kurz deren Regeln zu Drittsoftware/Addons prüfen.
