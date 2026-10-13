# OBS-Browserquelle: Meter live im Stream

Branch: `feature/obs-browser-overlay` · Stand: Plan, noch nichts umgesetzt

## Ziel

Streamer sollen den Meter in OBS/Streamlabs (Twitch, YouTube …) als **Browserquelle** über das Spiel legen können, ohne das Meter-Fenster abzufilmen. Die Seite läuft lokal, folgt den Einstellungen live und verlässt den Rechner nicht.

Ablauf für den Nutzer: Tray-Symbol oder Menü → „Link für das Stream-Overlay (OBS) kopieren“ → in OBS Quelle „Browser“ hinzufügen, Link einfügen, Größe setzen.

## Prinzip (Eigenentwurf, an bekannten Mustern orientiert)

- Kleiner HTTP-Server, gebunden **nur an 127.0.0.1**, Standard-Port 28920 (bei Belegung die nächsten 10 versuchen, eigener Port einstellbar).
- **Zufälliger Schlüssel** (32 Hex-Zeichen, `RandomNumberGenerator`) im Pfad: `http://127.0.0.1:PORT/<Schlüssel>/`. Vergleich zeitkonstant. Alles andere: schlichtes 403/404/405. Nur GET/HEAD. Der Host-Header muss `127.0.0.1:PORT` oder `localhost:PORT` sein (Schutz gegen DNS-Rebinding).
- Liefert nur eine feste Liste eigener Dateien (HTML, CSS, JS aus Ressourcen des Clients) mit `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, strenger CSP (`default-src 'none'`, Skripte/Stile nur vom Server selbst), `Referrer-Policy: no-referrer`.
- **Live-Daten per Server-Sent Events** unter `/<Schlüssel>/events` (ein Strom, einfacher als WebSocket): beim Verbinden die volle Lage („hello“), danach Änderungen je Kanal (`settings`, `snapshot`). Keep-Alive-Kommentar alle 15 s, `retry: 2000`. Höchstens 8 Clients; ein Client mit zu vollem Schreibpuffer (> 4 MB) wird getrennt.
- Die an die Seite gelieferten Einstellungen werden gefiltert: keine Fensterpositionen, keinen Schlüssel, keine Upload-IDs.

## Ist-Stand

- Im Client gibt es **keinen** lokalen Webserver (weder `HttpListener` noch Kestrel noch WebSocket).
- Datenquelle: Zeilen des Hauptfensters (`RefreshRows`, [PlayerRow.cs](../../Client/Ui/PlayerRow.cs)) und der Aggregator; beide hängen am UI-Thread. Momentaufnahmen müssen dort gebaut oder aus gesperrten Kopien serialisiert werden.
- Hotkeys/Menüs: [GlobalHotkeys.cs](../../Client/Ui/GlobalHotkeys.cs), Tray [TrayIcon.cs](../../Client/Ui/TrayIcon.cs). Klassensymbole, Boss-Bilder und Farben liegen in `Client/assets`.

## Umsetzung

1. **`StreamOverlayServer`** (neue Klasse): `HttpListener` auf `http://127.0.0.1:PORT/` (braucht keine Administratorrechte), Start/Stopp über Einstellung, sauberes Beenden beim Schließen des Hauptfensters.
2. **Momentaufnahme (`StreamSnapshot`)**: Liste der Spieler (Rang, Klasse, optional Name, DPS, Gesamt, Anteil, Heilung, erlittener Schaden), aktuelle Ansicht (Schaden/Heilung/erlitten/Alles/Alles kompakt), Boss-Zeile (Name, HP, Restzeit), Kampfzeit. Erzeugt auf dem UI-Thread, höchstens 4 mal pro Sekunde und nur, wenn sich etwas geändert hat; JSON in den SSE-Strom.
3. **Seite:** eine eigene HTML/CSS/JS-Seite (ohne Fremdbibliotheken), die dieselben Farben, Klassensymbole und Balken wie der Meter zeichnet, transparenter Hintergrund (für OBS), skalierbar über `?scale=` und die Einstellungen (Deckkraft, Schriftgröße, Spalten, Namen an/aus, Zeilenzahl).
4. **Einstellungen:** „Stream-Overlay an“ (**standardmäßig aus**), Port, „Link kopieren“, „Neuen Schlüssel erzeugen“ (alter Link wird ungültig), Option „Spielernamen zeigen“ (aus: nur Klassenbezeichnung, wie bei den Uploads).
5. **Später (eigene Teile):** Elemente des Kampf-Overlays (Cast-Leiste, Ziel des Ziels) und der Weltboss-Zeiten als weitere Schichten; ein „Stream-Fenster für Discord“.
6. Texte in zehn Sprachen, README-Abschnitt „Meter im Stream zeigen“ (OBS/Streamlabs), Features-Seite, Changelog.

## Abnahme

- Link in OBS einfügen: Meter erscheint mit transparentem Hintergrund und folgt Kampf, Ansichtswechsel und Einstellungen live.
- Aus dem Netzwerk (anderer Rechner) und mit falschem Schlüssel/Host kein Zugriff.
- Ausschalten der Option beendet den Server; neuer Schlüssel entwertet den alten Link.
- Kein messbarer Einfluss auf die Meter-Leistung bei 20 Spielern.

## Risiken

- Portkonflikt (siehe Portsuche), Firewall nicht betroffen, da nur Loopback.
- Namen im Stream: Datenschutz-Standard ist „aus“.
- Aggregator ist UI-Thread-gebunden → Serialisierung nur über Momentaufnahmen.
