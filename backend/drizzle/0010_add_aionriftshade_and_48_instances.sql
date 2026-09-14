-- aionriftshade.com: a new 4.8 private server. Confirmed a real, running 4.8 server by checking
-- its own game install directly (D:\Spiele\AION\Aion Riftshade\L10N - see below), not just taken on
-- faith from the URL.
INSERT OR IGNORE INTO `server_catalog` (`name`, `version`, `kind`) VALUES
	('AionRiftshade', '4.8', 'private');
--> statement-breakpoint

-- Three 4.8 instances not yet in the shared instances/bosses tables (see schema.ts's own remarks on
-- why instances/bosses are unscoped - real content, not per-server). Names verified directly
-- against aionriftshade.com's own client install: L10N/deu/Data/data.pak's
-- strings/client_strings_dic_place.xml, client_strings_dic_etc.xml and client_strings_monster.xml
-- (a plain zip, UTF-16 XML - same extraction method documented in assets/README.md and the client's
-- own EndBossDatabase.cs/InstanceTierDatabase.cs). The server's own English L10N pack was still
-- mid-download at the time (per the user), so German is what got checked; English names below
-- match aioncodex's own assets/npcs/npcs_en_4x.json dataset, one step less certain than a
-- from-this-server English verification would be - see backend/public/i18n.js's own per-entry
-- remarks.
-- OR IGNORE (name is UNIQUE): safe no-op if a row already exists rather than failing the whole
-- migration - this app deliberately never learns the real production state of these
-- hand-curated tables from git (see backend/README.md), so a plain INSERT here could not safely
-- assume any of these three are actually still missing.
INSERT OR IGNORE INTO `instances` (`name`, `sort_order`) VALUES
	-- mapCode IDStation, de "Aturam-Himmelsfestung" - solo instance.
	('Aturam Sky Fortress', 0),
	-- mapCode IDLDF4Re_01 ("Baruna-Forschungslabor" in the client's own lore text) - no confirmed
	-- "whole zone" display string of its own in the client's dictionary, so the name below is the
	-- community/user-supplied one rather than a client-verified one - solo instance.
	('Linkgate Foundry', 0),
	-- mapCode IDLDF5RE_solo, de "Halle des Wissens" (a direct match for "Hall of Knowledge") - a
	-- solo puzzle/lore instance with no classic endboss, per the user, so no boss row below.
	('Hall of Knowledge', 0),
	-- Already pre-staged in app.js/i18n.js but, per those files' own remarks, never confirmed to
	-- have a real row yet either - OR IGNORE covers both cases.
	('Ophidan Bridge', 0);
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`, `npc_name_aliases`, `is_solo`) VALUES
	(
		(SELECT id FROM instances WHERE name = 'Aturam Sky Fortress'),
		'Ashunatal Shadowslip',
		'["Ashunatal-Schattengleiter"]',
		1
	),
	-- Belsagos - one NPC across 3 escalating named states found in the client's own strings
	-- (Wilder/Verletzter/Unberechenbarer = Wild/Injured/Unpredictable), all aliased to one boss row
	-- the same way Danuar Reliquary's Modor already covers her own two phases.
	(
		(SELECT id FROM instances WHERE name = 'Linkgate Foundry'),
		'Belsagos',
		'["Wilder Belsagos","Verletzter Belsagos","Unberechenbarer Belsagos"]',
		1
	);
--> statement-breakpoint

-- Ophidan Bridge's PVE encounter (1 mage + 2 turrets, all three co-equal real bosses per the
-- client's own EndBossDatabase.cs) - the instance row already existed pre-staged with no bosses.
INSERT INTO `bosses` (`instance_id`, `name`, `npc_name_aliases`) VALUES
	(
		(SELECT id FROM instances WHERE name = 'Ophidan Bridge'),
		'Vera',
		'[]'
	),
	(
		(SELECT id FROM instances WHERE name = 'Ophidan Bridge'),
		'Beritran Support Magus',
		'["Verstärkungsmagier der Reserveeinheit"]'
	),
	(
		(SELECT id FROM instances WHERE name = 'Ophidan Bridge'),
		'Surkana Aetherturret',
		'["Surkana-Panzerabwehrätherkanone"]'
	);
