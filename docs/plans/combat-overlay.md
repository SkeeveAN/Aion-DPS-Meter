# Kampf-Overlay: Cast-Leiste, „Ziel des Ziels“, Heiler-Ziel

Branch: `feature/combat-overlay-cast-target` · Stand: umgesetzt (siehe „Umsetzungsstand“ am Ende)

## Ziel

Ein einziges Overlay-Fenster, das im Kampf drei Elemente zeigt. Jedes Element lässt sich einzeln im Rahmen verschieben und in der Größe ziehen. Ein globaler Greifer unten rechts skaliert alles gemeinsam.

1. **Boss-Cast-Leiste**: Der Skill eines Bosses ab dem Moment, in dem er zu wirken beginnt: Name, Symbol, ablaufender Balken mit Restzeit, wen er trifft („Du“ rot hervorgehoben), Flächen-Kennzeichnung, „LETHAL“, wenn der Treffer dich töten würde.
2. **„Ziel des Ziels“**: Hat das angeklickte Ziel selbst ein Ziel, wird das angezeigt.
   - Ist es ein Boss: Bossbild und Name.
   - Ist es ein Spieler: Klassensymbol und Name.
3. **Heiler-Ziel** (Option): HP-Balken und Debuffs des angeklickten Ziels.

## Bedienung (Vorgabe)

- Alle Elemente liegen in **einem** Overlay-Rahmen und sind einzeln **verschiebbar**.
- Jedes Element lässt sich **von links, rechts, oben und unten ziehen** (Größe ändern). Das gilt auch für die Elemente im Overlay.
- Der **globale Greifer unten rechts** ändert die Größe aller Elemente gemeinsam (Skalierung).
- Entsperrt: Rahmen sichtbar, alles bearbeitbar. Gesperrt: durchklickbar, nur Anzeige.

## Ist-Stand im Code (aus der Recherche)

- Alle Overlays erben von `MovableOverlay` ([PetMapWindows.cs:18-250](../../Client/Ui/PetMapWindows.cs)): transparent, Topmost, Alt+Tab versteckt, Sperre = Durchklicken ([NativeOverlay.cs](../../Client/Ui/NativeOverlay.cs)), Greifer unten rechts (`AttachGripResize`, ScaleTransform 0,5 bis 3), vier Kanten-Thumbs ([OverlayEdgeResize.cs](../../Client/Ui/OverlayEdgeResize.cs)).
- Speichern: `<X>Left/Top/Scale/Width/Height` plus `<X>Locked` in `MeterSettings`. Vorlage: `WorldBossOverlayWindow` samt Controller ([WorldBossOverlay.cs:61-215](../../Client/Ui/WorldBossOverlay.cs)). Der Controller liest die Einstellungen jede Sekunde und zeigt das Overlay nur, wenn das Spiel vorn ist oder das Overlay entsperrt ist.
- Hotkey-Aktionen: `HotkeyAction` ([GlobalHotkeys.cs:9-19](../../Client/Ui/GlobalHotkeys.cs)). Belegt sind H P D X U M T K O A B N E G, neu: **Ctrl+Alt+C**.
- Eigenes Ziel: `0x3538` → `Aion2EntityDirectory.LocalTarget` ([Aion2EntityDirectory.cs:652](../../Client/Aion2/Protocol/Aion2EntityDirectory.cs)); Boss → NPC-ID → Katalog/Bild ([Aion2BossCatalog.cs](../../Client/Aion2/Protocol/Aion2BossCatalog.cs), `Aion2Artwork.cs`), Spieler → Klasse/Name (`ClassOf`, `NameOf`).
- HP: Opcode `008d` ([Aion2FrameDecoder.cs:846](../../Client/Aion2/Protocol/Aion2FrameDecoder.cs)), nur Typ 0 (aktuelle HP). Max-HP wird aus dem höchsten gesehenen Wert geschätzt.
- **Nicht entschlüsselt:** Cast-Beginn und -Dauer, Fläche/Einzelziel, Buffs/Debuffs, das Ziel eines Monsters, Max-HP.

## Phase 0 – Protokoll in den Mitschnitten prüfen (Voraussetzung für alles andere)

