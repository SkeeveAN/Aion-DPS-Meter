CREATE TABLE `servers` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`fingerprint` text NOT NULL,
	`display_name` text,
	`first_seen_at` text DEFAULT (current_timestamp) NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `servers_fingerprint_unique` ON `servers` (`fingerprint`);--> statement-breakpoint
-- Backfill bucket for every row that predates server tracking. Deliberately NOT a guess at which
-- real server ("70.0.0.150:10241" etc.) those old rows actually came from - see constants.ts'
-- UNKNOWN_SERVER_FINGERPRINT remarks for why guessing here would be actively unsafe. Every upload
-- from here on carries a real fingerprint (see uploadSchema.ts), so this row only ever grows by
-- historical backfill, never by a real client upload.
INSERT INTO `servers` (`fingerprint`, `display_name`) VALUES ('unattributed-pre-server-tracking', 'Unbekannt (vor Server-Erkennung)');--> statement-breakpoint
DROP INDEX IF EXISTS `players_name_normalized_idx`;--> statement-breakpoint
ALTER TABLE `players` ADD `server_id` integer REFERENCES servers(id);--> statement-breakpoint
UPDATE `players` SET `server_id` = (SELECT `id` FROM `servers` WHERE `fingerprint` = 'unattributed-pre-server-tracking') WHERE `server_id` IS NULL;--> statement-breakpoint
CREATE UNIQUE INDEX `players_server_id_name_normalized_idx` ON `players` (`server_id`,`name_normalized`);--> statement-breakpoint
ALTER TABLE `encounters` ADD `server_id` integer REFERENCES servers(id);--> statement-breakpoint
UPDATE `encounters` SET `server_id` = (SELECT `id` FROM `servers` WHERE `fingerprint` = 'unattributed-pre-server-tracking') WHERE `server_id` IS NULL;--> statement-breakpoint
CREATE INDEX `encounters_server_id_boss_id_idx` ON `encounters` (`server_id`,`boss_id`);--> statement-breakpoint
ALTER TABLE `uploads` ADD `server_id` integer REFERENCES servers(id);--> statement-breakpoint
UPDATE `uploads` SET `server_id` = (SELECT `id` FROM `servers` WHERE `fingerprint` = 'unattributed-pre-server-tracking') WHERE `server_id` IS NULL;
