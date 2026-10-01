# Aion-2-Daevanion-Bretter

`nodes.json` — Knoten-ID -> `[Brett, Zeile, Spalte, Grad, Typ, Stat-Token bzw. Skill-ID, Wert bzw. Level]`
und Brett-ID -> `[Name, Klasse]`. Nur Fakten, abgeleitet aus den Spieldaten, die der installierte
AionFlex-Client mitbringt (`data/daevanion_boards.json`, Stand 2026-10-01). Genutzt von
`Aion2DaevanionCatalog`, um die im Login-Paket 0x26e2 gesendeten freigeschalteten Knoten
(Brett + Knoten-IDs) in Boni zu uebersetzen.
