CREATE TABLE `bosses` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`instance_id` integer NOT NULL,
	`name` text NOT NULL,
	`npc_name_aliases` text DEFAULT '[]' NOT NULL,
	`created_at` text DEFAULT (current_timestamp) NOT NULL,
	FOREIGN KEY (`instance_id`) REFERENCES `instances`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `bosses_instance_id_idx` ON `bosses` (`instance_id`);--> statement-breakpoint
CREATE TABLE `encounter_participants` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`encounter_id` integer NOT NULL,
	`player_id` integer NOT NULL,
	`class_name` text NOT NULL,
	`faction` text DEFAULT '' NOT NULL,
	`total_damage` integer NOT NULL,
	`dps` real NOT NULL,
	`idps` real NOT NULL,
	`crit_rate_percent` real NOT NULL,
	`is_crit_rate_authoritative` integer DEFAULT false NOT NULL,
	`ap_total` integer,
	FOREIGN KEY (`encounter_id`) REFERENCES `encounters`(`id`) ON UPDATE no action ON DELETE no action,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `encounter_participants_encounter_id_idx` ON `encounter_participants` (`encounter_id`);--> statement-breakpoint
CREATE INDEX `encounter_participants_player_id_idx` ON `encounter_participants` (`player_id`);--> statement-breakpoint
CREATE TABLE `encounter_skill_usage` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`participant_id` integer NOT NULL,
	`skill_name` text NOT NULL,
	`hits` integer NOT NULL,
	`crit_hits` integer NOT NULL,
	`total_damage` integer NOT NULL,
	`min_hit` integer NOT NULL,
	`max_hit` integer NOT NULL,
	FOREIGN KEY (`participant_id`) REFERENCES `encounter_participants`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `encounter_skill_usage_participant_id_idx` ON `encounter_skill_usage` (`participant_id`);--> statement-breakpoint
CREATE TABLE `encounters` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`boss_id` integer NOT NULL,
	`started_at` text NOT NULL,
	`ended_at` text NOT NULL,
	`duration_seconds` real NOT NULL,
	`group_idps` real NOT NULL,
	`roster_fingerprint` text NOT NULL,
	`merged_upload_count` integer DEFAULT 1 NOT NULL,
	`created_at` text DEFAULT (current_timestamp) NOT NULL,
	FOREIGN KEY (`boss_id`) REFERENCES `bosses`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `encounters_boss_id_idx` ON `encounters` (`boss_id`);--> statement-breakpoint
CREATE INDEX `encounters_started_at_idx` ON `encounters` (`started_at`);--> statement-breakpoint
CREATE TABLE `instances` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`name` text NOT NULL,
	`sort_order` integer DEFAULT 0 NOT NULL,
	`created_at` text DEFAULT (current_timestamp) NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `instances_name_unique` ON `instances` (`name`);--> statement-breakpoint
CREATE TABLE `players` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`name` text NOT NULL,
	`name_normalized` text NOT NULL,
	`first_seen_at` text DEFAULT (current_timestamp) NOT NULL,
	`last_seen_at` text DEFAULT (current_timestamp) NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX `players_name_normalized_idx` ON `players` (`name_normalized`);--> statement-breakpoint
CREATE TABLE `uploads` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`received_at` text DEFAULT (current_timestamp) NOT NULL,
	`client_version` text DEFAULT '' NOT NULL,
	`uploader_reported_name` text NOT NULL,
	`ip_hash` text NOT NULL,
	`matched_encounter_id` integer,
	`status` text DEFAULT 'pending' NOT NULL,
	`raw_payload_json` text NOT NULL,
	FOREIGN KEY (`matched_encounter_id`) REFERENCES `encounters`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `uploads_matched_encounter_id_idx` ON `uploads` (`matched_encounter_id`);