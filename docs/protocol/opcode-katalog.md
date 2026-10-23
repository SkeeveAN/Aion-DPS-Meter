# Opcode-Katalog

Einstieg in alle bisher untersuchten Frames. Zählung der 818 Opcodes aus den 185 Login-Dumps: **158 belegt/als Familie im Client**, 7 nur erwähnt (nicht geprüft), 653 unbekannt. Opcode = Hex der ersten zwei Frame-Bytes. **Stand 23.10.2026**; ausführliche Aufbauten in den verlinkten Dateien. Familien, die der Client schon dekodiert, stehen in `Client/assets/aion2/protocol/opcodes.json` (unten aufgelistet).

Dokumente: [login-und-fenster-frames.md](login-und-fenster-frames.md), [skill-frames.md](skill-frames.md), [welt-frames.md](welt-frames.md), [sitzungs-frames.md](sitzungs-frames.md), [inventar-quest-frames.md](inventar-quest-frames.md), [bewegungs-frames.md](bewegungs-frames.md), [combat-frames.md](combat-frames.md), [cooldown-frames.md](cooldown-frames.md), [hit-kinds.md](hit-kinds.md), [position-frames.md](position-frames.md), [ticket-frames.md](ticket-frames.md), [client-umsetzung-2026-10-23.md](client-umsetzung-2026-10-23.md) (im Client umgesetzte Frames und Berichtigungen: Legionsliste, eigener Rang, Ziel-Frames, Quittung, Ketten, Rezepte, Wochenpunkte, Positionsmessung).

## Dekodierte Frames

