🇬🇧 [English](README.md) · 🇩🇪 **Deutsch** · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**Der kostenlose DPS-Meter für AION 2** — Live-Schaden und -Heilung für deine ganze Gruppe, ein durchklickbares Overlay, Boss-Ranglisten, eine Karte für Pets und Sammelpunkte, ein Event-Zeitplan und Weltboss-Timer. Für Windows, Open Source, und er liest nie den Spielspeicher.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Für Windows herunterladen</b></a> · <a href="https://aiondps.com">Webseite</a> · <a href="https://aiondps.com/features">Alle Funktionen</a> · <a href="https://aiondps.com/instances">Boss-Ranglisten</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="Das Meter-Fenster: jeder Spieler mit Klassen-Icon, DPS, Gesamtschaden und Anteilsbalken">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="Das durchklickbare Overlay als kleine Chips über dem Spiel">
</p>

> Die Namen, Spieler und Zahlen in den folgenden Bildern sind Demodaten.

## Warum dieser Meter

- Live-DPS, iDPS und HPS für jeden Spieler in deiner Gruppe oder deinem Raid
- Ein durchklickbares Overlay, das keinen Klick abfängt
- Skill-Aufschlüsselung mit Krit-Raten und DPS-Kurve
- Boss-Ranglisten und öffentliche Charakterprofile auf aiondps.com (Upload optional, standardmäßig aus)
- Interaktive Karte, Event-Zeitplan, Weltboss-Timer und ein Netzwerk-Overlay
- Sicher: passiver Paketmitschnitt, kein Speicherlesen, keine Code-Injektion
- Kostenlos, Open Source (MIT) und aktualisiert sich selbst

Funktioniert mit der globalen Version von AION 2 — allen Regionen, die NCSOFT am 5. Oktober 2026 gestartet hat. Korea und Taiwan sind einige Versionen voraus, dort kann die Skill-Erkennung unvollständig sein.

