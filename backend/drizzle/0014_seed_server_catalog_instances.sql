-- Known instance lists per server, per the user - deliberately not exhaustive: only Origin
-- Aion/EuroAion (same 4.6 list) and Aion Riftshade (a wider 4.8 list, the 11 above plus 6
-- Riftshade-exclusive instances added in migrations 0010/0012) are confirmed so far. Every other
-- catalog server (including the OTHER 4.8 one, "Aion America - Fall of Beritra" - a different
-- private server can enable different content even at the same nominal patch version, so its list
-- is not assumed to match Riftshade's) gets no rows here at all and will show an empty instance
-- list until the user confirms its real one - same "empty beats wrong" rule as everywhere else in
-- this schema.
INSERT INTO `server_catalog_instances` (`server_catalog_id`, `instance_id`)
SELECT sc.id, i.id
FROM `server_catalog` sc
JOIN `instances` i ON i.name IN (
	'Tahmes',
	'Stahlrose: Anlegestelle',
	'Stahlrose: Kabine',
	'Stahlrose: Deck',
	'Sauro-Kriegsdepot',
	'Katalamize',
	'Stahlmauerbastion',
	'Ophidan Bridge',
	'Danuar Sanctuary',
	'Schutzturm der Ruhn',
	'Ruhnadium'
)
WHERE sc.name IN ('Origin Aion', 'EuroAion');
--> statement-breakpoint

INSERT INTO `server_catalog_instances` (`server_catalog_id`, `instance_id`)
SELECT sc.id, i.id
FROM `server_catalog` sc
JOIN `instances` i ON i.name IN (
	'Tahmes',
	'Stahlrose: Anlegestelle',
	'Stahlrose: Kabine',
	'Stahlrose: Deck',
	'Sauro-Kriegsdepot',
	'Katalamize',
	'Stahlmauerbastion',
	'Ophidan Bridge',
	'Danuar Sanctuary',
	'Schutzturm der Ruhn',
	'Ruhnadium',
	'Aturam Sky Fortress',
	'Linkgate Foundry',
	'Hall of Knowledge',
	'Beshmundirs Tempel',
	'Rentus-Basis',
	'Tiamats Unterschlupf'
)
WHERE sc.name = 'Aion Riftshade';
