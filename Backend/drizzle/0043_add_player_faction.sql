-- The faction of a player, kept on the player: a fight reports it, and so does a profile upload from a client that knows it.
-- Players only seen in a group (never in a recorded fight) had none before. Existing rows get the faction of their latest fight.
ALTER TABLE `players` ADD `faction` text DEFAULT '' NOT NULL;
--> statement-breakpoint
UPDATE `players` SET `faction` = COALESCE((SELECT ep.`faction` FROM `encounter_participants` ep WHERE ep.`player_id` = `players`.`id` AND ep.`faction` <> '' ORDER BY ep.`id` DESC LIMIT 1), '');
