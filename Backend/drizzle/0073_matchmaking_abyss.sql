-- The Abyss pairings of NC's matchmaking notice (any faction combination), next to the rift pairs of server_matchmaking.
CREATE TABLE `server_matchmaking_abyss` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`region` text NOT NULL,
	`server1` text NOT NULL,
	`server2` text NOT NULL,
	`faction1` text NOT NULL,
	`faction2` text NOT NULL,
	`server1_id` integer REFERENCES `servers`(`id`),
	`server2_id` integer REFERENCES `servers`(`id`),
	`notice_id` text,
	`valid_from` integer NOT NULL,
	`valid_to` integer
);
--> statement-breakpoint
CREATE INDEX `server_matchmaking_abyss_region_open_idx` ON `server_matchmaking_abyss` (`region`,`valid_to`);
