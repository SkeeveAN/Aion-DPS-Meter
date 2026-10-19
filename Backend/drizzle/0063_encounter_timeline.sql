-- The time axis of a fight, from clients that send it: when each player used which skill (and died), when each helpful effect was on a player,
-- and the boss's health over time. Stored as compact JSON (tenths of a second from the start of the fight), read only by the encounter page.
CREATE TABLE `encounter_timelines` (
	`participant_id` integer PRIMARY KEY NOT NULL,
	`data_json` text NOT NULL,
	FOREIGN KEY (`participant_id`) REFERENCES `encounter_participants`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
ALTER TABLE `encounter_buff_usage` ADD `spans_json` text DEFAULT '[]' NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounters` ADD `boss_hp_json` text;