Mitschnitte (siehe Abschnitt „Datenquellen“ unten): `C:\Users\ralf\Desktop\aion2\json` (rund 40 Dateien, 2,7 GB) und die Debug-Aufnahmen des Clients unter `%APPDATA%\Aion DPS Meter\Debug` (22 Dateien, 540 MB, 11./12.10.). Werkzeug: `Aion DPS.dll aion2-replay <jsonl>` und ein neuer Dump-Befehl.

Kandidaten aus der Analyse des DaevaMeter (Protokoll-Fakten, kein Code übernommen; ihre Schreibweise „04 38“ = unser `0438`). Jeder Wert ist **zu verifizieren**, bevor er im Decoder landet:

| Opcode | Vermutete Bedeutung |
|---|---|
| `0938` | **Windup** (Boss-Telegraph): Akteur-Varint, Skill u32, 1 Byte, „Aim“-Block (Flag, Ziel-Varint, Blickrichtung f32, optional x/y/z f32), danach **Dauer in ms als Varint** (50..120000) |
| `0238` | Cast: gleicher Aim-Block, ohne Dauer |
| `4838` / `0e38` | Fläche / bewegte Fläche (Position) |
| `0638` | Skill-Ende; `1b38`/`1e38` Aufladen Start/Ende |
| `2a38` / `2b38` / `2c38` | **Buff an / erneuert / Ende** (Ziel, Instanz-ID, Effekt-ID u32, Dauer, Caster, Position) |
| `05e0` | Stagger-Balken (`03 01` max+aktuell u32) und Break (`00 03`) |
| `008d` | HP; Typ 2 = Boss/Mob; die 9-Byte-Einträge (Typ + i64) enthalten eventuell Max-HP |
| `1b92` / `1c92` | Gruppen-HP/MP (max, aktuell) und Mitgliedsstatus |

Offene Fragen mit Rückfall:
- **Ziel eines Bosses:** Kandidat ist der Ziel-Varint im Aim-Block des letzten Casts/Windups; Rückfall: das Ziel des letzten Treffers.
- **Ziel eines Spielers:** Für fremde Spieler ist noch kein Frame bekannt. Findet die Suche keinen, bleibt das Element für Spieler-Ziele leer. Das steht dann so in der README.
- **Zielwahl per Kamera** (sanft) sendet vermutlich nichts; nur Tab (`3538`) ist belegt.

Ergebnis von Phase 0: eine kurze Datei `docs/protocol/combat-frames.md` mit belegten Opcodes, Feldlayouts und Beispielbytes, plus Selftests auf kleinen Auszügen.

## Phase 1 – Decoder und Modell

- `Aion2FrameDecoder`: neue Ereignisse `CastStart`, `CastEnd`, `BuffOn`, `BuffOff`, `TargetOf`, `HpMax`, über `ICombatSource` an die UI.
- Neue Klassen: `CastTracker` (laufende Casts, Ablauf, Abbruch bei Tod des Casters), `TargetTracker` (Ziel der Entitäten), `StatusTracker` (Buff-/Debuff-Spannen je Ziel; wird auch von `feature/rotation-buffs-stagger` genutzt, siehe dort).
- **Boss-Erkennung:** vom Welt-Modell als Boss markiert, oder im Boss-Katalog, oder Max-HP ≥ 500.000; keine Spieler, Beschwörungen, Puppen.
- **Fläche vs. Einzelziel lernen** (Prinzip): am Ende eines Casts zählen, wie viele verschiedene Spieler getroffen wurden. Treffen ≥ 2 oder nicht das genannte Ziel, ist es eine Fläche. Pro Skill speichern (`cast-skills.json`, max. 250 Einträge, Version im Dateikopf).
- **Landezeit:** Pro Skill die Zeit zwischen Cast-Ende und erstem Treffer merken; erst anzeigen, wenn ≥ 3 Werte innerhalb 200 ms übereinstimmen.
- **LETHAL:** erwarteter Schaden = dein höchster Treffer dieses Skills (sonst der höchste auf ein Gruppenmitglied) ≥ deine aktuelle HP und der Cast zielt auf dich oder ist eine Fläche.
- Anzeige-Priorität: auf dich gerichtet, tödlich, Fläche, Haupt-Boss, kleine Monster zuletzt; zeige die obersten N (1..3).
- Zonenwechsel und neue Verbindung leeren alles.

