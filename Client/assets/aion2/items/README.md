# Aion-2-Gegenstandsdaten

`item_info.json` — Gegenstands-ID -> `[englischer Name, Slot, Grad, Tier, Itemlevel]`. Nur Fakten
(IDs, Namen, Slot, Grad, Tier, Itemlevel), abgeleitet aus den Spieldaten, die der installierte
AionFlex-Client mitbringt (`data/items.json`, Stand 2026-10-01); keine Bilder, keine Texte, keine
Statistik-Tabellen. Genutzt von `Aion2ItemCatalog` fuer die Ansicht "Charakter".

`item_names.json` — Gegenstands-ID -> `[Grad, en, de, fr, es, ru, pt, ja, ko]` fuer 9.163 Gegenstaende (Ausruestung, Material, Seelen ...),
gebaut mit `Tools/aion2-dat/build_item_names.py` aus der Tabelle `Item` und den Texten des installierten Clients (Stand 2026-10-17). Ein
leerer Text heisst: wie Englisch. Grad = Skala des Charakterfensters (1 Common ... 7 Special, 0 = keiner). Genutzt vom Beute-Protokoll.
