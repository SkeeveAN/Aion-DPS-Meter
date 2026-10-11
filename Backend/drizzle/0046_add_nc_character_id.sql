-- NC's own id of the character a check matched (url-decoded base64url, like player_nc_character.character_id); together with the server id it
-- identifies the character for good, across renames. Filled when a check confirms a player; the ones already known are copied over.
ALTER TABLE `player_nc_match` ADD `nc_character_id` text;
--> statement-breakpoint
UPDATE `player_nc_match` SET `nc_character_id` = (SELECT c.`character_id` FROM `player_nc_character` c WHERE c.`player_id` = `player_nc_match`.`player_id` AND c.`character_id` IS NOT NULL AND c.`nc_server_id` = `player_nc_match`.`nc_server_id`) WHERE `status` = 'verified';