## Phase 2 – Overlay-Fenster mit einzeln beweglichen Elementen

- `CombatOverlayWindow : MovableOverlay`. Der Körper ist ein `Canvas`; jedes Element ist ein Kind mit eigenem Platz und eigener Größe.
- **Basisklasse `OverlayElement`** mit: Ziehen (Thumb im Element), vier Kanten-Thumbs, optionalem eigenem Eck-Greifer (Skalierung des Elements), Mindestgröße, Einrasten am Rahmen. Nur im entsperrten Zustand sichtbar.
- **Zwei Skalierungsebenen:** Elementgröße (Breite/Höhe) × Element-Skalierung × globale Skalierung (der bestehende Greifer unten rechts). Koordinaten sind unskaliert im Rahmen gespeichert; beim Ziehen durch die Gesamtskalierung teilen (`OverlayEdgeResize.MouseOnScreen` rechnet schon in DIPs).
- Das Fenster-`DragMove` des Rahmens darf nicht mit dem Element-Ziehen kollidieren: Element-Thumbs markieren das Ereignis als behandelt.
- **Einstellungen:** ein Fenstersatz `CombatOverlay{Left,Top,Scale,Width,Height,Locked}` und ein `Dictionary<string, ElementLayout>` für die Elemente (`Left, Top, Width, Height, Scale, Visible`). Speichern nur bei `DragCompleted`, nicht bei jeder Mausbewegung (jedes `Save()` schreibt die ganze Datei).
- Elemente:
  - **BossCast:** Symbol, Name, Balken mit Restzeit, Ziel („Du“ rot), Tags `AREA`/`LETHAL`, rote Kante, optional leiser Ton, Option „Ping einrechnen“ (Balken endet um den Ping früher).
  - **TargetOfTarget:** Boss: Bossbild + Name; Spieler: Klassensymbol + Name; erscheint nur, wenn das angeklickte Ziel ein Ziel hat.
  - **HealerTarget** (einstellbare Teile): Name, HP-Balken (aktuell/max, Prozent), Debuff-Symbole mit Restdauer. Ein Schalter „Heiler-Modus“ schaltet HP-Balken und Debuffs an.
- Zeigen: nur wenn das Spiel vorn ist oder das Overlay entsperrt ist (wie `WorldBossOverlay`); Option „nur im Kampf“.
- Hotkey **Ctrl+Alt+C** und Aufnahme in „alle Overlays an/aus“ (Ctrl+Alt+A).

## Phase 3 – Einstellungen, Sprachen, Doku

- Neue Einstellungsseite „Kampf-Overlay“: Schalter je Element, Cast-Filter (alles / Flächen und auf mich / nur auf mich), Anzahl gleichzeitiger Casts, Ping-Option, Ton, „Elemente zurücksetzen“, „Overlay verschieben“.
- Texte in `Client/assets/i18n/ui_strings.json` für alle zehn Sprachen; Skill-, Boss- und Klassennamen aus dem Client.
- README-Abschnitt, Features-Seite (en.js + Übersetzungen), neues Bild per `render-settings`/neuem `render-combatoverlay`, Changelog.

## Reihenfolge der Arbeit

1. Phase 0 (Mitschnitte durchsuchen, Befunde festhalten).
2. Decoder + Tracker + Selftests (ohne UI).
3. Overlay-Fenster mit Element-Framework und **Platzhalter-Elementen** (Layout testen, auch headless gerendert).
4. Elemente mit echten Daten füllen: erst BossCast, dann TargetOfTarget, dann HealerTarget.
5. Einstellungen, Sprachen, Doku.

## Abnahme

- Mit einem Mitschnitt (`aion2-replay`) erscheinen Casts mit richtiger Dauer und Ziel; Fläche/Einzelziel stimmt nach ein paar Kämpfen.
- Jedes Element lässt sich verschieben, an allen vier Kanten ziehen und mit dem globalen Greifer gemeinsam skalieren; Layout bleibt nach Neustart erhalten.
- Gesperrt ist das Overlay durchklickbar, entsperrt nicht.
- Ohne Treffer in Phase 0 für ein Teilfeature (z. B. Spieler-Ziel) wird dieses Element ausgeblendet statt zu raten.

## Risiken

