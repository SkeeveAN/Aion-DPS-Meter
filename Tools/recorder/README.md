# Recorder mit Debug-Punkten

`Aion2-Aufnahme-Punkte.bat` startet `Aion DPS.exe aion2-record <datei>.jsonl --points`: die Aufnahme wie gewohnt als JSON-Lines, dazu die Datei `<datei>.points.jsonl`.

- Ein Server-Frame, dessen Opcode nirgends beschrieben ist (nicht in `docs/protocol`, nicht im Decoder, nicht in `opcodes.json`; Liste: `Client/assets/aion2/protocol/known_opcodes.json`), ist ein **Debug-Punkt**: zwei Töne, eine Zeile in der Konsole und eine Zeile in der Punkte-Datei (Uhrzeit, Opcode, Länge, die ersten 96 Byte).
- Hat ein anderer Meter einen Namen für den Opcode (Datei `Client/assets/aion2/protocol/opcode_hints.json`, derzeit 36 Namen von Questlog, Stand 6.11.2026), steht er in der Konsolenzeile, in `aion2-opcode-census` und im Feld `hint` der Punkte-Datei. Nur ein Hinweis: der Name ist ihrer, Aufbau und Bedeutung sind hier nicht geprüft. Ist ein Opcode in `docs/protocol` beschrieben, den Eintrag aus der Datei löschen.
- Höchstens zwei Punkte je Opcode (der zweite erst nach zwei ruhigen Minuten), die erste Minute der Aufnahme gibt keinen Ton (der Login sendet viele unbeschriebene Frames).
- Nach dem Ton kurz notieren, was gerade getan wurde. Entweder im Chat mit der Uhrzeit, oder in das Recorder-Fenster tippen + Enter (landet mit Uhrzeit in der Punkte-Datei).
- Die Liste der bekannten Opcodes nach neuen Dokumenten neu bauen: `python -I Tools/aion2-dat/build_known_opcodes.py`.
- Wie viele Töne eine alte Aufnahme gegeben hätte: `Aion DPS.exe aion2-opcode-census <aufnahme.jsonl>`.
