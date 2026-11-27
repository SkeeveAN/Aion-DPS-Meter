# Aion 2 G13 Plugin

Wechselt das Profil eines Logitech G13 automatisch passend zur Klasse des eigenen Charakters in Aion 2.
Eigenstaendiges Programm, unabhaengig vom DPS-Tool-Projekt.

## So funktioniert es
1. Liest den Stream-Link des DPS-Tools (derselbe Strom wie fuer OBS), darin ist die Klasse des eigenen Charakters markiert.
2. Sucht in der Einstellung der Klasse nach der Aktion: Tastenkuerzel senden oder Programm starten.
3. Die Logitech-Software reagiert auf das Kuerzel und wechselt das Profil (zu pruefen beim Tester).

## Aendern und neu bauen
- `./build.sh` baut die kleine .exe, `./build.sh full` die grosse ohne Laufzeit-Voraussetzung.
- Vor jeder neuen Fassung `<Version>` in `Aion2G13Plugin.csproj` erhoehen; die Version steht im Fenstertitel,
  so weiss man bei Rueckmeldungen, welche Fassung gemeint ist.
- `MainForm.cs` Oberflaeche, `Watcher.cs` Klassenerkennung, `Keys.cs` Tastenkuerzel, `Settings.cs` Speicherung
  (`%APPDATA%\Aion2G13Plugin\settings.json`).

## Download
`download/Aion2G13Plugin-0.2.0-full.zip` (runs without an installed .NET runtime, not signed).
SHA-256: `6521cb6ebc2ca43fbf6d2e07cd451826d3059babff4e51b731179a01cd3552c4`
