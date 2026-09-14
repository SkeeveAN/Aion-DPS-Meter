-- Seven more instances from the user's own detailed 4.8 instance list, added on the user's
-- explicit decision (per the conversation: "Deine Liste als gegeben übernehmen" after the client's
-- German L10N pack turned out to be gone and the English one/its Riftshade-specific override both
-- turned out to be non-plain-zip/obfuscated - see that conversation for the full story). UNLIKE
-- every other instance/boss in this app, these names are NOT independently verified against any
-- client string dump - taken directly from the user's own report. Deliberately NOT added to the
-- C# client's EndBossDatabase.cs allowlist for that same reason (see that file's own provenance
-- rule) - a real upload for any of these bosses will still be rejected by the client until someone
-- verifies the real names and adds them there too.
INSERT OR IGNORE INTO `instances` (`name`, `sort_order`) VALUES
	('Mantor', 0),
	("Jormungand's Bridge", 0),
	('Linkgate Foundry', 0),
	('Lost Rentus Base', 0),
	('Lost Refuge', 0),
	("Tiamat's Hidden Space", 0),
	('Makarna', 0);
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`, `is_solo`) VALUES
	((SELECT id FROM instances WHERE name = 'Mantor'), 'Nasto', 1);
--> statement-breakpoint

-- Jormungand's Bridge - per the user, "Spirited Velkur bzw. Fugitive Mazikin/Asachin + Velkur":
-- read as three co-equal bosses (same pattern as Danuar Sanctuary's own three), with "Spirited
-- Velkur" as an alternate name for the same Velkur.
INSERT INTO `bosses` (`instance_id`, `name`, `npc_name_aliases`) VALUES
	((SELECT id FROM instances WHERE name = "Jormungand's Bridge"), 'Fugitive Mazikin', '[]'),
	((SELECT id FROM instances WHERE name = "Jormungand's Bridge"), 'Fugitive Asachin', '[]'),
	((SELECT id FROM instances WHERE name = "Jormungand's Bridge"), 'Velkur', '["Spirited Velkur"]');
--> statement-breakpoint

-- Hall of Knowledge (added migration 0010 with no boss at all, on the ORIGINAL list's claim of "no
-- classic endboss") - the user's later, more detailed list says otherwise: two scenario-dependent
-- bosses.
INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Hall of Knowledge'), 'Secret Test Subject 48123-A'),
	((SELECT id FROM instances WHERE name = 'Hall of Knowledge'), 'Doomtread Kurores');
--> statement-breakpoint

-- "Lost" variants and Tiamat's Hidden Space/Makarna - per the user's own list these are separate
-- instance rows, not just a difficulty toggle on an existing one (unlike the Occupied/Seized/
-- Infernal/Lucky variants from the FIRST list, which turned out to share their base instance's own
-- single mapCode - see migration 0010's own remarks). Boss names reused where the user's list
-- reuses them (e.g. Vasharti again for Lost Rentus Base) - a real, different NPC/encounter in a
-- separate instance, same "same name, different bosses.id" pattern as Sauro/Tahmes.
INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Lost Rentus Base'), 'Brigade General Vasharti');
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Lost Refuge'), 'Warmage Suyaroka'),
	((SELECT id FROM instances WHERE name = 'Lost Refuge'), 'Chief Medic Tagnu'),
	((SELECT id FROM instances WHERE name = 'Lost Refuge'), 'Virulent Ukahim');
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = "Tiamat's Hidden Space"), 'Tiamat');
--> statement-breakpoint

INSERT INTO `bosses` (`instance_id`, `name`) VALUES
	((SELECT id FROM instances WHERE name = 'Makarna'), 'Beritrakt');
--> statement-breakpoint

-- All seven are Aion Riftshade's own 4.8 content, same as everything else from the user's list.
INSERT INTO `server_catalog_instances` (`server_catalog_id`, `instance_id`)
SELECT sc.id, i.id
FROM `server_catalog` sc
JOIN `instances` i ON i.name IN (
	'Mantor',
	"Jormungand's Bridge",
	'Linkgate Foundry',
	'Lost Rentus Base',
	'Lost Refuge',
	"Tiamat's Hidden Space",
	'Makarna'
)
WHERE sc.name = 'Aion Riftshade';
