-- Two corrections from the user's own, more detailed 4.8 instance table:
--
-- 1. "Tiamats Festung" (mapCode IDTiamat_1, en "Tiamat Stronghold" per
--    assets/places/instances_multilang.json) was never added - only its sibling "Tiamats
--    Unterschlupf" (IDTiamat_2) was. Its endboss, Brigade General Tahabata, is now confirmed
--    directly against aionriftshade.com's own game install (client_strings_dic_monster.xml id
--    STR_DIC_M_IDTiamat_Tahabata_Named_60_Ah -> "Brigadegeneral Tahabata"; the nameplate table
--    STR_IDTiamat_Tahabata_Named_60_Ah confirms the same) and against assets/npcs/npcs_en_4x.json
--    (id 219358, "Brigade General Tahabata", Heroic).
--
-- 2. Migration 0010 named the Belsagos instance "Linkgate Foundry" - per the user, that's actually
--    a DIFFERENT, separate instance (variable endboss depending on key stage, not yet confirmed
--    anywhere in this app's own data). The Belsagos zone's real name is "Baruna Research
--    Laboratory" ("Baruna-Forschungslabor" - matches its boss's own dic_monster lore text found
--    when Belsagos was first added). Renamed in place (server_catalog_instances/bosses reference
--    it by id, unaffected) - app.js/i18n.js/EndBossDatabase.cs updated to match in the same commit.
UPDATE `instances` SET `name` = 'Baruna Research Laboratory' WHERE `name` = 'Linkgate Foundry';
--> statement-breakpoint

INSERT OR IGNORE INTO `instances` (`name`, `sort_order`) VALUES
	('Tiamats Festung', 0);
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Tiamats Festung'), 'Brigade General Tahabata');
--> statement-breakpoint

-- Tiamats Festung is part of Aion Riftshade's own known list, same as its Tiamats Unterschlupf
-- sibling (see migration 0014) - "Baruna Research Laboratory" needs no new row here, it already has
-- one under its old name (server_catalog_instances references by instance_id, unaffected by the
-- rename above).
INSERT INTO `server_catalog_instances` (`server_catalog_id`, `instance_id`)
SELECT sc.id, i.id
FROM `server_catalog` sc
JOIN `instances` i ON i.name = 'Tiamats Festung'
WHERE sc.name = 'Aion Riftshade';
