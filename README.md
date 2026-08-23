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
- `0x05` = `SM_ATTACK_STATUS` – HP/MP/FP-Tick (Schaden negativ, Heal positiv), 14 Byte Body
- `0x36` = `SM_ATTACK` – direkter Skill/Attacke mit Trefferliste, Damage-Feld ist das, was ein
  DMG-Meter eigentlich braucht

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

## Nächste Schritte (nach erfolgreicher Kalibrierung)

- **Namensauflösung**: Object-IDs → Spielernamen brauchen den Spawn-Opcode (im Emulator
  `SM_PLAYER_INFO`/`SM_PLAYER_SPAWN` o.ä.) – noch nicht implementiert, aber nach demselben Muster
  wie `SM_ATTACK_STATUS` ableitbar, sobald der Opcode bestätigt ist.
- **Mehrfach-Treffer/Schild-Varianten in `SM_ATTACK`**: aktuell wird nur der erste Treffer mit
  `shieldType == 0` sauber geparst; AoE-Skills mit mehreren Zielen oder reflektierte/geblockte
  Treffer brauchen die variable-length-Felder aus dem Original-`SM_ATTACK.java`.
- **Aggregation/UI**: sobald Rohdaten stimmen, ist die Aggregation (DPS pro Spieler, Fenster,
  Export) reine Anwendungslogik ohne weitere Reverse-Engineering-Risiken.

## Rechtlicher Hinweis

Passives Mitlesen des eigenen Netzwerkverkehrs ist technisch unproblematisch, kann aber je nach
Regelwerk des privaten Servers (OriginAion) gegen dessen Nutzungsbedingungen verstoßen. Vor
produktivem Einsatz kurz deren Regeln zu Drittsoftware/Addons prüfen.
