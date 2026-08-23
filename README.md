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
- **Skill-Erkennung** – die `skillId` steht schon in `SM_ATTACK_STATUS`/`SM_ATTACK`; fehlt noch
  die Zuordnung ID → Skillname/Icon (reine Datentabelle, kein Reverse-Engineering mehr, siehe
  unten)
- **UI** – Live-Overlay (WPF/WinForms) mit Rangliste pro Spieler, DPS/HPS-Verlauf, ähnlich
  UltraKikiMeter/rainy.ws, aber mit Live-Netzwerkdaten statt Chat-Log-Nachlese
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
- **Mehrfach-Treffer/Schild-Varianten in `SM_ATTACK`**: aktuell wird nur der erste Treffer mit
  `shieldType == 0` sauber geparst; AoE-Skills mit mehreren Zielen oder reflektierte/geblockte
  Treffer brauchen die variable-length-Felder aus dem Original-`SM_ATTACK.java`.
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
- **UI**: danach ein Live-Overlay/Fenster (WPF oder WinForms), das die aggregierten Werte
  anzeigt – Rangliste pro Spieler (inkl. Rasse/Klassen-Icon), DPS/HPS-Verlauf, AP/GP-Zähler,
  ähnlich UltraKikiMeter/rainy.ws, aber mit den netzwerkbasierten Live-Daten statt
  Chat-Log-Nachlese.

## Rechtlicher Hinweis

Passives Mitlesen des eigenen Netzwerkverkehrs ist technisch unproblematisch, kann aber je nach
Regelwerk des privaten Servers (OriginAion) gegen dessen Nutzungsbedingungen verstoßen. Vor
produktivem Einsatz kurz deren Regeln zu Drittsoftware/Addons prüfen.
