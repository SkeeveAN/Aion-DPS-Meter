# Aufnahmen auslesen (Python, nur lesend)

Kleine Hilfsskripte für die Debug-Aufnahmen (`%APPDATA%\Aion DPS Meter\Debug\aion2-*.jsonl`).
Ausführen mit `python3 -I <skript> <datei-oder-ordner>`.

- `frames.py` - setzt den Strom nach Seq zusammen, schneidet die Varint-Frames, entpackt FF-FF-LZ4-Bundles (`iter_frames`).
- `p4738b_lib.py` - liest das Abklingzeiten-Frame `0x4738` (`parse`), siehe [docs/protocol/cooldown-frames.md](../../docs/protocol/cooldown-frames.md).
- `dump.py <datei> <opcode-hex> <anzahl>` - gibt die ersten Frames eines Opcodes aus.
- `p0438c2.py <ordner>` - zählt Frames `0438` ohne Schadensblock danach, ob ein Schadens-Frame desselben Skills danebenliegt (Ausweichen/Widerstehen).
