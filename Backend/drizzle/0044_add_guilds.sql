-- A legion is a name on ONE server. Until now it was only the name on each player, so two legions of the same name on different
-- servers shared one pile as long as their members sat on a wrong server, and a legion had no identity of its own to keep its link.
CREATE TABLE `guilds` (
	`id` integer PRIMARY KEY AUTOINCREMENT NOT NULL,
	`name` text NOT NULL,
	`name_normalized` text NOT NULL,
	`server_id` integer NOT NULL,
	`slug` text,
	`created_at` text DEFAULT (current_timestamp) NOT NULL,
	FOREIGN KEY (`server_id`) REFERENCES `servers`(`id`) ON UPDATE no action ON DELETE no action
);
--> statement-breakpoint
CREATE UNIQUE INDEX `guilds_server_id_name_normalized_idx` ON `guilds` (`server_id`,`name_normalized`);
--> statement-breakpoint
CREATE UNIQUE INDEX `guilds_slug_idx` ON `guilds` (`slug`);
--> statement-breakpoint
ALTER TABLE `players` ADD `guild_id` integer REFERENCES guilds(id);
--> statement-breakpoint
CREATE INDEX `players_guild_id_idx` ON `players` (`guild_id`);
--> statement-breakpoint
INSERT INTO `guilds` (`name`, `name_normalized`, `server_id`)
SELECT MIN(TRIM(`guild`)), LOWER(TRIM(`guild`)), `server_id` FROM `players`
WHERE `guild` IS NOT NULL AND TRIM(`guild`) <> '' AND `server_id` IS NOT NULL
GROUP BY `server_id`, LOWER(TRIM(`guild`));
--> statement-breakpoint
UPDATE `players` SET `guild_id` = (SELECT g.`id` FROM `guilds` g WHERE g.`server_id` = `players`.`server_id` AND g.`name_normalized` = LOWER(TRIM(`players`.`guild`)))
WHERE `guild` IS NOT NULL AND TRIM(`guild`) <> '';