- Cast-Frames könnten nur für bestimmte Bosse gesendet werden → Phase 0 zeigt es früh.
- `SizeToContent` plus LayoutTransform: Ziehkoordinaten müssen durch die Skalierung geteilt werden.
- DPI-Ausrichtung in `app.manifest` ist nicht bestätigt (gemischte Monitore prüfen).
- Der Eintrag „Heiler-HP“ braucht Max-HP: Kandidat sind die 9-Byte-Einträge in `008d`; sonst Schätzung wie bisher und als „~“ kennzeichnen.

## Datenquellen (für alle Phasen)

- **Desktop-Mitschnitte:** `C:\Users\ralf\Desktop\aion2\json\aion2-*.jsonl`, 5.–11.10., rund 40 Dateien, 2,7 GB; dazu `aion2-alleports-*` und `aion2-lobby-*`.
- **Debug-Aufnahmen des Clients:** `%APPDATA%\Aion DPS Meter\Debug\aion2-<Datum>-<Charakter>.jsonl` (Einstellungen → Debug-Aufnahme, [DebugRecording.cs](../../Client/Aion2/Capture)); 22 Dateien, 540 MB, 11./12.10., überwiegend Charakter „Aahz“. Gleiches Format wie die Desktop-Mitschnitte: ein JSON pro TCP-Segment `{"T","From","To","Seq","Server","Data":base64}`, Server-Port 13328.
- **Gespeicherte Kämpfe:** `%APPDATA%\Aion DPS Meter\fights.db` (`events_gz` = gzip-JSON der Schadensereignisse).
- Alle Dateien nur lesen, nie verändern; große Dateien streamen, nicht komplett laden.

## Umsetzungsstand (13.10.2026)

**Fertig und geprüft (Selftests mit echten Bytes, Aufnahmen, gezeichnete Bilder):**

- Phase 0: Befunde in [docs/protocol/combat-frames.md](../protocol/combat-frames.md). Windup `0938` (Dauer, Ziel, Position), Cast `0238` (Ziel), Effekte `2a38`/`2b38`/`2c38`, Max-HP (`008d` Art 7) sind belegt.
- Phase 1: `Aion2CombatState` (Casts, gelernte Skills in `cast-skills.json`, Ziel-Gedächtnis, Effekte), Decoder-Erweiterungen, `HitPoints.MaxOf`.
- Phase 2: `CombatOverlayWindow` (erbt von `MovableOverlay`) mit `CombatStage` und `CombatElementHost` (Verschieben, vier Kanten, eigener Eck-Greifer; globaler Greifer des Rahmens skaliert alles); drei Elemente (`BossCastView`, `TargetOfTargetView`, `HealerTargetView`); Controller mit „nur wenn das Spiel vorn ist“.
- Phase 3: Einstellungsseite „Kampf-Overlay“ mit den zwei Schaltern (Overlay an/aus, Overlay gesperrt an/aus) plus Elementen und Optionen; Hotkey **Ctrl+Alt+C**; „Alle Overlays an/aus“ (Ctrl+Alt+A) schaltet das Kampf-Overlay mit; Texte in zehn Sprachen; README (alle zehn); Bild; Boss-Bilder für den Client (`Tools/aion2-icons/build_boss_thumbs.py`, 34 Bosse mit Karte, sonst Emblem).
- Zeichenbefehl `render-combatoverlay <png> [live|edit]` und `AIONDPS_LANG` für Bilder in jeder Sprache.

**Bewusst noch offen:**

- Ziehen/Skalieren der Elemente mit der Maus ist **nicht automatisch getestet** (braucht echte Mausereignisse); die Rechnung steht in `CombatElementHost`, bitte einmal von Hand im entsperrten Overlay ausprobieren.
- Optionen „Ping einrechnen“ und „Ton“ der Cast-Leiste (Phase 2 des Plans) sind nicht eingebaut; „Wann es landet“ (weißer Strich/roter Teil) ist da und erscheint erst nach drei übereinstimmenden Beobachtungen je Skill.
- Features-Seite der Website (Abschnitt + Übersetzungen) und Changelog-Eintrag kommen beim Merge nach `main`.
- Ziel eines fremden Spielers und Auswahl per Kamera: kein Frame bekannt (siehe Protokoll-Datei).