Ein Schaden-/Heilungs-Meter und Werkzeugkasten für **Aion 2**. Es liest den Netzwerkverkehr des Spiels auf deinem Rechner
über den [Npcap](https://npcap.com)-Treiber — passiv: Es sendet nie ein Paket und fasst weder den
Spielprozess noch dessen Speicher an. Nichts von deinem Spiel verlässt deinen Rechner, es sei denn, du lädst es hoch (siehe
[Uploads](#uploads)); das Einzige, was sonst gesendet wird, ist die Update-Prüfung, die GitHub fragt, ob eine neuere
Version existiert, und die sich abschalten lässt; siehe [Updates](#updates).


## Inhalt

[Installation](#installation) · [Was es kann](#was-es-kann) · [Benutzung](#benutzung) · [Tastenkürzel](#tastenkürzel) ·
[Uploads](#uploads) · [Updates](#updates) · [Sprachen](#sprachen) · [Aus dem Quellcode bauen](#aus-dem-quellcode-bauen)

## Installation

1. Installiere den [Npcap](https://npcap.com)-Treiber (der Meter braucht ihn, um den Verkehr des Spiels zu sehen;
   er gehört nicht zum Installer).
2. Lade `AionDpsMeter-win-Setup.exe` vom [neuesten Release](../../releases/latest) herunter und führe sie aus.
   Sie installiert in dein Benutzerprofil und startet den Meter — keine Administratorrechte, kein .NET nötig.
3. Starte den Meter und logge dich dann mit deinem Charakter ein. Der Meter liest Charakter, Ausrüstung, Skills und
   Daevanion-Boards aus dem Spiel selbst; der Server, auf dem du spielst, wird automatisch erkannt.

## Was es kann

### Live-Schaden und Heilung für alle

Jeder Spieler in deiner Gruppe oder deinem Raid mit Name, Gesamtschaden, DPS und Anteilsbalken; Fraktion und Klasse
werden als Icons angezeigt. Filtere nach Klasse, Gruppe oder Ziel. Der **Mob/Boss**-Filter schaltet zwischen Gesamt-DPS und
echtem **iDPS** pro Ziel um; Bosse werden aus den Daten des Spiels erkannt und mit Namen angezeigt. Ein
**PvE/PvP**-Schalter trennt die beiden, und ein Wipe an einem Boss bekommt einen eigenen Eintrag (ein Totenkopf markiert einen Versuch,
nach dem der Boss wieder volle Lebenspunkte hatte).

### Fünf Ansichten, ein Klick entfernt

**Schaden**, **Heilung**, **Erlittener Schaden** und zwei **All in One**-Ansichten. Umschalten mit dem Tastenkürzel
(Standard **Ctrl+Alt+M**) oder per Klick auf die Modusanzeige im Overlay. *All in One* zeigt erlittenen Schaden,
Heilung und Gesamt nebeneinander, *All in One Compact* behält die gewohnten Spalten DPS und Gesamt und setzt
Heilung und erlittenen Schaden in eine kleine zweite Zeile unter den Namen.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="All-in-One-Ansicht: erlittener Schaden, Heilung und Gesamtschaden nebeneinander">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="Das durchklickbare Overlay als kleine Chips über dem Spiel">
</p>

### Ein durchklickbares Overlay

**Hide UI** macht aus dem Fenster kleine Chips (oder ein kompaktes Panel), die über Aion 2 liegen, ohne
auch nur einen Klick abzufangen. Deckkraft, Schadensanteil-Balken, die Lebenspunkte des Bosses und der erlittene Schaden pro Spieler
sind Optionen. Das Overlay zeichnet nur, solange das Spiel im Vordergrund ist. Schalte es von überall per Tastenkürzel um.

Optional (**Einstellungen → Oberfläche → Bei Spielmenüs ausblenden**, standardmäßig aus): Der Meter und seine Overlays machen Platz, solange eines der Spielmenüs offen ist (Karte, Inventar, Legion, Shop, Esc ...), und kommen zurück, wenn du es schließt. Erkannt wird das am Mauszeiger: Im Steuerungsmodus, der den Cursor beim Spielen versteckt, zeigt das Spiel ihn, sobald ein Menü aufgeht; hältst du die Taste zum Anzeigen des Cursors (standardmäßig Alt), bleibt der Meter sichtbar, damit du ihn anklicken kannst, und **Ctrl+Alt+J** zeigt die Overlays für das gerade offene Menü. Im klassischen Steuerungsmodus ist der Cursor immer sichtbar, deshalb lassen sich Menüs nicht unterscheiden; der Meter bemerkt das im Kampf und bleibt dann einfach sichtbar.

### Skill-Aufschlüsselung für jeden Spieler

**Doppelklick** auf einen Spieler: jeder Skill mit Treffern, Crit-Rate, Summe, Min, Max und Durchschnitt, aufgeteilt nach Ziel,
dazu eine DPS-Kurve über den gesamten Kampf. Crit-Raten werden direkt aus den Trefferdaten des Servers gelesen und sind daher exakt. Die Tabelle zeigt außerdem, wie die Treffer einer Fertigkeit gelandet sind: **Rücken**, **Front**, **Perfekt**, **Doppelt** und **Multi** (mehrere Schläge in einem Treffer) als Anteil ihrer direkten Treffer, gelesen aus den Trefferdaten. Doppelt (etwa der doppelte Schaden), Perfekt (ein kleiner Bonus) und Parade wurden anhand des Schadens geprüft, den sie verursachen; Rücken und Front stammen aus einem Richtungsbyte, das noch nicht gegen das Spiel geprüft wurde, und das Fenster weist darauf hin.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Skill-Aufschlüsselung eines Spielers mit einer Schaden-über-Zeit-Kurve">
</p>

### Rotation, Effekte und Stagger

Dasselbe Fenster hat zwei weitere Tabs. **Rotation** zeichnet jeden Skill, den der Spieler benutzt hat, im Moment der Benutzung auf eine Zeitleiste: Unterstützungsskills in der oberen Spur, Schadensskills darunter, ein direkt nach einem anderen eingeflochtener Skill eine Zeile tiefer, sodass eine Folge von Einflechtungen wie eine Treppe aussieht. Dahinter läuft der DPS des Spielers (der Durchschnitt der letzten fünf Sekunden) und die Momente, in denen ein Monster gestaggert war. Zieh, um dich durch den Kampf zu bewegen, **Strg + Mausrad** zoomt, und beim Überfahren eines Symbols siehst du den Einsatz (wann, der wievielte von wie vielen, die Zeit seit dem vorherigen, der DPS zu diesem Zeitpunkt). Darunter stehen der **Opener** (die ersten Skills in Reihenfolge) und die *Rotation in Kürze*: Einsätze, Pro Min., Abstand zwischen zwei Einsätzen, Erster Einsatz und der Anteil am Schaden. **Buffs** listet jeden Effekt auf, der auf dem Spieler lag, mit seiner Laufzeit, dem Teil davon, der vom Spieler selbst stammte, wie oft er aufgelegt wurde und wer ihn gegeben hat - ein Effekt, den zwei Spieler gleichzeitig aufrechterhalten haben, zählt nur einmal.

Ein Kampf behält dies in der lokalen Historie, sodass ein alter Boss erneut betrachtet werden kann; mit einer älteren Version aufgezeichnete Kämpfe zeigen keine Rotation.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="Der Rotation-Tab des Spielerfensters: Skills auf einer Zeitleiste mit dem DPS dahinter, der Opener und die Rotation in Kürze">
</p>

### Replay eines Boss-Kampfs (früher Test)

Im Spielerfenster eines Boss-Kampfs spielt **Replay** den Kampf von oben ab: Deine Gruppe als klassenfarbige Punkte mit den letzten Sekunden ihres Wegs, der Boss mit seiner Blickrichtung und seiner Gesundheit, die Flächen der Skills, die er gerade aufbaut (bernsteinfarben beim Aufbau, rot beim Auftreffen – gestrichelt, solange die Größe noch geraten ist), ein roter Ring um jeden, den er getroffen hat, die Toten durchgestrichen. Unter der Karte zeigt eine Zeitleiste die großen Angriffe des Bosses (bernstein), die Treffer auf die Gruppe (rot), Tode, Stagger (schattiert) und die Gesundheit des Bosses; per Klick oder Ziehen springst Du zu diesem Moment. Abspielen und Pause (Leertaste), Geschwindigkeiten von halb bis vierfach, ← und → springen fünf Sekunden, Karte ziehen, Mausrad zum Zoomen, Doppelklick zum erneuten Einpassen.

Die Positionen sind die eigenen Messwerte des Spiels: Es sendet sie mit Bewegungen und Skills, nicht in regelmäßigem Takt, deshalb wird ein Weg gerade zwischen zwei Positionen gezeichnet. Das Spiel sagt nie, wie groß die Fläche eines Angriffs ist: Der Meter lernt die Größe jedes Skills daraus, wen er getroffen hat, und merkt sie sich über Kämpfe hinweg. Gezeigt werden nur Deine Gruppe und die Bosse, nie andere Spieler. Ein Kampf behält sein Replay im lokalen Verlauf; Kämpfe, die mit einer älteren Version aufgezeichnet wurden, haben keines.

<p align="center">
  <img src="Web-Frontend/images/features/replay@2x.webp" width="640" alt="Das Replay eines Boss-Kampfs: die Gruppe um den Boss, eine Fläche im Aufbau, die Zeitleiste mit der Gesundheit des Bosses">
</p>

### Dein ganzer Charakter auf einen Blick

Das **Charakter**-Fenster (Personen-Symbol) zeigt, was das Spiel über dich sendet: Ausrüstung mit Itemlevel und
Verzauberung (einschließlich der Verstärkungsstufe über +15), Arkana, Artenwissen, alle Skills mit ihren Leveln
und die vollständigen Daevanion-Boards. Es merkt sich deinen letzten Login und ist deshalb nie leer. Fahre über ein Teil für seine
Details (Steine, gewürfelte Werte, Götterstein).

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="Das Charakter-Fenster mit Ausrüstung, Itemlevels und Verzauberung">
</p>

### Interaktive Karte mit Pets und Sammeln

Ein Karten-Overlay zeigt deine Position, die Spawnpunkte der Pets, hinter denen du her bist, und die Sammelpunkte, die du
eingeschaltet hast — Kräuter, Erze, Holz, Edelsteine und mehr — ohne per Alt-Tab auf eine Webseite zu wechseln. Aufgelistet wird nur, was deine eigene
Fraktion nutzen kann, und die zuletzt bekannte Position bleibt auf der Karte, während das Spiel geschlossen ist. Die Pet-
Einstellungen listen jede Region von Verteron, Altgard und dem Abyss mit ihren Pets und deinem eigenen Pet-Level auf, dazu einen
**Selten**-Tab für Pets mit wenigen Spawnpunkten. Pfeile am Rand zeigen auf das nächste Pet oder Sammelobjekt.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="Die interaktive Karte mit Pet-Spawnpunkten und der Pet-Liste">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Sammel-Einstellungen: einschalten, was auf der Karte erscheint">
</p>

### Event-Zeitplan mit Erinnerungen

Ein Overlay listet die nächsten Events — Schlachtfeld, die Abyss-Belagerungen (Artefakt-Belagerung), das Shugo-Fest,
den Beritra-Luftangriff, den Raumzeit-Riss und mehr. Wähle pro Event einen Sound und eine Lautstärke und lass dich
wenige Minuten vor dem Start erinnern. Ein Schalter aktiviert oder deaktiviert alle Erinnerungen.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="Zeitplan-Einstellungen mit Sound und Lautstärke pro Event">
</p>

### Weltbosse in deinem Overlay

Schalte einen Feldboss ein, um seinen Timer im Overlay zu sehen und seinen Sound in dem Moment zu hören, in dem er da ist. Die Zeiten
kommen direkt aus dem Spiel; der Server sendet sie für die Karte, auf der du dich befindest, die anderen Karten zeigen ihre erst,
nachdem du dort warst. Sound und Lautstärke werden pro Boss eingestellt, gruppiert nach Region.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="Weltboss-Einstellungen: jeder Feldboss nach Karte mit Wiederkehrzeit, Schalter, Ton und Lautstärke">
</p>

### Netzwerk-Overlay

Ping, Jitter, Datenverkehr, Paketverlust und wie lange du schon online bist, gemessen am Verkehr, den dein PC
ohnehin empfängt. Die Werte wechseln von Grün zu Rot, je schlechter die Verbindung wird. Wähle die gewünschten Zeilen,
sperre die Position, schalte es per Tastenkürzel ein und aus.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="Das Netzwerk-Overlay: Ping, Jitter, Datenverkehr und Paketverlust">
</p>

### Kampf-Overlay

Ein transparenter Rahmen mit bis zu drei Elementen, die du jeweils einzeln platzierst und in der Größe anpasst:

- **Boss-Zauberleiste:** In dem Moment, in dem ein Boss eine Fähigkeit beginnt, siehst du ihren Namen, eine ablaufende Leiste mit der Restzeit und wen sie trifft (du in Rot). Eine Fähigkeit, die mehrere Spieler getroffen hat, ist mit **FLÄCHE** markiert; **TÖDLICH** bedeutet, dass der höchste Treffer, den du davon bisher erhalten hast, mindestens deinen aktuellen Lebenspunkten entspricht. Der Meter lernt jede Fähigkeit (Fläche oder nicht, wann ihr Schaden eintrifft) und merkt sie sich über Sitzungen hinweg.
- **Ziel des Ziels:** Zielt das mit Tab gewählte Ziel selbst auf jemanden, siehst du, auf wen: ein Boss mit Bild und Name, ein Spieler mit Klassenemblem und Name.
- **Heiler-Ziel** (optional): die Lebensleiste des gewählten Ziels und die Debuffs (oder Buffs) darauf.

Entsperre das Overlay unter **Einstellungen → Kampf-Overlay**, um es einzurichten: Zieh den Körper eines Elements, um es zu verschieben, zieh an den Rändern für die Größe, an der Ecke zum Skalieren; der Griff unten rechts am ganzen Rahmen skaliert alle Elemente zusammen. Gesperrt ist es klickdurchlässig und erscheint nur, solange das Spiel im Vordergrund ist. **Ctrl+Alt+C** schaltet es ein und aus, ebenso der Hotkey *Alle Overlays an / aus*.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="Das Kampf-Overlay: zwei Boss-Zauber mit Zeitleiste, das Ziel des Ziels und das Heiler-Ziel mit Leben und Debuffs">
</p>

### Ankündigungen und Twitch Drops

Ein Megafon-Knopf öffnet die offiziellen NCSoft-Ankündigungen auf aiondps.com — Wartungen, Ausfälle, neue
Server, Servertransfers, Entschädigungen — neueste zuerst, mit einem farbigen Label pro Art und einem Filter. Solange
eine AION-2-Twitch-Drops-Kampagne läuft, öffnet ein Twitch-Knopf eine Seite mit ihren Belohnungen, den Stunden, die du
zuschauen musst, einem Countdown und den Streams, bei denen Drops gerade aktiviert sind. Eine Markierung *Neu* zeigt an, wenn
etwas neuer ist als dein letzter Besuch.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="Offizielle AION-2-Ankündigungen mit Filter nach Art">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Twitch-Drops-Kampagnen mit Belohnungen und Live-Streams">
</p>

### Der Meter in deinem Stream (OBS, Streamlabs)

Zeige den Meter live über deinem Spiel, ohne sein Fenster abzufilmen: Schalte **Einstellungen → Oberfläche → Stream-Overlay für OBS an** ein, nutze dann **Link für OBS kopieren** (oder im Tray-Symbol *Link für das Stream-Overlay (OBS) kopieren*), füge in OBS oder Streamlabs eine **Browser**-Quelle hinzu, füge den Link ein und lege die Größe der Quelle fest. Die Seite folgt dem Meter live: die aktuelle Ansicht (Schaden, Heilung, erlittener Schaden, alles in einer), die Spieler mit Klassensymbol und Balken sowie die Gesundheit des Bosses. Hänge `?scale=1.5` oder `?rows=5` an den Link, um sie zu vergrößern oder weniger Zeilen anzuzeigen.

Er läuft nur auf deinem PC: Der Server lauscht auf `127.0.0.1` (von außerhalb deines Computers ist er nicht erreichbar), der Link enthält einen zufälligen Schlüssel (mit **Neuer Schlüssel** funktioniert der alte Link nicht mehr), es wird nur `GET` bedient, die Seite lädt nichts von anderswo, und es wird nichts an irgendjemanden gesendet. Spielernamen sind **standardmäßig aus** - jeder wird wie in den Uploads mit seiner Klasse angezeigt - und lassen sich in derselben Einstellungskarte einschalten.

### Sessions

Jede Karte und jede Instanz wird als eigene Session aufgezeichnet, damit du dir einen früheren Durchlauf noch einmal ansehen kannst. Ein Plausibilitätscheck vergleicht den Schaden mit den verlorenen Lebenspunkten des Bosses.

<p align="center">
  <img src="Web-Frontend/images/features/sessions@2x.webp" width="590" alt="Session-Manager mit Zeitleiste und Plausibilitätscheck">
</p>

Der Verlauf merkt sich, welche Boss-Kämpfe online gelandet sind: Seine Liste hat eine Spalte **Hochgeladen**, kann nur die nicht hochgeladenen Boss-Kämpfe zeigen, und **Einstellungen → Oberfläche** nennt, wie viele es sind. Von einer älteren Version aufgezeichnete Kämpfe zeigen stattdessen ein „?“ (der Verlauf hat das damals nicht festgehalten).

## Benutzung

Die Aufnahme beginnt, sobald der Meter läuft. **Pause verwirft** statt zu puffern: Ereignisse
während einer Pause sind für immer weg, ein Fortsetzen spielt also nie einen Kampf nach, den du ausgesessen hast.

- **Copy** legt ein einzeiliges, chatfertiges Ranking in die Zwischenablage (`Name 1.234.567 (890), …`); **Copy All**
  liefert eine Discord-Markdown-Tabelle.
- Das Aussehen wird unter **Einstellungen → Oberfläche** festgelegt: Theme (dunkel/hell), Schriftgröße, Sprache, Overlay-Optik
  und -Deckkraft, Start mit Windows.
- Deine Einstellungen überstehen einen Absturz: Die Einstellungsdatei wird in einem Zug geschrieben, und eine Kopie vom letzten Start liegt daneben. Ist die Datei einmal beschädigt, wird diese Kopie wiederhergestellt (die beschädigte Datei bleibt daneben erhalten). **Einstellungen → Oberfläche → Alle Einstellungen auf Standard zurücksetzen** fragt zweimal nach und lässt sich 30 Sekunden lang rückgängig machen; deine Upload- und Datenschutz-Entscheidungen, die Update-Prüfung und dein Charakter bleiben dabei, wie sie sind.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="Einstellungen: Oberfläche, Overlay und Verhalten">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="Einstellungen: Tastenkürzel">
</p>

## Tastenkürzel

Systemweit, auch wenn das Spiel den Fokus hat. Klicke unter **Einstellungen → Tastenkürzel** auf ein Feld und drücke die
gewünschte Kombination (sie braucht Ctrl, Alt, Shift oder Win); **×** entfernt sie. Sie ersetzen Chat-Befehle,
die nicht funktionieren können, weil der Chat von Aion 2 im Netzwerkverkehr nicht lesbar ist.

| Standard | Aktion |
|---|---|
| **Ctrl+Alt+H** | Overlay (Fenster ausblenden, Chips zeigen) |
| **Ctrl+Alt+P** | Pause / Fortsetzen |
| **Ctrl+Alt+D** | Schadens-Ranking kopieren |
| **Ctrl+Alt+Shift+X** | Sitzung leeren |
| **Ctrl+Alt+U** | Boss-Schaden hochladen |
| **Ctrl+Alt+M** | Nächster Modus (fünf Ansichten) |
| **Ctrl+Alt+T** | Zeitplan-Overlay |
| **Ctrl+Alt+K** | Interaktive Karte an / aus |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | Pfeile am Rand für Pets / Sammelobjekte |
| **Ctrl+Alt+B** | Weltboss-Overlay |
| **Ctrl+Alt+N** | Netzwerk-Overlay |
| **Ctrl+Alt+C** | Kampf-Overlay |
| **Ctrl+Alt+J** | Overlays bei diesem Menü zeigen |
| **Ctrl+Alt+O** | Hotkey-Overlay (eine verschiebbare Liste der Tastenkürzel) |
| **Ctrl+Alt+A** | Alle Overlays an / aus |

## Uploads

- **Boss-Kämpfe** werden hochgeladen, wenn du auf Upload klickst (Knopf in der Leiste, Sitzung-Menü oder **Ctrl+Alt+U**): der
  Boss, die beteiligten Spieler, Schaden, Heilung, erlittener Schaden und Skills. Angenommen werden nur Bosse, die das Spiel
  angekündigt hat und der Katalog kennt. Automatischer Upload ist verfügbar und **standardmäßig aus**.
- **Dein eigenes Charakterprofil** (Name, Klasse, Level, Ausrüstung, Skills, Daevanion, Legion, Server, Fraktion) kann
  wenige Sekunden nach dem Login automatisch hochgeladen werden, damit man dich auf der Webseite findet. Das ist
  **standardmäßig aus**; schalte es unter **Einstellungen** ein.
- Ohne Upload verlässt nichts deinen Rechner.

## Updates

Der Meter aktualisiert sich selbst. Er fragt beim Start und danach alle fünf Minuten, solange er läuft, GitHub nach einer neueren Version,
lädt sie im Hintergrund herunter und tauscht sie beim nächsten Start ein — kein Installer, kein UAC-Dialog. Ist ein Update bereit,
erscheint eine grüne Zeile in der Statuszeile (und ein Abzeichen im transparenten
Overlay); ein Klick darauf bietet den sofortigen Neustart an. **App → Nach Updates suchen** macht dasselbe auf Wunsch.
Eine alte Version lädt noch 72 Stunden nach Erscheinen ihres Nachfolgers hoch, danach muss sie aktualisiert werden.

Die Prüfung liest eine einzige URL und sendet nichts außer der Anfrage selbst:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Schalte sie unter **Einstellungen → Updates** ab; der Menüpunkt funktioniert auch dann weiter.

## Sprachen

Englisch, Deutsch, Französisch, Spanisch, Russisch, Chinesisch (vereinfacht und traditionell), Portugiesisch (Brasilien),
Japanisch und Koreanisch — einzustellen unter **Einstellungen → Oberfläche → Sprache**. Diese README gibt es auch auf
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md) und [한국어](README.ko.md).

## Aus dem Quellcode bauen

```
cd Client
dotnet build
dotnet run -- selftest                              # Selbsttests (Protokoll, Capture, Dekodierung, ...)
dotnet run -- aion2-record <out.jsonl>              # Verkehr des Spiels aufnehmen ("stop" beendet)
dotnet run -- aion2-replay <file.jsonl>             # Aufnahme durch den echten Decoder abspielen
dotnet run -- aion2-upload-dryrun <file.jsonl>      # Uploads einer Aufnahme bauen, nichts senden
```

Nur Windows (WPF). `Tools/aion2-dat` liest die Texttabellen des Spiels (Namen in acht Sprachen); siehe dessen README.

## Hinweis zu Serverregeln

Der Meter beobachtet nur passiv den Netzwerkverkehr des Spiels. Trotzdem legen Publisher eigene
Regeln für Drittwerkzeuge fest — wirf vor der Nutzung einen Blick in die Bedingungen des Spiels.

## Haftungsausschluss und Lizenz

Aion 2 und alle zugehörigen Namen, Texte und Grafiken sind Eigentum von NCSOFT. Dieses Projekt steht in keiner Verbindung zu NCSOFT und wird weder von NCSOFT unterstützt noch gebilligt; Spielinhalte verbleiben bei ihrem Eigentümer. Der Quellcode steht unter der [MIT-Lizenz](LICENSE) — sie gilt nur für den Code, nicht für Spielinhalte.