| Opcode | Bedeutung | Stand | Dokument |
|---|---|---|---|
| `0x0036` | Serveruhr (UTC ms, alle 50 ms) | bekannt | ticket-frames / position-frames |
| `0x2136` | Zonenframe (Zone, Position, Art) | bekannt | position-frames |
| `0x218d` | Kampf betreten/verlassen | bekannt | combat-frames |
| `0x4b36` | Entitäts-Stat (z. B. Boss-Wut 0xCD) | bekannt | combat-frames |
| `0x0238` | Skill-Cast | bekannt | combat-frames |
| `0x0338` | Treffer-Vorankündigung | bekannt | combat-frames |
| `0x0438` | Schaden | bekannt | combat-frames |
| `0x0938` | Windup | bekannt | combat-frames |
| `0x2a38` | Buff an | bekannt | combat-frames |
| `0x2b38` | Buff erneuert | bekannt | combat-frames |
| `0x2c38` | Buff Ende | bekannt | combat-frames |
| `0x4738` | Abklingzeiten | bekannt | cooldown-frames |
| `0x0b61` | Ticket-/Zählerliste (Login) | bekannt | ticket-frames, login-und-fenster-frames |
| `0x0c61` | Ticket-Änderung | bekannt | ticket-frames |
| `0x24e3` | Daeva-Pass-XP | bekannt | ticket-frames |
| `0x3be3` | Quai-Premium/Ticket-Zeiten | bekannt | ticket-frames |
| `0x2636` | Charakterliste (Account-ID, alle Charaktere) | bekannt | - |
| `0x1c8d` | Pflichtaufgaben/Missionsliste | bekannt | ticket-frames |
| `0x8e56` | Kaufzähler beim Öffnen eines Shops | bekannt, 23.10. präzisiert | login-und-fenster-frames |
| `0x9156` | Schwarzwolke-Shop | bekannt | - |
| `0x0191` | Feldbossliste einer Karte | bekannt | - |
| `0x7e8d` | Gelernte Herstellungsrezepte | neu 23.10. | login-und-fenster-frames |
| `0x8c8d` | Gelernte Wandlungsrezepte | neu 23.10. | login-und-fenster-frames |
| `0x8f8d` | Konvertierungszähler | neu 23.10. | login-und-fenster-frames |
| `0x3292` | Freigeschaltete Teleporter | neu 23.10. | login-und-fenster-frames |
| `0x098d` | Questlog | neu 23.10. | login-und-fenster-frames |
| `0x8356` | Erfolge, Saisonmissionen, Wochenpunkte | neu 23.10. | login-und-fenster-frames |
| `0x56e2` | Versorgungsgesuche (4 Tabs) | neu 23.10. | login-und-fenster-frames |
| `0x3ee2` | Versorgungsgesuch: Fortschritt gesetzt (Abgabe) | neu 23.10. | login-und-fenster-frames |
| `0x3fe2` | Versorgungsgesuch: Belohnung (Abgabe) | neu 23.10. | login-und-fenster-frames |
| `0x1257` | Garantierte Belohnung je Dungeon | neu 23.10. | login-und-fenster-frames |
| `0x37e2` | Albtraum-Rekorde je Boss | neu 23.10. | login-und-fenster-frames |
| `0x3193` | Transzendenz-Rekorde | neu 23.10. | login-und-fenster-frames |
| `0x3ee3` | Aszensusritus-Rekord | neu 23.10. | login-und-fenster-frames |
| `0x2ae3` | Arena-Wertung und Rekord je Modus | neu 23.10. | login-und-fenster-frames |
| `0x578d` | Rangprofil je Inhaltstyp | neu 23.10. | login-und-fenster-frames |
| `0x558d` | Ranglisten (bis 500 Einträge) | neu 23.10. | login-und-fenster-frames |
| `0x2ce3` | Arena-Verlauf (Klartext-JSON) | neu 23.10. | login-und-fenster-frames |
| `0x058a` | Legionsliste (Stufe 45) | neu 23.10. | login-und-fenster-frames |
| `0x74e2` | Freundesliste | neu 23.10. | login-und-fenster-frames |
| `0x82e2` | Freund/Legionsmitglied online/offline | neu 23.10. | login-und-fenster-frames |
| `0x338a` | Legion eines sichtbaren Spielers | neu 23.10., Rang-Byte offen | login-und-fenster-frames |
| `0x5b8d` | Besessene Skins | neu 23.10. | login-und-fenster-frames |
| `0x7256` | Besessene Flügel | neu 23.10. | login-und-fenster-frames |
| `0x3356` | Götterstein-Kombinationen | neu 23.10. | login-und-fenster-frames |
| `0x378d` | Essenzextraktion (Sammelfähigkeiten) | neu 23.10. | login-und-fenster-frames |
| `0x0197` | Liste offener Gruppen/Räume | neu 23.10. | login-und-fenster-frames |
| `0x0195` | Freigeschaltete Spielinhalte des Servers | neu 23.10. | login-und-fenster-frames |
| `0x1636` | Spielserver-Adresse | neu 23.10. | login-und-fenster-frames |
| `0x1536` | Login-Token | neu 23.10. | login-und-fenster-frames |
| `0x078c` | Kaufzähler der Münz-Shops | neu 23.10. | skill-frames |
| `0x1838` | Kettenskill freigeschaltet | neu 23.10. | skill-frames |
| `0x1938` | Kettenfenster geschlossen | neu 23.10. | skill-frames |
| `0x0138` | Skill-Quittung (eigener Spieler) | neu 23.10. | skill-frames |
| `0x3238` | Ziel eines fremden Akteurs | neu 23.10. | skill-frames |
| `0x3138` | Ziel des eigenen Spielers | neu 23.10. | skill-frames |
| `0x1338` | Skill-Liste Einzelsatz | neu 23.10. | skill-frames |
| `0x0f38` | Skill-Liste Einzelsatz (ohne zweite ID) | neu 23.10. | skill-frames |
| `0x1038` | Skill-Variante entfernt | neu 23.10. | skill-frames |
| `0x2238` | Skill-Ladungen | neu 23.10. | skill-frames |
| `0x4a36` | Stat-Update des eigenen Charakters | neu 23.10. | skill-frames |
| `0x1c56` | Verstärkung sichtbarer Spieler (Datensatz) | neu 23.10. | skill-frames |
| `0x2656` | Verstärkung: Art/alt/neu | neu 23.10. | skill-frames |
| `0x0056` | Schnellleiste | neu 23.10. | skill-frames |
| `0x0556` | Schnellleiste (Preset-Wechsel) | neu 23.10. | skill-frames |
| `0x8ce2` | Schnellleiste | neu 23.10. | skill-frames |
| `0x0e38` | Bewegte Fläche/Geschoss eines Monsterskills | neu 23.10. | skill-frames |
| `0x0795` | NPC-Zustandsblock | neu 23.10., Zweck Hypothese | skill-frames |
| `0x0895` | NPC-Zustandsblock | neu 23.10., Zweck Hypothese | skill-frames |
| `0x1a37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x1b37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x1c37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x1d37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x1e37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x1f37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2037` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2437` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2537` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2837` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2937` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2a37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2d37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2f37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x3037` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x3237` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2b37` | Bewegung/Position anderer Entitäten | neu 23.10. | bewegungs-frames |
| `0x2e37` | Skill-Cast mit Position (fremde Spieler) | neu 23.10. | bewegungs-frames |
| `0x2137` | Marker: Bewegung beginnt | neu 23.10., Name Hypothese | bewegungs-frames |
| `0x2237` | Marker: Bewegung endet | neu 23.10., Name Hypothese | bewegungs-frames |
| `0x3c37` | Positionskorrektur/Teleport | bekannt | position-frames |
| `0x4736` | Spieler entfernen (mit Grund) | neu 23.10. | bewegungs-frames |
| `0x4236` | NPC entfernen | neu 23.10. | bewegungs-frames |
| `0x3536` | Umgebungsobjekt entfernen | neu 23.10. | bewegungs-frames |
| `0x3436` | Umgebungsobjekt (statisch) | bekannt | bewegungs-frames |
| `0x4136` | NPC-Spawn | bekannt | bewegungs-frames |
| `0x4536` | Spieler-Spawn | bekannt | bewegungs-frames |
| `0x3836` | Umgebungsobjekt wird benutzt | neu, Art Hypothese | bewegungs-frames |
| `0x3a36` | Umgebungsobjekt wird benutzt | neu, Art Hypothese | bewegungs-frames |
| `0x3b36` | Umgebungsobjekt wird benutzt | neu, Art Hypothese | bewegungs-frames |
| `0x4e36` | Ausrichtung (zwei Winkel) | neu 23.10. | bewegungs-frames |
| `0x0638` | Skill-Ende | neu 23.10. | welt-frames |
| `0x3b38` | Begleiter der Skill-Quittung 0138 (nicht Zielverlust) | neu 23.10. | welt-frames |
| `0x3d38` | Begleiter der Skill-Quittung 0138 (nicht Zielverlust) | neu 23.10. | welt-frames |
| `0x3438` | Zielwahl bestätigt | neu 23.10. | welt-frames |
| `0x3538` | Ziel des gewählten Ziels | neu 23.10. | welt-frames |
| `0x05e0` | Stagger-Balken | bekannt | welt-frames |
| `0x4637` | Entität-Höhe (z) | neu 23.10. | welt-frames |
| `0x0336` | Heartbeat alle 10 s mit Serveruhr | neu 23.10. | welt-frames |
| `0x4838` | NPC-Bodenfläche | neu 23.10. | welt-frames |
| `0x1b38` | Aufladen beginnt | neu 23.10. | welt-frames |
| `0x1e38` | Aufladen losgelassen | neu 23.10. | welt-frames |
| `0x2036` | Client-Einstellungen (zlib-JSON) | neu 23.10. | sitzungs-frames |
| `0x5ae2` | Session-Token | neu 23.10., Bedeutung Hypothese | sitzungs-frames |
| `0x1136` | Verbindungsaufbau | neu 23.10., Bedeutung Hypothese | sitzungs-frames |
| `0x43e3` | Anmeldedatensatz / Eigen-ID | neu 23.10. | sitzungs-frames |
| `0x2336` | Eigene Position nach dem Laden | neu 23.10. | sitzungs-frames |
| `0x0140` | Zonenzustand | neu 23.10. | sitzungs-frames |
| `0x0240` | Zonen-Zusatzzustand | neu 23.10. | sitzungs-frames |
| `0x0143` | Luftraumschlacht | neu 23.10. | sitzungs-frames |
| `0x0243` | Luftraumschlacht | neu 23.10. | sitzungs-frames |
| `0x0343` | Luftraumschlacht | neu 23.10. | sitzungs-frames |
| `0x62e2` | siehe Doku | neu 23.10., Bedeutung Hypothese | sitzungs-frames |
| `0x61e2` | siehe Doku | neu 23.10., Bedeutung Hypothese | sitzungs-frames |
| `0xa856` | siehe Doku | neu 23.10. | sitzungs-frames |
| `0x538d` | Saison-Tabelle | neu 23.10., Bedeutung Hypothese | sitzungs-frames |
| `0x2292` | Spieler-Datensatz mit GUID | neu 23.10., Rest Hypothese | sitzungs-frames |
| `0x1e56` | Beute erscheint | neu 23.10. | inventar-quest-frames |
| `0x1f56` | Beute verschwindet (Schlussbyte 1 = aufgenommen) | neu 23.10. | inventar-quest-frames |
| `0x1356` | Gegenstand benutzt | neu 23.10. | inventar-quest-frames |
| `0x1556` | Gegenstand benutzt (Stapel, Anzahl) | neu 23.10. | inventar-quest-frames |
| `0x8f56` | Kaufzähler-Update | bekannt | inventar-quest-frames |
| `0x8c56` | Kaufmenge dieses Kaufs | neu 23.10. | inventar-quest-frames |
| `0x6f8d` | Herstellungsergebnis (Rezept-ID) | neu 23.10., Eintragsliste unbekannt | inventar-quest-frames |
| `0x728d` | Spieler stellt her (Berufs-ID) | neu 23.10. | inventar-quest-frames |
| `0x7c8d` | Berufs-XP | bekannt | inventar-quest-frames |
| `0x7d8d` | Berufsstufe | bekannt | inventar-quest-frames |
| `0x0f8d` | Quest abgegeben | neu 23.10., Anhang unbekannt | inventar-quest-frames |
| `0x118d` | Quest angenommen | bekannt | inventar-quest-frames |
| `0x208d` | Quest-Angebotsnummer | bekannt, Deutung Hypothese | inventar-quest-frames |
| `0x108d` | Quest-Fortschritt (Instanznummer, Zielzähler) | neu 23.10. | inventar-quest-frames |
| `0x928d` | Spieler-eid + Zustand | neu 23.10., Hypothese | inventar-quest-frames |
| `0x8456` | Erfolge-Zähler live (auch Saisonmissionen) | neu 23.10. | inventar-quest-frames |

## Im Client als Familie (opcodes.json), hier nicht noch einmal beschrieben

| Opcode | Familie |
|---|---|
| `0x0039` | stigmas |
| `0x0051` | skills |
| `0x008d` | hpUpdate |
| `0x0090` | species |
| `0x0297` | roster |
| `0x0360` | playerList |
| `0x048d` | appearance |
| `0x0538` | dot |
| `0x0739` | specslots |
| `0x0990` | petAdded |
| `0x0d90` | petProgress |
| `0x1156` | equipment |
| `0x1490` | petLevel |
| `0x1b56` | inventoryChange |
| `0x26e2` | daevanion |
| `0x3336` | character |
| `0x4936` | attributes |
| `0x5036` | inspect |
| `0x7956` | titles |
| `0x8156` | titleChange |
| `0x92e2` | presetswitch |

## Erwähnt, aber in dieser Auswertung nicht geprüft (7 Opcodes)

Diese Opcodes stehen im Client-Code, in älteren Dokumenten oder in Notizen. Eine Erwähnung heißt nicht, dass der Aufbau hier belegt ist; einige (z. B. `0x0001`, `0x0010`) sind nur Zahlen im Text.

| Opcode | Frames | Beleg |
|---|---|---|
| `0x418d` | 131 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x0001` | 13 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x918d` | 11 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x0010` | 6 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x0004` | 4 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x0002` | 2 | nur in Notizen erwähnt, nicht neu geprüft |
| `0x3600` | 1 | nur in Notizen erwähnt, nicht neu geprüft |

