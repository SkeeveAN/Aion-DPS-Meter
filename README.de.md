🇬🇧 [English](README.md) · 🇩🇪 **Deutsch** · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Webseite mit Community-Boss-Ranglisten und Charakterprofilen: **https://aiondps.com**

Ein Schaden-/Heal-Meter für **Aion 2**. Es liest den Netzwerkverkehr des Spiels auf deinem Rechner über
den [Npcap](https://npcap.com)-Treiber — passiv: es sendet nie ein Paket und fasst weder den Spielprozess
noch dessen Speicher an. Nichts von deinem Spiel verlässt deinen Rechner, außer du lädst es hoch (siehe
[Uploads](#uploads)); dazu kommt die Update-Prüfung, die GitHub nach einer neueren Version fragt und
abschaltbar ist; siehe [Updates](#updates).

> Klassisches Aion (Chat.log-basiert) ist nicht mehr Teil des Meters. Die letzte Version, die es
> unterstützt, liegt auf dem Branch [`aion1-included`](../../tree/aion1-included).

## Installation

1. Installiere den [Npcap](https://npcap.com)-Treiber (der Meter braucht ihn, um den Verkehr des Spiels
   zu sehen; er gehört nicht zum Installer).
2. Lade `AionDpsMeter-win-Setup.exe` vom [neuesten Release](../../releases/latest) herunter und führe sie
   aus. Sie installiert in dein Benutzerprofil und startet den Meter — kein Administratorrecht, kein .NET nötig.
3. Starte den Meter und logge dich dann mit deinem Charakter ein. Der Meter liest Charakter, Ausrüstung,
   Skills und Daevanion-Boards aus dem Spiel selbst; der Server, auf dem du spielst, wird automatisch erkannt.

## Benutzung

Die Aufnahme beginnt, sobald der Meter läuft. **Pause verwirft** statt zu puffern: Ereignisse während einer
Pause sind für immer weg, ein Fortsetzen spielt nie einen Kampf nach, den du ausgesessen hast.

### Ansichten

- **Dmg** — Schaden pro Spieler mit Summe und DPS, Klassen-Icons und sortierbarer Liste. Der
  **Mob/Boss**-Filter schaltet die Spalte zwischen Gesamt-DPS und echtem Ziel-**iDPS** um. Bosse werden
  aus den Daten des Spiels erkannt und mit Namen angezeigt. **Doppelklick** auf einen Spieler zeigt die
  Skill-Aufschlüsselung.
- **Charakter** (das Personen-Symbol) — öffnet ein Fenster mit deinem eigenen Charakter: Profil, Ausrüstung
  mit Itemlevel und Verzauberung, Skills mit Leveln und die Daevanion-Boards. Es merkt sich deinen letzten
  Login und ist deshalb nie leer.

### Hide UI (Overlay)

Macht aus dem Fenster kleine durchklickbare Chips, die über dem Spiel liegen — einer pro Spieler mit Name,
Schaden und DPS. Umschalten mit **Strg+Alt+H**, von überall.

### Kopieren

**Copy** legt ein einzeiliges, chatfertiges Ranking in die Zwischenablage (`Name 1.234.567 (890), …`);
**Copy All** liefert eine Discord-Markdown-Tabelle.

### Chat-Befehle

`.ui` (Overlay), `.pause` / `.resume`, `.dmg` (Ranking kopieren) und `.cleardmg` (Sitzung leeren). Der
Handler nimmt sie nur von deinem eigenen Charakter an. Der Chat von Aion 2 wird noch nicht dekodiert,
deshalb bewirken sie derzeit nichts.

## Uploads

- **Boss-Kämpfe** werden beim Klick auf Upload (Knopf oder Session-Menü) hochgeladen: Boss, beteiligte
  Spieler, Schaden, Heilung, erlittener Schaden und Skills. Angenommen werden nur Bosse, die das Spiel
  angekündigt hat und der Katalog kennt.
- **Dein eigenes Charakterprofil** (Name, Klasse, Level, Ausrüstung, Skills, Daevanion, Legion, Server)
  wird wenige Sekunden nach dem Login automatisch hochgeladen, damit man dich auf der Webseite findet.
  In den **Einstellungen** abschaltbar.
- Ohne Upload verlässt nichts deinen Rechner.

## Updates

Der Meter aktualisiert sich selbst. Er fragt beim Start und alle fünf Minuten GitHub nach einer neueren
Version, lädt sie im Hintergrund und tauscht sie beim nächsten Start ein — kein Installer, kein UAC-Dialog.
Ist ein Update bereit, erscheint unten eine grüne Zeile; ein Klick darauf bietet den sofortigen Neustart an.
**App → Check for updates** macht dasselbe auf Wunsch.

Die Prüfung liest eine einzige URL und sendet nichts außer der Anfrage selbst:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Abschaltbar unter **Einstellungen → Updates**; der Menüpunkt funktioniert auch dann.

## Aus dem Quellcode bauen

```
cd Client
dotnet build
dotnet run -- selftest                              # Selbsttests (Protokoll, Capture, Dekodierung, ...)
dotnet run -- aion2-record <out.jsonl>              # Verkehr des Spiels aufnehmen ("stop" beendet)
dotnet run -- aion2-replay <datei.jsonl>            # Aufnahme durch den echten Decoder abspielen
dotnet run -- aion2-upload-dryrun <datei.jsonl>     # Uploads einer Aufnahme bauen, nichts senden
```

Nur Windows (WPF). `Tools/aion2-dat` liest die Texttabellen des Spiels (Namen in acht Sprachen); siehe dessen README.

## Hinweis zu Serverregeln

Der Meter beobachtet nur passiv den Netzwerkverkehr des Spiels. Trotzdem legen Publisher eigene Regeln für
Drittwerkzeuge fest — wirf vor der Nutzung einen Blick in die Bedingungen des Spiels.
