-- What the website's buff columns need besides the name and the count: the game's effect id (icon, localized name), how much of the fight the effect was on the player, and who gave it.
ALTER TABLE `encounter_buff_usage` ADD `skill_id` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounter_buff_usage` ADD `uptime_permille` integer DEFAULT 0 NOT NULL;
--> statement-breakpoint
ALTER TABLE `encounter_buff_usage` ADD `givers_json` text DEFAULT '[]' NOT NULL;