## Noch unbekannt (653 Opcodes in den 185 Login-Dumps)

Häufigkeit in den Dumps, Anzahl der Dumps, größte Frame-Länge. Viele sind kleine Zustandsmeldungen der Welt; die ersten sind die lohnenden Kandidaten.

| Opcode | Frames | Dumps | größte Länge |
|---|---|---|---|
| `0x748d` | 2750 | 137 | 10 |
| `0x6d8d` | 2662 | 170 | 5 |
| `0x058d` | 1026 | 156 | 5 |
| `0x5636` | 927 | 184 | 18 |
| `0xa9ff` | 520 | 176 | 18 |
| `0x15e2` | 482 | 169 | 14 |
| `0x4938` | 477 | 1 | 39 |
| `0x0390` | 360 | 129 | 11 |
| `0xa7ff` | 356 | 58 | 4 |
| `0x1c92` | 338 | 14 | 76 |
| `0x1d56` | 315 | 170 | 10 |
| `0x038a` | 306 | 171 | 114 |
| `0x4636` | 305 | 171 | 9 |
| `0x458a` | 304 | 171 | 16 |
| `0x1b92` | 285 | 10 | 35 |
| `0x4437` | 282 | 31 | 6 |
| `0x1a92` | 273 | 8 | 8 |
| `0x0160` | 271 | 49 | 31 |
| `0x2ee2` | 264 | 169 | 18 |
| `0x0f57` | 264 | 173 | 32 |
| `0x63e2` | 262 | 184 | 7 |
| `0x588d` | 261 | 184 | 3 |
| `0x448d` | 261 | 184 | 11 |
| `0x4856` | 261 | 184 | 12 |
| `0x4b56` | 261 | 184 | 23 |
| `0x8256` | 261 | 184 | 6 |
| `0x0c91` | 253 | 28 | 30 |
| `0x42e3` | 245 | 169 | 741 |
| `0x00e2` | 243 | 170 | 51 |
| `0x0060` | 243 | 169 | 264 |
| `0x51e2` | 242 | 169 | 3 |
| `0x0a40` | 242 | 169 | 11 |
| `0x23e2` | 242 | 169 | 14 |
| `0x5736` | 242 | 169 | 11 |
| `0x49e3` | 241 | 169 | 13 |
| `0x2d57` | 240 | 169 | 3 |
| `0xaf8a` | 239 | 169 | 128 |
| `0xac56` | 239 | 169 | 56 |
| `0x3093` | 239 | 169 | 120 |
| `0x3de3` | 239 | 169 | 6 |
| `0x24e2` | 239 | 169 | 6 |
| `0x57e2` | 239 | 169 | 56 |
| `0xacff` | 239 | 169 | 26 |
| `0x0361` | 239 | 169 | 12 |
| `0x0be3` | 239 | 169 | 27 |
| `0xb656` | 239 | 169 | 3 |
| `0xa5ff` | 239 | 169 | 18 |
| `0x5ce2` | 239 | 169 | 5 |
| `0x838d` | 239 | 169 | 3 |
| `0x398a` | 238 | 166 | 13 |
| `0x4ee3` | 238 | 168 | 4 |
| `0x0690` | 235 | 111 | 6 |
| `0x2337` | 216 | 74 | 33 |
| `0x4d8d` | 215 | 153 | 12 |
| `0x038d` | 186 | 81 | 8 |
| `0x4137` | 186 | 81 | 6 |
| `0x0e92` | 172 | 7 | 9 |
| `0x348d` | 169 | 41 | 9 |
| `0x3437` | 161 | 9 | 34 |
| `0x2d92` | 160 | 22 | 35 |
| `0x4237` | 158 | 75 | 4 |
| `0x3f56` | 148 | 80 | 9 |
| `0x73e2` | 124 | 8 | 11 |
| `0x4356` | 123 | 67 | 362 |
| `0x2156` | 117 | 12 | 13 |
| `0x938d` | 116 | 4 | 9 |
| `0x3736` | 110 | 37 | 20 |
| `0x3837` | 99 | 1 | 23 |
| `0x2d38` | 97 | 19 | 10 |
| `0x3936` | 96 | 56 | 13 |
| `0x0c38` | 87 | 40 | 14 |
| `0x2f92` | 77 | 47 | 6 |
| `0x0061` | 77 | 54 | 23 |
| `0x5536` | 72 | 16 | 9 |
| `0x0551` | 71 | 18 | 12 |
| `0x028d` | 71 | 30 | 8 |
| `0x8a56` | 69 | 61 | 136 |
| `0x1190` | 69 | 18 | 19 |
| `0xa6ff` | 69 | 7 | 18 |
| `0x4d36` | 58 | 52 | 3 |
| `0x528d` | 50 | 12 | 5 |
| `0x0861` | 47 | 38 | 3 |
| `0x0560` | 46 | 7 | 44 |
| `0x1692` | 45 | 9 | 14 |
| `0x1638` | 45 | 15 | 27 |
| `0x4556` | 45 | 29 | 330 |
| `0x4038` | 45 | 11 | 8 |
| `0x1c38` | 43 | 24 | 35 |
| `0x43e2` | 43 | 19 | 8 |
| `0x158d` | 42 | 18 | 60 |
| `0x0961` | 41 | 33 | 6 |
| `0x41e2` | 39 | 17 | 16 |
| `0x23e3` | 38 | 29 | 14 |
| `0x7656` | 36 | 21 | 9 |
| `0x1438` | 30 | 10 | 31 |
| `0x1538` | 30 | 10 | 19 |
| `0x2c8d` | 30 | 14 | 17 |
| `0x0257` | 30 | 7 | 17 |
| `0x0000` | 28 | 11 | 349269 |
| `0x4b8d` | 27 | 23 | 5 |
| `0x0540` | 27 | 24 | 4 |
| `0x2637` | 27 | 18 | 6 |
| `0x0161` | 26 | 26 | 12 |
| `0x3e36` | 26 | 16 | 7 |
| `0x1656` | 24 | 9 | 14 |
| `0x0660` | 24 | 8 | 13 |
| `0x8656` | 24 | 11 | 9 |
| `0x2356` | 22 | 10 | 17 |
| `0x0092` | 21 | 10 | 808 |
| `0x4156` | 21 | 20 | 75 |
| `0x39e2` | 20 | 3 | 11 |
| `0x4ee2` | 20 | 13 | 14 |
| `0x0461` | 20 | 1 | 8 |
| `0x1d97` | 19 | 19 | 4 |
| `0x3792` | 19 | 19 | 8 |
| `0x3ae2` | 19 | 3 | 7 |
| `0x96e2` | 19 | 11 | 91 |
| `0xb18a` | 19 | 11 | 93 |
| `0x07e3` | 18 | 13 | 146 |
| `0x28e2` | 18 | 7 | 8 |
| `0x078d` | 17 | 9 | 19 |
| `0x4c37` | 17 | 14 | 4 |
| `0x2092` | 16 | 3 | 4 |
| `0x5c8d` | 16 | 16 | 158 |
| `0x4337` | 16 | 8 | 76 |
| `0x1756` | 16 | 13 | 16 |
| `0x7d56` | 16 | 13 | 6 |
| `0x338d` | 15 | 7 | 27 |
| `0x1956` | 15 | 13 | 16 |
| `0x0a57` | 15 | 8 | 725 |
| `0x3737` | 15 | 1 | 33 |
| `0x1f93` | 14 | 14 | 11 |
| `0x1b97` | 14 | 14 | 6 |
| `0x318d` | 14 | 6 | 236 |
| `0x3e8d` | 14 | 7 | 14 |
| `0x6a8d` | 14 | 2 | 141 |
| `0x2e92` | 13 | 13 | 97 |
| `0x2c37` | 13 | 9 | 23 |
| `0x05e3` | 13 | 8 | 77 |
| `0x2a56` | 13 | 2 | 53 |
| `0x0b97` | 12 | 7 | 54 |
| `0x4d56` | 12 | 4 | 9 |
| `0x0040` | 12 | 3 | 7 |
| `0x1192` | 11 | 9 | 10 |
| `0x0290` | 11 | 10 | 13 |
| `0x8de2` | 11 | 8 | 186 |
| `0x48e2` | 11 | 9 | 13 |
| `0x7356` | 11 | 9 | 11 |
| `0x0860` | 11 | 11 | 7 |
| `0x0013` | 11 | 7 | 271709 |

(weitere 503 Opcodes mit weniger Frames nicht aufgeführt; die Zählung lässt sich mit den Hilfsskripten unter `Tools/recording-py` aus den Aufnahmen neu erstellen.)
