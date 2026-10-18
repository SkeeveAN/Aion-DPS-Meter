-- Which Elyos server shares its world with which Asmodian server (NC's matchmaking notices), one row per pairing and period; valid_to is null while it is current.
CREATE TABLE `server_matchmaking` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`region` text NOT NULL,
	`elyos_server` text NOT NULL,
	`asmodian_server` text NOT NULL,
	`elyos_catalog_id` integer REFERENCES `server_catalog`(`id`),
	`asmodian_catalog_id` integer REFERENCES `server_catalog`(`id`),
	`phase` text,
	`updated_label` text,
	`notice_id` text,
	`valid_from` integer NOT NULL,
	`valid_to` integer
);--> statement-breakpoint
CREATE INDEX `server_matchmaking_region_open_idx` ON `server_matchmaking` (`region`,`valid_to`);
