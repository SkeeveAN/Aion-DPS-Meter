CREATE TABLE `encounter_buff_usage` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`participant_id` integer NOT NULL,
	`skill_name` text NOT NULL,
	`casts` integer NOT NULL,
	FOREIGN KEY (`participant_id`) REFERENCES `encounter_participants`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE INDEX `encounter_buff_usage_participant_id_idx` ON `encounter_buff_usage` (`participant_id`);