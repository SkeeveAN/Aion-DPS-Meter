-- The paths of the players, bosses and other monsters of a boss fight, sent by the clients that were there (the "Upload for the replay" button): one row per uploader and entity,
-- the points gzipped as JSON {t: ms from start_ms, x, y}. The clients merge several uploaders' paths of one entity when they fetch them (replayTracks.ts).
CREATE TABLE `encounter_replay_tracks` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`encounter_id` integer NOT NULL,
	`uploader_hash` text NOT NULL,
	`kind` text NOT NULL,
	`name` text NOT NULL,
	`class_name` text,
	`is_self` integer DEFAULT 0 NOT NULL,
	`start_ms` integer NOT NULL,
	`points` integer NOT NULL,
	`data_gz` blob NOT NULL,
	`created_at` text DEFAULT (datetime('now')) NOT NULL,
	FOREIGN KEY (`encounter_id`) REFERENCES `encounters`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `replay_tracks_unique` ON `encounter_replay_tracks` (`encounter_id`,`uploader_hash`,`kind`,`name`);
--> statement-breakpoint
CREATE INDEX `replay_tracks_encounter_idx` ON `encounter_replay_tracks` (`encounter_id`);
