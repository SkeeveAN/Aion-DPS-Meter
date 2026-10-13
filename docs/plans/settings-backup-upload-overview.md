# Einstellungen sichern, Zurücksetzen mit Rückgängig, Übersicht „nicht hochgeladen“

Branch: `feature/settings-backup-upload-overview` · Stand: Plan, noch nichts umgesetzt

## Ziel

1. **Einstellungen sichern:** Eine beschädigte `meter-settings.json` (Absturz, Stromausfall beim Schreiben) kostet nicht mehr alle Einstellungen.
2. **Zurücksetzen mit Rückgängig:** „Auf Standard zurücksetzen“ in den Einstellungen verlangt einen zweiten Klick; danach lässt sich das 30 Sekunden lang mit „Zurücksetzen rückgängig“ zurücknehmen.
3. **Übersicht „nicht hochgeladen“:** Der Meter weiß dauerhaft, welche gespeicherten Boss-Kämpfe schon hochgeladen sind, zeigt die Zahl der noch offenen Kämpfe und lässt sie nachträglich hochladen.

## Ist-Stand

- `MeterSettings` ([MeterSettings.cs:357-397](../../Client/Ui/MeterSettings.cs)): eine JSON-Datei `%APPDATA%\Aion DPS Meter\meter-settings.json`. `Load()` schluckt `JsonException` und liefert Standardwerte; `Save()` ist ein einfaches `File.WriteAllText` (nicht atomar). Es gibt **weder Sicherung noch „Auf Standard zurücksetzen“** in den Einstellungen (`OnOverlayResetClicked` leert nur die Schadensdaten).
- Laden passiert an vielen Stellen, die Controller der Overlays lesen jede Sekunde neu; Fensterpositionen werden von `MovableOverlay` nur im Konstruktor gelesen.
- **Upload-Zustand:** Es gibt keinen dauerhaften Hochgeladen-Merker. `FightStore` ([FightStore.cs](../../Client/History/FightStore.cs)) hat keine Spalte dafür, `FightSummary` kein Feld; nur ein Speicher-Wörterbuch `_bossUploadedUntil` ([MainWindow.xaml.cs:2451](../../Client/Ui/MainWindow.xaml.cs)) geht beim Neustart verloren. `AutoUploadLog` ist nur ein Textlog.
- Upload: `UploadClient.SendAsync` liefert `UploadResult`; der Aufbau des Pakets (`BuildEncounterUpload`) hängt am laufenden `_aggregator`. Der Server fasst gleiche Kämpfe mehrerer Uploader zusammen (Zeitfenster 20 s, Teilnehmer-Ähnlichkeit ≥ 0,5), ein zweiter Upload desselben Kampfes ist also grundsätzlich unkritisch, **aber** zu prüfen (siehe `feature/upload-plausibility`).

## Teil A – Sichern und Wiederherstellen

1. **Atomar schreiben:** in `meter-settings.json.tmp` schreiben, dann ersetzen (`File.Replace`), damit nie eine halbe Datei entsteht.
2. **Sicherung beim Start:** Wurde die Datei erfolgreich gelesen, nach `meter-settings.backup.json` kopieren.
3. **Beschädigt:** Lässt sich die Datei nicht lesen, die kaputte Datei als `meter-settings.damaged-<Zeitstempel>.json` aufheben und aus `…backup.json` laden; Hinweis im Statusfeld („Einstellungen aus der Sicherung wiederhergestellt“).
4. Nach dem Wiederherstellen müssen Overlays ihre Positionen neu übernehmen: `MovableOverlay` liest sie nur im Konstruktor, daher Overlays neu erzeugen oder Position über einen `ApplySettings()`-Haken setzen.
5. Der Erststart-Assistent darf die Upload-Schalter nicht erneut zurücksetzen (Gate in [MainWindow.xaml.cs:332-339](../../Client/Ui/MainWindow.xaml.cs) prüfen).

## Teil B – Zurücksetzen mit Rückgängig

1. Neue Schaltfläche in den Einstellungen („Auf Standard zurücksetzen“). Erster Klick: Schaltfläche wird zu „Wirklich zurücksetzen?“; zweiter Klick führt aus.
2. Vorher den aktuellen JSON-Text im Speicher **und** als `meter-settings.undo.json` ablegen. Danach 30 Sekunden lang „Zurücksetzen rückgängig“.
3. **Bleibt erhalten:** die Datenschutz- und Upload-Entscheidungen (Auto-Upload, Profil-Upload, Fraktion/Server), damit ein Zurücksetzen nie ungewollt etwas freischaltet oder abschaltet; das steht im Hinweistext.
4. Sprache, Hotkeys und Overlay-Positionen werden zurückgesetzt.

## Teil C – Übersicht „nicht hochgeladen“

1. **Datenbank:** `ALTER TABLE fights ADD COLUMN uploaded_at TEXT NULL` in `FightStore` (in `try/catch`, da das Schema nur `CREATE IF NOT EXISTS` kennt; ggf. eine kleine `schema_version`-Tabelle einführen). `FightSummary` und `Query` erweitern.
2. `MarkUploaded(id)` überall dort, wo ein Upload erfolgreich war: automatische Uploads, Knopf/Hotkey, „alle Bosse hochladen“ (MainWindow ~2862, 3012-3090).
3. **Anzeige:** In `FightHistoryWindow` Spalte/Filter „Hochgeladen“; in den Einstellungen eine Zeile „Nicht hochgeladene Kämpfe: N (neuester: …)“ mit „Verlauf öffnen“.
4. **Nachträglich hochladen:** `FightDetail` durch denselben Paket-Aufbau schicken. Dafür den Aufbau von `_aggregator` lösen (`BuildEncounterUpload(FightDetail)`), das ist das größte Stück dieses Branches und kommt zuletzt.
5. Kämpfe ohne bekannte Boss-/Server-Zuordnung werden als „nicht hochladbar“ gekennzeichnet statt stillschweigend ausgelassen.
6. Alte Kämpfe (vor dem Update) gelten als „unbekannt“, nicht als „nicht hochgeladen“, damit nicht auf einen Schlag Hunderte offen erscheinen.

## Abnahme

- Datei abschneiden/zerstören: Meter startet mit den Einstellungen der letzten Sicherung.
- Zurücksetzen und innerhalb von 30 s Rückgängig: alle Werte wieder wie vorher, Overlays an den alten Plätzen.
- Hochgeladener Kampf zeigt „Hochgeladen“ auch nach Neustart; offener Kampf lässt sich nachträglich hochladen und taucht danach nicht mehr in der Zahl auf.

## Risiken

- Nachträglicher Upload ohne laufenden Aggregator.
- Rückgängig nach Neustart des Meters innerhalb der 30 s (Undo-Datei vorhanden, Zeitfenster aber nur im Speicher).
- Migration der Datenbank bei vielen gespeicherten Kämpfen (große Datei): in einer Transaktion und mit Fortschritt, nie beim Start blockieren.
