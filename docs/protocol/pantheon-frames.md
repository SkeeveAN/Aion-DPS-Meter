# Pantheon-Frames (belegt 27.10.2026)

Das Pantheon (Fenster mit 17 Dekoration-Slots, drei Aufstellungen/Presets) kommt nur für den **eigenen Charakter**. Beim Login ist es schon vollständig im Strom, die Fenster-Frames liefern Änderungen.

| Frame | Inhalt | Aufbau |
|---|---|---|
| `0x8ce2` | Login-Zustand (Schnellleiste, siehe skill-frames.md); die **letzten 88 Byte** sind die aktive Aufstellung | Block s.u. |
| `0xaf8a` | alle freigeschalteten Dekorationen | `n (u8)` dann `n` x (`id u32`, `slot u8`); Slot 0 = nicht in der aktiven Aufstellung aufgestellt |
| `0x96e2` | Aufstellung des gezeigten Tabs (bei Änderung und Tab-Wechsel) | Block s.u. + 1 Byte `00` |
| `0xb18a` | die aufgestellten Dekorationen als Liste | `n (u8)` dann `n` x (`id u32`, `slot u8`) |
| `0x90e2` | Tab-Wechsel | `00 00 07 <Tab> 00 00` |

**Aufstellungs-Block:** `01 | Preset (1..3) | Anzahl (17, bei leerem Preset 0) | 17 x (id u32 LE, slot u8)`, Slots 1..17 der Reihe nach, id 0 = leer.
Slots 1-12 Gemälde (im Uhrzeigersinn ab oben rechts, paarweise: 1/2 rechts oben, 3/4 rechts Mitte, 5/6 rechts unten, 7/8 links unten, 9/10 links Mitte, 11/12 links oben), 13-16 Statuen, 17 Koloss.

**Dekorations-ID** aus der Item-ID (`item_names.json`): Gemälde `534310xxx` -> `xxx`; Statue `534320xxx` -> `1000 + xxx - 1`; Koloss `534330xxx` -> `5000 + xxx - 1`. Die Effekte (Götterwerte) im Fenster sind die Summe der Dekorations-Effekte, der Client rechnet sie selbst.

Geprüft an Nunzio (Preset 1: 43, 9, 61 | 1008, 1021, 1004, 1023 | 5015; 11 freigeschaltet; Preset 2 und 3 leer) und Aahz (alle 17 belegt, 24 freigeschaltet), fünf Mitschnitte. Im Client: `Aion2FrameDecoder.NotePantheon`, Selbsttest `RunAion2PantheonScenario`.
