-- The pairings link to the servers known from uploads (servers), not to the client's picker list (server_catalog). The table only holds data the job re-reads from NC's notices, so it is rebuilt.
DROP TABLE `server_matchmaking`;--> statement-breakpoint
CREATE TABLE `server_matchmaking` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`region` text NOT NULL,
	`elyos_server` text NOT NULL,
	`asmodian_server` text NOT NULL,
	`elyos_server_id` integer REFERENCES `servers`(`id`),
	`asmodian_server_id` integer REFERENCES `servers`(`id`),
	`phase` text,
	`updated_label` text,
	`notice_id` text,
	`valid_from` integer NOT NULL,
	`valid_to` integer
);--> statement-breakpoint
CREATE INDEX `server_matchmaking_region_open_idx` ON `server_matchmaking` (`region`,`valid_to`);
