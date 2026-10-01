CREATE TABLE `player_profiles` (
	`player_id` integer PRIMARY KEY NOT NULL,
	`source` text NOT NULL,
	`updated_at` text DEFAULT (current_timestamp) NOT NULL,
	`level` integer,
	`class_id` integer,
	`faction` integer,
	`gear_json` text DEFAULT '[]' NOT NULL,
	`skills_json` text DEFAULT '[]' NOT NULL,
	`daevanion_json` text DEFAULT '[]' NOT NULL,
	FOREIGN KEY (`player_id`) REFERENCES `players`(`id`) ON UPDATE no action ON DELETE no action
);
