-- Thumbs on the Daevanion boards and the skill builds of a character (profile page). profile_setups holds the states that are or were current; a state without a thumb is overwritten in place, so only rated states pile up.
CREATE TABLE `profile_setups` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`player_id` integer NOT NULL,
	`kind` text NOT NULL,
	`hash` text NOT NULL,
	`data_json` text NOT NULL,
	`class_id` integer,
	`thumbs` integer DEFAULT 0 NOT NULL,
	`created_at` text DEFAULT (datetime('now')) NOT NULL,
	`updated_at` text DEFAULT (datetime('now')) NOT NULL,
	`superseded_at` text,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE no action
);--> statement-breakpoint
CREATE UNIQUE INDEX `profile_setups_hash_idx` ON `profile_setups` (`player_id`,`kind`,`hash`);--> statement-breakpoint
CREATE INDEX `profile_setups_player_idx` ON `profile_setups` (`player_id`,`kind`,`superseded_at`);--> statement-breakpoint
CREATE INDEX `profile_setups_class_idx` ON `profile_setups` (`kind`,`class_id`,`superseded_at`,`thumbs`);--> statement-breakpoint
CREATE TABLE `setup_votes` (
	`setup_id` integer NOT NULL,
	`voter_hash` text NOT NULL,
	`ip_hash` text NOT NULL,
	`created_at` text DEFAULT (datetime('now')) NOT NULL,
	FOREIGN KEY (`setup_id`) REFERENCES `profile_setups`(`id`) ON UPDATE no action ON DELETE no action
);--> statement-breakpoint
CREATE UNIQUE INDEX `setup_votes_pk` ON `setup_votes` (`setup_id`,`voter_hash`);--> statement-breakpoint
CREATE INDEX `setup_votes_ip_idx` ON `setup_votes` (`setup_id`,`ip_hash`);--> statement-breakpoint
CREATE INDEX `setup_votes_voter_idx` ON `setup_votes` (`voter_hash`);
