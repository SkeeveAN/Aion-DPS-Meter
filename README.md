# AionSniffer – Netzwerkbasierter DMG-Meter für AION 4.6 (OriginAion)

## Stand der Analyse

Drei bestehende Tools wurden analysiert:

| Tool | Methode |
|---|---|
| UltraKikiMeter 1.29 | .NET/Mono, parst `Chat.log` per Regex (bestätigt via Decompile-Strings) – **kein Netzwerk** |
| rainy.ws (AionRainMeter) | Sagt selbst: "no network data involved" – **Chat-Log-basiert** |
| aiDPSMeter (aioninfo.com) | Laut Doku netzwerkbasiert via Npcap – Quellcode nicht öffentlich einsehbar |

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
- **iDPS** ("aktiver" DPS, nur die Zeit zählt, in der tatsächlich Schaden gemacht wurde) neben
  normalem DPS – blendet lange Lücken zwischen großen Skills aus (Beispiel: Zauberer mit 60s-CD
  vs. Gladiator mit konstantem Auto-Attack/Weaving sollen bei iDPS vergleichbar sein, nicht nur
  bei rohem DPS). Reine Aggregations-Logik auf Basis von (Zeitstempel, Schaden) pro Spieler –
  braucht keine zusätzlichen Pakete. Offene Design-Frage für später: ab welcher Lückenlänge gilt
  ein Zeitabschnitt als "inaktiv" (Default-Vorschlag: > 3s ohne ausgehenden Schaden dieses
  Spielers zählt nicht in die iDPS-Zeitbasis).
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

- **Namensauflösung + Rasse/Klasse**: Object-IDs → Spielername/Rasse/Klasse brauchen den
  Spawn-Opcode – noch nicht identifiziert, aber nach demselben Muster wie `SM_ATTACK_STATUS`
  ableitbar, sobald der Opcode bestätigt ist. Vermutlich ein einziges Paket liefert alle drei
  Felder zusammen mit der Object-ID.
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
