# Menü-Erkennung über den Mauszeiger

Branch: `feature/menu-detect-cursor` · Stand: umgesetzt (siehe „Umsetzungsstand“ am Ende)

## Ziel

Optionaler Komfort: Der Meter und seine Overlays treten zur Seite, solange ein Spielmenü offen ist (Karte, Inventar, Legion, Shop, Esc …) und kommen zurück, wenn es geschlossen wird.

Das funktioniert nur mit der **Steuerung, bei der das Spiel den Mauszeiger beim Spielen ausblendet** und beim Öffnen eines Menüs zeigt. In der klassischen Steuerung (im Spiel „AION 1“-Modus genannt) ist der Zeiger immer sichtbar, dort lassen sich Menüs so nicht erkennen. Die Option ist deshalb **standardmäßig aus** und trägt in den Einstellungen einen Hinweis darauf.

## Prinzip

- Der Zeiger ist sichtbar, wenn `GetCursorInfo` das Flag `CURSOR_SHOWING` meldet und der Handle nicht 0 ist. Schlägt der Aufruf fehl, gilt „nicht sichtbar“ (der Meter bleibt oben).
- Halten des Zeigertasten-Hotkeys im Spiel (Standard Alt) zeigt den Zeiger ebenfalls, ohne dass ein Menü offen ist, damit man den Meter anklicken kann. Dann bleibt der Meter oben. Ob eine Taste gehalten wird, fragt `GetAsyncKeyState` ab (Tasten 1..254 ohne Caps/Num/Scroll-Lock), und zwar nur, solange der Zeiger sichtbar ist.
- **Zustandsautomat:**
  - Meldungen älter als 3 s werden ignoriert.
  - Zeiger sichtbar und eine Taste gehalten < 20 s: „Spähen“, Meter bleibt.
  - Eine Taste länger als 20 s gehalten: als hängend ignorieren.
  - Zeiger sichtbar und 250 ms nichts gehalten: „Menü“, Meter weg.
  - Nach dem Verschwinden des Zeigers bleibt „Menü“ noch 300 ms (Flackern vermeiden).
  - Eigene Fenster des Meters (Einstellungen, entsperrte Overlays, Menüs) unterdrücken das Verstecken.
- **Steuerungsart lernen:** Läuft das Spiel im Vordergrund und es entsteht Schaden bei sichtbarem Zeiger ≈ 12 s lang, ist es die klassische Steuerung → Funktion abschalten und das in den Einstellungen anzeigen. Bleibt der Zeiger bei Schaden etwa 15 s lang verborgen, ist es die Steuerung mit ausgeblendetem Zeiger → Funktion zulässig. Die Zähler klingen ab.

## Ist-Stand

- Es gibt keinen Zeigercode im Client. Fokus des Spiels: `GameWindow` ([PetFarmWindow.cs:369-480](../../Client/Ui/PetFarmWindow.cs)), `ForegroundIsGameOrSelf()` und `ForegroundClientArea()`. Das Hauptfenster prüft das alle 250 ms (`_gateTimer`, [MainWindow.xaml.cs:131, 4585-4604](../../Client/Ui/MainWindow.xaml.cs)); die Controller der Overlays fragen dieselbe Funktion jede Sekunde.
- Wir laufen im selben Prozess wie die Oberfläche, es braucht keinen separaten Helfer.

## Umsetzung

