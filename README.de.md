🇬🇧 [English](README.md) · 🇩🇪 **Deutsch** · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Ein DPS- und Loot-Meter für **AION 4.6 (OriginAion)**, das ausschließlich aus der `Chat.log` des
Spiels arbeitet.

Es liest eine Textdatei, die der Client von sich aus schreibt. Es zeichnet keinen Netzwerkverkehr
auf und liest weder aus dem Spielprozess noch schreibt es hinein. Nichts von deinem Spiel verlässt
deinen Rechner — keine Schadenszahlen, kein Loot, keine Namen. Das Einzige, was es sendet, ist die
Update-Prüfung, die GitHub nach einer neueren Version fragt und sich abschalten lässt; siehe
[Updates](#updates).

## Installation

1. `AionDpsMeter-win-Setup.exe` aus dem [aktuellen Release](../../releases/latest) herunterladen
   und ausführen. Es gibt nichts durchzuklicken — die Installation läuft ins Benutzerprofil und
   startet den Meter. Keine Adminrechte, kein .NET nötig.
2. Unter **Settings → App Settings** den **Aion-Installationsordner** auswählen — den
   Wurzelordner, also den mit `bin64\game.dll` darin. Der Dialog meldet sofort, ob er eine gültige
   Installation gefunden hat und ob dort bereits eine `Chat.log` liegt.

> **Umstieg von 0.5.2 oder älter?** Vorher die alte Version deinstallieren (Windows-Einstellungen →
> Apps → *Aion DPS Meter*), dann den neuen Installer ausführen. Diese Versionen lagen in
> `Program Files` — genau deshalb konnten sie sich nie selbst aktualisieren. Das ist einmalig, ab
> dann kommen Updates von allein.

### Voraussetzung: das Chat-Log des Clients muss an sein

Aion schreibt die `Chat.log` nur, wenn die clientinterne Option `g_chatlog` aktiviert ist. Dieser
Schalter sitzt im Spielclient, nicht in diesem Tool — aktiviere ihn so, wie du es ohnehin tust
(zum Beispiel mit [ShugoConsole](https://github.com/grenadium/ShugoConsole)). **Aion DPS Meter
fasst dafür den Spielprozess nicht an**; wird die Datei nicht geschrieben, hat der Meter nichts zu
lesen.

## Bedienung

Die Aufzeichnung läuft, sobald der Meter offen ist und ein gültiger Aion-Ordner eingetragen wurde.

**Deine Chat-Historie wird nie gelesen.** Beim Start springt der Meter an das *aktuelle* Ende der
`Chat.log` und verarbeitet nur Zeilen, die ab diesem Moment dazukommen — wie ein Tonbandgerät, das
gerade eingeschaltet wurde, nicht wie ein Archiv-Scanner. **Pause verwirft**, statt aufzuschieben:
Zeilen, die während der Pause geschrieben werden, sind endgültig übersprungen, ein Resume spielt
also nie einen Kampf nach, den du bewusst ausgelassen hast. Vergangene private Gespräche,
Legions-Chat und Flüstern werden nie angesehen.

### Ansichten

- **Dmg** — Schaden pro Spieler mit Summe und DPS, Klassensymbolen und sortierbarer Liste. Der
  **Mob/Boss**-Filter schaltet die Spalte zwischen Gesamt-DPS und echtem ziel-bezogenem **iDPS**
  um (Schaden an einem Ziel geteilt durch die gemeinsame Kampfdauer der Gruppe mit diesem Ziel).
- **Loot** — was für wen gefallen ist: Person, Gegenstand, Menge und Seltenheitsstufe. Relikte
  zählen zusätzlich auf die Abyss-Punkte der jeweiligen Person.

### Hide UI (Overlay)

Verwandelt das Fenster in kleine, klickdurchlässige Chips, die über dem Spiel liegen bleiben
können — einer pro Spieler, mit Name, Schaden und DPS. Umschalten mit **Strg+Alt+H**, von überall
aus, damit es nie eine Einbahnstraße ist.

### Copy

**Copy** legt eine einzeilige, chatfertige Rangliste in die Zwischenablage
(`Name 1.234.567 (890), …`). In der Loot-Ansicht entsteht stattdessen eine Loot-Zusammenfassung für
den Aion-Chat, **Copy All** liefert eine Discord-Markdown-Tabelle.

### Befehle im Spiel

Diese als normale Chat-Zeilen tippen, um den Meter zu steuern, ohne das Spiel zu verlassen:

| Befehl | Wirkung |
|---|---|
| `.ui` | Hide-UI-Overlay umschalten |
| `.pause` / `.resume` | Aufzeichnung anhalten / fortsetzen |
| `.dmg` | Schadensrangliste in die Zwischenablage kopieren |
| `.cleardmg` | aktuelle Session löschen |
| `.loot` | Loot-Zusammenfassung in die Zwischenablage kopieren |

Nur Charaktere, die du in den Einstellungen hinterlegt hast, dürfen sie auslösen — ein `.cleardmg`,
das ein Fremder in einem Kanal tippt, den du nicht einmal liest, kann deine Session also nicht
löschen.

## Updates

Der Meter aktualisiert sich selbst. Beim Start und danach alle fünf Minuten fragt er bei GitHub
nach einer neueren Version, lädt sie im Hintergrund und tauscht sie beim nächsten Start ein. Kein
Installer, kein UAC-Prompt, nichts zu klicken. Das geht, weil er im Benutzerprofil liegt und nicht
in `Program Files` — dort darf er seine eigenen Dateien ersetzen.

Ist ein Update fertig geladen, erscheint unten in der Statuszeile eine grüne Zeile; ein Klick
darauf bietet den sofortigen Neustart an. Ablehnen kostet nichts — die Version liegt schon da und
wird beim nächsten normalen Start aktiv. Ein Popup gibt es bewusst nicht: Das Fenster liegt über
einem laufenden Spiel, und ein Dialog, der mitten im Boss den Fokus klaut, ist schlimmer als ein
spätes Update.

**App → Check for updates** macht dasselbe auf Zuruf und sagt dir auch, wenn du schon aktuell bist.

Die Prüfung liest genau eine URL und sendet nichts außer der Anfrage selbst:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Abschalten unter **Settings → App Settings → Updates**. Der Menüpunkt funktioniert weiterhin —
der ist deine Nachfrage, nicht die Entscheidung des Programms.

## Wie genau ist das?

Validiert gegen drei `Chat.log`-Dateien desselben Sauro-Supply-Base-Runs, aufgezeichnet auf drei
verschiedenen PCs (einer davon ein deutscher Client), mit den echten Boss-HP als Referenz. Der
Schaden pro Boss liegt **0,02 % – 2,8 %** neben dessen tatsächlichen HP:

| Boss | Echte HP | Gemessen | Abweichung |
|---|---|---|---|
| Wachhauptmann Ahuradim | 1.736.993 | 1.737.299 | +0,02 % |
| Dunkelverschlinger Derakanak | 1.343.657 | 1.347.258 | +0,27 % |
| Inspektionsoffizier Sayahum | 1.377.644 | 1.370.734 | −0,50 % |
| Versorgungskommandant Ranodim | 489.332 | 503.244 | +2,84 % |

Der Rest ist Overkill beim Todesstoß, den kein log-basierter Meter sehen kann. Der Gesamtschaden
eines Spielers kam auf allen drei Rechnern auf die Einheit gleich heraus.

Zwei bekannte, harmlose Ausreißer: Bei einem Boss, dessen Schild Schaden schluckt, wird dieser
Schaden trotzdem geloggt (er liest sich dadurch über seinen HP), und mehrere Mobs mit demselben
Namen werden zusammengezählt.

## Aus dem Quellcode bauen

```
dotnet build
dotnet run -- selftest                    # Selbsttests für Parser und DPS-Rechnung
dotnet run -- chatlog <Pfad-zur-Chat.log> # Datei parsen und Zusammenfassung ausgeben
```

Nur Windows (WPF). Die Selbsttests fahren wortgetreue Zeilen aus echten Logs in allen
unterstützten Sprachen durch und sind der schnellste Weg zu sehen, ob eine Parser-Änderung etwas
kaputt gemacht hat.

## Hinweis zu Server-Regeln

Dieses Tool liest nur eine Log-Datei, die das Spiel selbst erzeugt. Trotzdem setzen private Server
eigene Regeln zu Drittsoftware und Addons — ein Blick in die von OriginAion lohnt sich vor dem
Einsatz.
