# Konto-Zähler (Tickets): Odenergie, Eintritts- und Belohnungszähler

Befund vom 14.10.2026 aus 25 Login-Listen und 56 Änderungen der Debug-Aufnahmen vom 10. bis 13.10. (Aahz, Chumley). Dieselben Opcodes nennen beide anderen Meter (nur Protokollfakten, kein Code); der Aufbau unten ist an unseren Aufnahmen **bis zum letzten Byte** nachgeprüft.

Das Spiel nennt diese Zähler *Contents Tickets*: Eintrittszahlen (Alltags-Dungeon, Albtraum, Dimensionsinvasion), Belohnungszahlen der Gruppen-Dungeons, besiegte Endbosse, Zugangszeiten (Abyss, Riss, Wachstums-Dungeons) und die **Odenergie** (Ticket `60000001`).

## Login-Liste `0x0b61`

Kommt beim Login 50 ms nach dem Login-Datensatz `0x2036` und **3 bis 5 s vor** dem Charakter-Datensatz `0x3336`, der den Namen nennt.

```
0b 61 | n (u8) | n x ( Maske (u8) | Ticket-ID (u32 LE) | Felder )
```

Die Maske wählt die Felder: Bit 1 = verbleibende Zeit (u64, ms), Bit 4 = Anzahl (varint), Bit 8 = Bonus (varint). **Ein fehlendes Feld ist 0** (ein aufgebrauchter Zähler hat Maske 0). Andere Bits kommen nicht vor.

Beispiele (Aahz, 17:11:54): Ticket 7 (Schugofesta-Schlüssel) Maske 4, Anzahl 28; 60000007 (besiegte Endbosse) 35; Ticket 3 (Untere Abyss) Maske 1, Zeit 25.200.000 ms = 7 h; **Odenergie** 540 (Maske 4). Ein anderer Charakter: Maske 12, Anzahl 75 und Bonus 520.

## Änderung `0x0c61`

```
0c 61 | Flag (u8) | Maske (u8) | Ticket-ID (u32) | Felder | Grund (u8) | wenn Flag 1: Zugewinn (u32)
```

Grund 2 = regelmäßiger Refill (z. B. zur vollen Stunde), 3 = verbraucht, 1 = dazugewonnen: Odenergie-Bonus 630 -> 640 kommt mit Flag 1 und Zugewinn 10.

## Odenergie

Anzahl = die tägliche Energie (Obergrenze 560, mit Abo 840), **Bonus** = der Teil aus Steinen (Beispiel: 75 + 520). Die Obergrenzen und Namen stehen in [Client/assets/aion2/tickets/tickets.json](../../Client/assets/aion2/tickets/tickets.json) (gebaut von `Tools/aion2-dat/build_tickets.py`: Namen aus den Texten des eigenen Clients in acht Sprachen; Obergrenzen, Konto/Charakter und Refill-Zeiten aus der ContentsTicket-Tabelle, wie sie ein anderes Werkzeug ausgelesen hat, gegen unsere Login-Listen geprüft).

## Im Client

`Aion2Tickets` ([Client/Aion2/Aion2Tickets.cs](../../Client/Aion2/Aion2Tickets.cs)) liest beide Frames; die Liste wartet, bis der Charakter-Datensatz den Namen nennt (die Login-Frames dazwischen nennen noch den alten Charakter). Die Zähler jedes Charakters liegen in `%AppData%\Aion DPS Meter\aion2-tickets.json`. Anzeige: Tools > Checkliste (kein Overlay). Die Spalte „~“ rechnet aus den Refills seit dem letzten Stand (täglich 16:00, wöchentlich Mittwoch 16:00) eine Schätzung.

## Offen
- Die Refill-Uhrzeit ist Serverzeit; hier gilt die Uhr des PCs (für die EU-Server dieselbe Zeit).
- Der Grund-Wert 2 kommt für die Zeit-Tickets 10 und 12 (nicht in der Tabelle) alle zehn Minuten; nicht gedeutet.

## Siehe auch

Weitere Zähler- und Fensterframes (Versorgungsgesuche, Saisonmissionen, Garantierte Belohnung, Ranglisten, Arena): [login-und-fenster-frames.md](login-und-fenster-frames.md). Odenergie/Schlüssel/Versuche stehen in `0x0b61` (Beispielwerte dort belegt).
