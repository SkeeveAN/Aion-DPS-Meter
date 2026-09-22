-- Game dimension (classic "aion" vs "aion2") plus URL slugs and English names, ahead of Aion 2
-- content on the website. Column adds and index swaps only - no table rebuild, so this is safe on
-- the live WAL database. Existing rows default to 'aion'; slugs/name_en are filled by
-- src/db/backfill.ts right after migrating (runs from db:migrate), not by SQL, because the
-- English names live in public/game-data.js and slugging needs Unicode handling.
CREATE TABLE `boss_mechanics` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`boss_id` integer,
	`instance_id` integer,
	`sort_order` integer DEFAULT 0 NOT NULL,
	`key` text NOT NULL,
	`trigger_type` text NOT NULL,
	`trigger_pct` integer,
	`trigger_label` text DEFAULT '' NOT NULL,
	`severity` text NOT NULL,
	`action` text DEFAULT '' NOT NULL,
	`detail` text DEFAULT '' NOT NULL,
	`position_sheet` text,
	`source` text DEFAULT 'derived' NOT NULL,
	FOREIGN KEY (`boss_id`) REFERENCES `bosses`(`id`) ON UPDATE no action ON DELETE no action,
	FOREIGN KEY (`instance_id`) REFERENCES `instances`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `boss_mechanics_boss_key_idx` ON `boss_mechanics` (`boss_id`,`key`);--> statement-breakpoint
CREATE UNIQUE INDEX `boss_mechanics_instance_key_idx` ON `boss_mechanics` (`instance_id`,`key`);--> statement-breakpoint
CREATE TABLE `boss_npc_ids` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`boss_id` integer NOT NULL,
	`npc_id` integer NOT NULL,
	`difficulty` text,
	FOREIGN KEY (`boss_id`) REFERENCES `bosses`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `boss_npc_ids_npc_id_unique` ON `boss_npc_ids` (`npc_id`);--> statement-breakpoint
CREATE INDEX `boss_npc_ids_boss_id_idx` ON `boss_npc_ids` (`boss_id`);--> statement-breakpoint
DROP INDEX IF EXISTS `instances_name_unique`;--> statement-breakpoint
ALTER TABLE `instances` ADD `game` text DEFAULT 'aion' NOT NULL;--> statement-breakpoint
ALTER TABLE `instances` ADD `slug` text;--> statement-breakpoint
ALTER TABLE `instances` ADD `name_en` text;--> statement-breakpoint
ALTER TABLE `instances` ADD `category` text;--> statement-breakpoint
ALTER TABLE `instances` ADD `source` text DEFAULT 'curated' NOT NULL;--> statement-breakpoint
CREATE UNIQUE INDEX `instances_game_name_idx` ON `instances` (`game`,`name`);--> statement-breakpoint
CREATE UNIQUE INDEX `instances_game_slug_idx` ON `instances` (`game`,`slug`);--> statement-breakpoint
ALTER TABLE `bosses` ADD `slug` text;--> statement-breakpoint
ALTER TABLE `bosses` ADD `name_en` text;--> statement-breakpoint
ALTER TABLE `bosses` ADD `source` text DEFAULT 'curated' NOT NULL;--> statement-breakpoint
CREATE INDEX `bosses_slug_idx` ON `bosses` (`slug`);--> statement-breakpoint
ALTER TABLE `server_catalog` ADD `game` text DEFAULT 'aion' NOT NULL;--> statement-breakpoint
ALTER TABLE `server_catalog` ADD `slug` text;--> statement-breakpoint
-- Second unassigned bucket (see constants.ts UNASSIGNED_INSTANCE_NAME): an unknown Aion 2 boss must
-- never land next to unknown classic-Aion bosses. Legal now that the name is only unique per game.
INSERT INTO `instances` (`name`, `name_en`, `slug`, `game`, `sort_order`)
SELECT 'Unbekannt / nicht zugeordnet', 'Unassigned', 'unassigned', 'aion2', -1
WHERE NOT EXISTS (SELECT 1 FROM `instances` WHERE `name` = 'Unbekannt / nicht zugeordnet' AND `game` = 'aion2');