1. `GameWindow.IsCursorVisible()` per P/Invoke `GetCursorInfo`; `GameWindow.AnyKeyDown()` per `GetAsyncKeyState`.
2. **Reine Klasse `MenuDetector`** (Zustandsautomat, Zeit wird hineingereicht, daher testbar) mit Selftest-Szenarien: Menü auf/zu, Alt-Spähen, hängende Taste, veraltete Meldung, Flackern.
3. Abfrage nur, solange das Spiel vorn ist; Takt 50 ms über einen eigenen `DispatcherTimer` (Kosten gering, da `GetAsyncKeyState` nur bei sichtbarem Zeiger läuft).
4. In das Tor des Hauptfensters (`_gameGateOpen`, `ShowOverlayPanels`) und in die Controller der Overlays einhängen: „Spiel vorn **und** Menü offen“ = verstecken.
5. Einstellungen: ein Schalter „Während Spielmenüs ausblenden“ mit Hinweistext (nur mit der Steuerung, die den Zeiger ausblendet), Statusanzeige „Steuerung erkannt: …“, Hotkey „Anzeigen während eines Menüs“.
6. **Läuft das Spiel als Administrator**, sieht ein normaler Prozess die gehaltenen Tasten nicht (Windows-Schutz zwischen Prozessen unterschiedlicher Rechte). Dann Hinweis und Angebot „Immer als Administrator starten“; ohne das bleibt nur „Zeiger sichtbar“ ohne Spähen. **Zu prüfen:** ob `GetCursorInfo` selbst davon betroffen ist.
7. Texte in zehn Sprachen, README (Funktionsliste, Hinweis zur Steuerung), Changelog.

## Abnahme

- In der Steuerung mit verstecktem Zeiger verschwindet der Meter beim Öffnen von Karte/Inventar und kommt beim Schließen wieder; Halten von Alt lässt ihn stehen.
- In der klassischen Steuerung bleibt er oben und die Einstellung zeigt den Hinweis.
- Fenster des Meters selbst werden nie versteckt, solange man damit arbeitet.

## Risiken

- Betrieb als Administrator (siehe oben).
- Flackern bei kurzen Zeiger-Einblendungen → Entprellung.
- Die Steuerungsart kann sich zur Laufzeit ändern → Lernzähler klingen ab.

## Umsetzungsstand (13.10.2026)

**Fertig und geprüft (Selftests des Zustandsautomaten):**

- `MenuDetector` (rein): 250 ms Zeiger sichtbar ohne gehaltene Taste = Menü; Alt halten = Spähen, bleibt oben; eine über 20 s gehaltene Taste gilt als hängend; 300 ms Nachlauf nach dem Verschwinden des Zeigers; nie bei anderem Fenster im Vordergrund oder solange Overlays verschoben werden.
- `ControlModeLearner` (rein): 12 s Zeiger sichtbar beim Kämpfen = klassische Steuerung (Funktion schaltet sich ab), 15 s verborgen = Steuerung mit ausgeblendetem Zeiger; Zähler klingen ab; wird in den Einstellungen gemerkt (`MenuControlMode`).
- `MenuWatcher` (UI-Thread, 50 ms): `GetCursorInfo`, `GetAsyncKeyState` nur bei sichtbarem Zeiger und nur jeden zweiten Blick. `GameWindow.ForegroundIsGameOrSelf()` und `ForegroundClientArea()` melden bei offenem Menü „nicht im Spiel“; dadurch weichen DPS-Overlay, Hotkey-Overlay, Karte, Zeitplan, Weltboss, Netzwerk und Kampf-Overlay alle gemeinsam aus.
- Einstellungen: Schalter „Bei Spielmenüs ausblenden“ (aus) mit Hinweis auf die Steuerung und Statuszeile der erkannten Steuerung unter Oberfläche; Hotkey **Ctrl+Alt+J** „Overlays bei diesem Menü zeigen“; zehn Sprachen; README (alle zehn).

**Bewusst offen / zu beobachten:**

- Im Spiel mit echten Menüs nicht ausprobiert (ich starte nichts auf dem Rechner); bitte einmal mit Karte/Inventar prüfen.
- Läuft das Spiel als Administrator, sieht ein normaler Prozess die gehaltenen Tasten womöglich nicht (dann hält „Alt halten“ den Meter nicht oben; der Zeiger selbst wird trotzdem erkannt). Ein Hinweis „als Administrator starten“ ist nicht eingebaut. **Zu prüfen:** ob `GetCursorInfo` selbst betroffen ist.
- Changelog-Eintrag und Features-Seite folgen beim Merge nach `main`.
