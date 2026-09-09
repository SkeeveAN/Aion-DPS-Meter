ALTER TABLE `server_catalog` ADD `active` integer DEFAULT true NOT NULL;
--> statement-breakpoint
-- Per the user: these 8 researched entries are marked inactive rather than deleted (see schema.ts's
-- own remarks) - GET /api/server-catalog stops offering them, but the row (and whatever it's
-- already linked to) stays intact.
UPDATE `server_catalog` SET `active` = false WHERE `name` IN (
	'AlfaAion',
	'Elden Aion',
	'Aion Reborn',
	'AionPT',
	'World Of Aion',
	'InfiniteAION',
	'HiveGamez Aion Classic',
	'NOVArpg'
);